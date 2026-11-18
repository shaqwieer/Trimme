using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Shouldly;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Scheduling;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.Modules.Bookings.Application;
using Trimme.Modules.Bookings.Domain;
using static Trimme.IntegrationTests.Bookings.BookingTestData;

namespace Trimme.IntegrationTests.Bookings;

/// <summary>
/// D-148, the customer's pages (the client's document, section 4): a short reference («GH-» and 5 characters) that is
/// drawn again when taken; cancelling and changing until the start by default; a package with other services in one
/// booking (their prices and lengths add up, one barber for all, a service already in the package refused); closed days
/// and opening windows for the date strip and the greyed times; and the admin's demo mark on every public shop shape.
/// </summary>
public sealed partial class CustomerPagesTests(PostgresFixture postgres)
{
    private static DateOnly Target => TodayAt(DateTimeOffset.UtcNow).AddDays(2);

    [GeneratedRegex("^GH-[ABCDEFGHJKMNPQRSTUVWXYZ23456789]{5}$")]
    private static partial Regex ShortReference();

    private static async Task<object?> ScalarAsync(TrimmeApiFactory factory, string sql, CancellationToken ct, object? parameter = null)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        if (parameter is not null)
        {
            command.Parameters.AddWithValue("p", parameter);
        }

        return await command.ExecuteScalarAsync(ct);
    }

    [Fact]
    public async Task References_AreShort_FoundBySearchInAnyCase_AndDrawnAgainWhenTaken()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "d148_reference", ct);
        using var customer = await CustomerAsync(w.Factory, "نورة", ct);
        var booking = await OkAsync(BookAsync(customer, w.SlugA, w.Haircut, w.Faisal, At(Target, 10), ct), ct, HttpStatusCode.Created);
        var reference = booking.GetProperty("reference").GetString()!;
        ShortReference().IsMatch(reference).ShouldBeTrue(reference);

        // The shop finds it by the reference typed in lower case (it is read out over the phone).
        var found = await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/bookings?search={reference.ToLowerInvariant()}", ct), ct);
        found.GetProperty("items").EnumerateArray().Select(b => b.GetProperty("id").GetGuid()).ShouldBe([booking.GetProperty("id").GetGuid()]);

        // A new booking whose drawn reference is taken (in any shop) draws again and is saved.
        await using var scope = w.Factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        using var system = services.GetRequiredService<ISystemDataScope>().Begin();
        var db = services.GetRequiredService<TrimmeDbContext>();
        var shop = new ShopId(w.Shops.B.ShopId);
        var offer = (await services.GetRequiredService<IBookableOfferCatalog>().FindAsync(shop, w.ServiceB, null, ct)).ShouldNotBeNull();
        var salem = (await services.GetRequiredService<IProfessionalDirectory>().FindAsync(new ProfessionalId(w.ProB), ct)).ShouldNotBeNull();
        var walkIn = Booking.CreateWalkIn(
            new BookingId(Guid.CreateVersion7()), shop, "زائر", new BookedProfessional(salem.Id, salem.NameAr, salem.NameEn), BookingMapping.Snapshot(offer),
            At(Target, 12), startsNow: false, null, BookingActor.System, DateTimeOffset.UtcNow);
        typeof(Booking).GetProperty(nameof(Booking.Reference))!.SetValue(walkIn, reference);
        db.Add(walkIn);
        await BookingReferences.SaveNewAsync(db, walkIn, ct);
        walkIn.Reference.ShouldNotBe(reference);
        ShortReference().IsMatch(walkIn.Reference).ShouldBeTrue(walkIn.Reference);
        (await ScalarAsync(w.Factory, "SELECT count(*) FROM bookings.bookings WHERE reference = @p", ct, reference)).ShouldBe(1L);
    }

    [Fact]
    public async Task ByDefault_TheCustomerCancelsAndChanges_UntilTheStart()
    {
        var ct = TestContext.Current.CancellationToken;

        // 9:00 two days ahead, so a booking at 10:30 is 90 minutes away: inside the old two-hour window, outside the new rule.
        var clock = new FakeTimeProvider(At(Target, 9).ToUniversalTime());
        await using var w = await ArrangeAsync(postgres, "d148_cutoff", ct, clock);
        (await OkAsync(w.Admin.GetAsync("/api/v1/admin/settings", ct), ct)).GetProperty("cancellationCutoffMinutes").GetInt32().ShouldBe(0);
        using var anonymous = ApiSession.Create(w.Factory);
        (await OkAsync(anonymous.GetAsync($"/api/v1/public/shops/{w.SlugA}", ct), ct)).GetProperty("cancellationCutoffMinutes").GetInt32().ShouldBe(0);

        using var customer = await CustomerAsync(w.Factory, "نورة", ct);
        var booking = await OkAsync(BookAsync(customer, w.SlugA, w.Haircut, w.Faisal, At(Target, 10, 30), ct), ct, HttpStatusCode.Created);
        booking.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()).ShouldBe(["Cancel", "Reschedule"], ignoreOrder: true);
        booking.GetProperty("cancellationCutoffMinutes").GetInt32().ShouldBe(0);
        await OkAsync(customer.GetAsync($"/api/v1/me/bookings/{booking.GetProperty("id").GetGuid()}/reschedule/slots?date={Iso(Target)}", ct), ct);
        await OkAsync(customer.PostAsync(
            $"/api/v1/me/bookings/{booking.GetProperty("id").GetGuid()}/cancel", new { version = booking.GetProperty("version").GetUInt32() }, ct), ct);
    }

    [Fact]
    public async Task APackageWithOtherServices_AddsUp_NeedsOneBarberForAll_AndRefusesAServiceAlreadyInIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "d148_package", ct);

        // A dye only Faisal does: 40 minutes, 50 SAR.
        var dye = (await OkAsync(w.OwnerA.PostAsync("/api/v1/shop/services",
                new { nameAr = "صبغة", nameEn = "Dye", price = 50m, durationMinutes = 40, onlineBookable = true, professionalIds = new[] { w.Faisal } }, ct),
            ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        using var anonymous = ApiSession.Create(w.Factory);
        string Slots(string extra) => $"/api/v1/public/shops/{w.SlugA}/availability/slots?packageId={w.Package}&date={Iso(Target)}{extra}";

        // Only a barber who does the package and the dye is offered; a service already in the package is refused.
        var slots = await OkAsync(anonymous.GetAsync(Slots($"&serviceIds={dye}"), ct), ct);
        slots.GetProperty("slots").EnumerateArray().SelectMany(s => s.GetProperty("professionalIds").EnumerateArray().Select(p => p.GetGuid()))
            .Distinct().ShouldBe([w.Faisal]);
        await FailsAsync(anonymous.GetAsync(Slots($"&serviceIds={w.Haircut}"), ct), HttpStatusCode.BadRequest, "booking.service_in_package", ct);
        await FailsAsync(anonymous.GetAsync(Slots($"&serviceIds={dye}&professionalId={w.Omar}"), ct), HttpStatusCode.UnprocessableEntity, null, ct);

        using var customer = await CustomerAsync(w.Factory, "نورة", ct);
        Task<HttpResponseMessage> Book(Guid[] serviceIds) => customer.SendAsync(
            HttpMethod.Post, "/api/v1/bookings",
            new { shopSlug = w.SlugA, packageId = w.Package, serviceIds, professionalId = w.Faisal, startsAt = At(Target, 10), note = (string?)null }, ct,
            headers: Key());
        await FailsAsync(Book([w.Beard]), HttpStatusCode.BadRequest, "booking.service_in_package", ct);
        var booking = await OkAsync(Book([dye]), ct, HttpStatusCode.Created);
        var item = booking.GetProperty("item");
        item.GetProperty("packageId").GetGuid().ShouldBe(w.Package);
        item.GetProperty("serviceId").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("price").GetDecimal().ShouldBe(130m);
        item.GetProperty("durationMinutes").GetInt32().ShouldBe(100);
        item.GetProperty("nameAr").GetString().ShouldBe("باقة + صبغة");
        booking.GetProperty("endsAt").GetDateTimeOffset().ShouldBe(At(Target, 11, 40));
        var items = item.GetProperty("packageItems").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("serviceId").GetGuid()).ShouldBe([w.Haircut, w.Beard, dye]);
        items[0].GetProperty("price").ValueKind.ShouldBe(JsonValueKind.Null);
        items[2].GetProperty("price").GetDecimal().ShouldBe(50m);
        var id = booking.GetProperty("id").GetGuid();

        // Rescheduling rebuilds the same offer: only Faisal can take it, so no colleague is offered.
        await OkAsync(customer.GetAsync($"/api/v1/me/bookings/{id}/reschedule/slots?date={Iso(Target.AddDays(1))}", ct), ct);
        using var faisal = await w.BarberAsync(w.Faisal, ct);
        (await OkAsync(faisal.GetAsync($"/api/v1/shop/bookings/{id}/professional-options", ct), ct))
            .GetProperty("professionals").EnumerateArray().ShouldBeEmpty();

        // The walk-in choices add up the same way.
        var options = await OkAsync(faisal.GetAsync($"/api/v1/shop/availability/walk-in?packageId={w.Package}&serviceIds={dye}&date={Iso(Target)}", ct), ct);
        options.GetProperty("durationMinutes").GetInt32().ShouldBe(100);
        options.GetProperty("professionals").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ShouldBe([w.Faisal]);

        // The dye booked with the package can be taken off until «انتهت الخدمة»; the package's own services cannot (D-147).
        Task<HttpResponseMessage> Remove(Guid serviceId, uint version) =>
            faisal.PostAsync($"/api/v1/shop/bookings/{id}/services/{serviceId}/delete", new { version }, ct);
        var current = (await OkAsync(faisal.GetAsync($"/api/v1/shop/bookings/{id}", ct), ct)).GetProperty("booking").GetProperty("version").GetUInt32();
        await FailsAsync(Remove(w.Haircut, current), HttpStatusCode.UnprocessableEntity, "booking.service_fixed", ct);
        var trimmed = await OkAsync(Remove(dye, current), ct);
        trimmed.GetProperty("item").GetProperty("price").GetDecimal().ShouldBe(80m);
        trimmed.GetProperty("item").GetProperty("durationMinutes").GetInt32().ShouldBe(60);
        trimmed.GetProperty("item").GetProperty("nameAr").GetString().ShouldBe("باقة");
    }

    [Fact]
    public async Task TheDateStrip_SaysClosed_AndTheTimes_ComeWithTheOpeningWindows()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "d148_closed", ct);
        var closed = Target.AddDays(1);
        await OkAsync(w.OwnerA.PostAsync("/api/v1/shop/schedule/closures", new { startDate = Iso(closed), endDate = Iso(closed), reason = "صيانة" }, ct), ct, HttpStatusCode.Created);

        using var anonymous = ApiSession.Create(w.Factory);
        var dates = await OkAsync(anonymous.GetAsync(
            $"/api/v1/public/shops/{w.SlugA}/availability/dates?serviceId={w.Haircut}&from={Iso(Target)}&to={Iso(Target.AddDays(2))}", ct), ct);
        var byDate = dates.GetProperty("dates").EnumerateArray().ToDictionary(d => d.GetProperty("date").GetString()!, d => d);
        byDate[Iso(closed)].GetProperty("closed").GetBoolean().ShouldBeTrue();
        byDate[Iso(closed)].GetProperty("slotCount").GetInt32().ShouldBe(0);
        byDate[Iso(Target)].GetProperty("closed").GetBoolean().ShouldBeFalse();

        // 09:00–21:00 Riyadh: the page lays out every time in that window and greys the taken ones.
        var slots = await OkAsync(anonymous.GetAsync($"/api/v1/public/shops/{w.SlugA}/availability/slots?serviceId={w.Haircut}&date={Iso(Target)}", ct), ct);
        var open = slots.GetProperty("open").EnumerateArray().Single();
        open.GetProperty("start").GetDateTimeOffset().ShouldBe(At(Target, 9));
        open.GetProperty("end").GetDateTimeOffset().ShouldBe(At(Target, 21));
        (await OkAsync(anonymous.GetAsync($"/api/v1/public/shops/{w.SlugA}/availability/slots?serviceId={w.Haircut}&date={Iso(closed)}", ct), ct))
            .GetProperty("open").EnumerateArray().ShouldBeEmpty();

        // The customer's reschedule strip and times say the same.
        using var customer = await CustomerAsync(w.Factory, "نورة", ct);
        var id = (await OkAsync(BookAsync(customer, w.SlugA, w.Haircut, w.Faisal, At(Target, 10), ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var mine = await OkAsync(customer.GetAsync($"/api/v1/me/bookings/{id}/reschedule/dates?from={Iso(Target)}&to={Iso(Target.AddDays(2))}", ct), ct);
        mine.GetProperty("dates").EnumerateArray().Single(d => d.GetProperty("date").GetString() == Iso(closed)).GetProperty("closed").GetBoolean().ShouldBeTrue();
        (await OkAsync(customer.GetAsync($"/api/v1/me/bookings/{id}/reschedule/slots?date={Iso(Target)}", ct), ct))
            .GetProperty("open").EnumerateArray().Single().GetProperty("start").GetDateTimeOffset().ShouldBe(At(Target, 9));
    }

    [Fact]
    public async Task TheAdmin_MarksADemoShop_AndEveryPublicShapeSaysSo()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "d148_demo", ct);

        // Discovery lists located shops only.
        foreach (var shopId in new[] { w.Shops.A.ShopId, w.Shops.B.ShopId })
        {
            await OkAsync(w.Admin.PutAsync($"/api/v1/admin/shops/{shopId}/location", new
            {
                latitude = 24.7743, longitude = 46.6380, addressLine = (string?)null, district = "الملقا", city = "الرياض",
                formattedAddress = "حي الملقا، الرياض", source = "Manual",
            }, ct), ct);
        }

        await FailsAsync(w.OwnerA.PutAsync($"/api/v1/admin/shops/{w.Shops.A.ShopId}/demo", new { isDemo = true }, ct), HttpStatusCode.Forbidden, null, ct);
        var marked = await OkAsync(w.Admin.PutAsync($"/api/v1/admin/shops/{w.Shops.A.ShopId}/demo", new { isDemo = true }, ct), ct);
        marked.GetProperty("isDemo").GetBoolean().ShouldBeTrue();
        (await ScalarAsync(w.Factory, "SELECT count(*) FROM administration.audit_entries WHERE action = 'shop.demo_changed' AND entity_id = @p", ct,
            w.Shops.A.ShopId.ToString())).ShouldBe(1L);

        using var anonymous = ApiSession.Create(w.Factory);
        (await OkAsync(anonymous.GetAsync($"/api/v1/public/shops/{w.SlugA}", ct), ct)).GetProperty("isDemo").GetBoolean().ShouldBeTrue();
        var results = (await OkAsync(anonymous.GetAsync("/api/v1/public/shops/search?pageSize=50", ct), ct)).GetProperty("items").EnumerateArray().ToList();
        results.Single(s => s.GetProperty("id").GetGuid() == w.Shops.A.ShopId).GetProperty("isDemo").GetBoolean().ShouldBeTrue();
        results.Where(s => s.GetProperty("id").GetGuid() == w.Shops.B.ShopId).ShouldAllBe(s => !s.GetProperty("isDemo").GetBoolean());

        (await OkAsync(w.Admin.PutAsync($"/api/v1/admin/shops/{w.Shops.A.ShopId}/demo", new { isDemo = false }, ct), ct)).GetProperty("isDemo").GetBoolean().ShouldBeFalse();
        (await OkAsync(anonymous.GetAsync($"/api/v1/public/shops/{w.SlugA}", ct), ct)).GetProperty("isDemo").GetBoolean().ShouldBeFalse();
    }
}
