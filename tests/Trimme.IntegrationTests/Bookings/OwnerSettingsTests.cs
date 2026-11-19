using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Scheduling;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.IntegrationTests.Identity;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.Modules.Bookings.Application;
using Trimme.Modules.Bookings.Domain;
using static Trimme.IntegrationTests.Bookings.BookingTestData;

namespace Trimme.IntegrationTests.Bookings;

/// <summary>
/// D-149, the owner's dashboard: each shop's own booking settings (confirmation, start interval, minimum notice, furthest
/// day, cleaning time) shape its slots and its bookings; the cleaning time is held after each booking, by the engine and by
/// the exclusion constraint; each barber takes their own time for a service, everywhere a length is used; barbers have
/// languages and the owner adds them by mobile; the shop's own sections; the shop is a men's barbershop; and the migration
/// maps an existing shop's data.
/// </summary>
public sealed class OwnerSettingsTests(PostgresFixture postgres)
{
    private static DateOnly Target => TodayAt(DateTimeOffset.UtcNow).AddDays(2);

    private static object Settings(bool manual, int step, int lead, int horizon, int buffer) =>
        new { requireManualConfirmation = manual, slotStepMinutes = step, minLeadTimeMinutes = lead, bookingHorizonDays = horizon, bufferMinutes = buffer };

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

    private static async Task ExecuteAsync(TrimmeApiFactory factory, string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<List<string>> TimesAsync(ApiSession session, string url, CancellationToken ct) =>
        [.. (await OkAsync(session.GetAsync(url, ct), ct)).GetProperty("slots").EnumerateArray().Select(s => s.GetProperty("localTime").GetString()!)];

    [Fact]
    public async Task EachShop_SetsItsOwnBookingRules_TheyShapeItsSlots_AndBarbersCannotChangeThem()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "d149_settings", ct);

        // Until the shop sets its own, the platform's are in force: 14 days ahead (D-149, 6-1), 5-minute starts, an hour's notice.
        var defaults = await OkAsync(w.OwnerA.GetAsync("/api/v1/shop/booking-settings", ct), ct);
        defaults.GetProperty("requireManualConfirmation").GetBoolean().ShouldBeFalse();
        defaults.GetProperty("slotStepMinutes").GetInt32().ShouldBe(5);
        defaults.GetProperty("minLeadTimeMinutes").GetInt32().ShouldBe(60);
        defaults.GetProperty("bookingHorizonDays").GetInt32().ShouldBe(14);
        defaults.GetProperty("bufferMinutes").GetInt32().ShouldBe(0);
        using var anonymous = ApiSession.Create(w.Factory);
        (await OkAsync(anonymous.GetAsync($"/api/v1/public/shops/{w.SlugA}/availability/dates?serviceId={w.Haircut}", ct), ct))
            .GetProperty("dates").GetArrayLength().ShouldBe(14);

        // Barbers operate, they do not set the shop's rules; out-of-range values are refused field by field.
        using var faisal = await w.BarberAsync(w.Faisal, ct);
        await FailsAsync(faisal.PutAsync("/api/v1/shop/booking-settings", Settings(false, 15, 0, 3, 10), ct), HttpStatusCode.Forbidden, null, ct);
        var invalid = await FailsAsync(w.OwnerA.PutAsync("/api/v1/shop/booking-settings", Settings(false, 7, 2000, 91, 7), ct), HttpStatusCode.BadRequest, null, ct);
        foreach (var field in new[] { "slotStepMinutes", "minLeadTimeMinutes", "bookingHorizonDays", "bufferMinutes" })
        {
            invalid.ShouldContain(field);
        }

        var set = await OkAsync(w.OwnerA.PutAsync("/api/v1/shop/booking-settings", Settings(true, 15, 0, 3, 0), ct), ct);
        set.GetProperty("slotStepMinutes").GetInt32().ShouldBe(15);
        set.GetProperty("bookingHorizonDays").GetInt32().ShouldBe(3);
        (await ScalarAsync(w.Factory, "SELECT count(*) FROM administration.audit_entries WHERE action = 'shop.booking_settings_changed' AND entity_id = @p", ct,
            w.Shops.A.ShopId.ToString())).ShouldBe(1L);

        // Starts every 15 minutes, three days (today included) on the strip, nothing past them; shop B keeps the platform's.
        var times = await TimesAsync(anonymous, $"/api/v1/public/shops/{w.SlugA}/availability/slots?serviceId={w.Haircut}&date={Iso(Target)}", ct);
        times.ShouldNotBeEmpty();
        times.Where(t => !t.EndsWith(":00", StringComparison.Ordinal) && !t.EndsWith(":15", StringComparison.Ordinal)
            && !t.EndsWith(":30", StringComparison.Ordinal) && !t.EndsWith(":45", StringComparison.Ordinal)).ShouldBeEmpty();
        times.ShouldContain("09:15");
        (await OkAsync(anonymous.GetAsync($"/api/v1/public/shops/{w.SlugA}/availability/dates?serviceId={w.Haircut}", ct), ct))
            .GetProperty("dates").GetArrayLength().ShouldBe(3);
        (await TimesAsync(anonymous, $"/api/v1/public/shops/{w.SlugA}/availability/slots?serviceId={w.Haircut}&date={Iso(Target.AddDays(1))}", ct)).ShouldBeEmpty();
        var slugB = (await OkAsync(w.Admin.GetAsync($"/api/v1/admin/shops/{w.Shops.B.ShopId}", ct), ct)).GetProperty("slug").GetString()!;
        (await TimesAsync(anonymous, $"/api/v1/public/shops/{slugB}/availability/slots?serviceId={w.ServiceB}&date={Iso(Target)}", ct)).ShouldContain("09:05");

        // Manual confirmation: an online booking waits for the shop.
        using var customer = await CustomerAsync(w.Factory, "نورة", ct);
        (await OkAsync(BookAsync(customer, w.SlugA, w.Haircut, w.Faisal, At(Target, 10, 15), ct), ct, HttpStatusCode.Created))
            .GetProperty("status").GetString().ShouldBe("Pending");
        await FailsAsync(BookAsync(customer, w.SlugA, w.Haircut, w.Faisal, At(Target, 11, 5), ct), HttpStatusCode.Conflict, null, ct);

        // The admin reads and sets the same settings from the shop's admin page; the owner cannot use the admin's.
        await FailsAsync(w.OwnerA.GetAsync($"/api/v1/admin/shops/{w.Shops.A.ShopId}/booking-settings", ct), HttpStatusCode.Forbidden, null, ct);
        (await OkAsync(w.Admin.GetAsync($"/api/v1/admin/shops/{w.Shops.A.ShopId}/booking-settings", ct), ct)).GetProperty("slotStepMinutes").GetInt32().ShouldBe(15);
        (await OkAsync(w.Admin.PutAsync($"/api/v1/admin/shops/{w.Shops.A.ShopId}/booking-settings", Settings(false, 30, 0, 14, 0), ct), ct))
            .GetProperty("requireManualConfirmation").GetBoolean().ShouldBeFalse();
        (await OkAsync(w.OwnerA.GetAsync("/api/v1/shop/booking-settings", ct), ct)).GetProperty("slotStepMinutes").GetInt32().ShouldBe(30);
    }

    [Fact]
    public async Task TheCleaningTime_HoldsTheBarber_AfterEachBooking_AndTheDatabaseKeepsItFree()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "d149_buffer", ct);
        await OkAsync(w.OwnerA.PutAsync("/api/v1/shop/booking-settings", Settings(false, 5, 0, 14, 10), ct), ct);

        using var customer = await CustomerAsync(w.Factory, "نورة", ct);
        var booking = await OkAsync(BookAsync(customer, w.SlugA, w.Haircut, w.Faisal, At(Target, 10), ct), ct, HttpStatusCode.Created);
        booking.GetProperty("endsAt").GetDateTimeOffset().ShouldBe(At(Target, 10, 30));
        var id = booking.GetProperty("id").GetGuid();
        ((DateTime)(await ScalarAsync(w.Factory, "SELECT blocked_until FROM bookings.bookings WHERE id = @p", ct, id))!).ShouldBe(At(Target, 10, 40).UtcDateTime);

        // Faisal is busy until 10:40 (the 30 minutes and the cleaning), and a booking before must leave its own cleaning time.
        using var anonymous = ApiSession.Create(w.Factory);
        var times = await TimesAsync(anonymous, $"/api/v1/public/shops/{w.SlugA}/availability/slots?serviceId={w.Haircut}&professionalId={w.Faisal}&date={Iso(Target)}", ct);
        times.ShouldNotContain("10:30");
        times.ShouldNotContain("10:35");
        times.ShouldContain("10:40");
        times.ShouldContain("09:20");
        times.ShouldNotContain("09:25");
        times.ShouldNotContain("20:25");
        times.ShouldContain("20:20");

        // The walk-in desk keeps it too.
        using var faisal = await w.BarberAsync(w.Faisal, ct);
        await FailsAsync(faisal.SendAsync(HttpMethod.Post, "/api/v1/shop/bookings/walk-in",
            new { serviceId = w.Haircut, professionalId = w.Faisal, startsAt = At(Target, 10, 35), customerName = "زائر", channel = "Phone" }, ct, headers: Key()),
            HttpStatusCode.Conflict, null, ct);

        // Even past the application's checks, the exclusion constraint refuses a booking that starts inside the cleaning time.
        await using var scope = w.Factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        using var system = services.GetRequiredService<ISystemDataScope>().Begin();
        var db = services.GetRequiredService<TrimmeDbContext>();
        var shop = new ShopId(w.Shops.A.ShopId);
        var offer = (await services.GetRequiredService<IBookableOfferCatalog>().FindAsync(shop, w.Haircut, null, ct)).ShouldNotBeNull();
        var pro = (await services.GetRequiredService<IProfessionalDirectory>().FindAsync(new ProfessionalId(w.Faisal), ct)).ShouldNotBeNull();
        db.Add(Booking.CreateWalkIn(
            new BookingId(Guid.CreateVersion7()), shop, "زائر", new BookedProfessional(pro.Id, pro.NameAr, pro.NameEn), BookingMapping.Snapshot(offer),
            At(Target, 10, 35), startsNow: false, null, BookingActor.System, DateTimeOffset.UtcNow, BookingChannel.Phone, bufferMinutes: 10));
        var refused = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
        DatabaseErrors.IsExclusionViolation(refused).ShouldBeTrue();
    }

    [Fact]
    public async Task AnOfferedNewTime_HoldsItsCleaningTimeToo_AndABookingPlacedAgain_TakesTheShopsCleaningTimeNow()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "d149_hold", ct);
        await OkAsync(w.OwnerA.PutAsync("/api/v1/shop/booking-settings", Settings(false, 5, 0, 14, 10), ct), ct);
        using var faisal = await w.BarberAsync(w.Faisal, ct);
        using var noura = await CustomerAsync(w.Factory, "نورة", ct);
        using var sara = await CustomerAsync(w.Factory, "سارة", ct);

        // Faisal offers Noura 15:00 instead of 12:00: the offer holds 15:00–15:30 and its cleaning time, until 15:40.
        var booking = await OkAsync(BookAsync(noura, w.SlugA, w.Haircut, w.Faisal, At(Target, 12), ct), ct, HttpStatusCode.Created);
        var id = booking.GetProperty("id").GetGuid();
        await OkAsync(faisal.PostAsync($"/api/v1/shop/bookings/{id}/postpone", new { startsAt = At(Target, 15), version = booking.GetProperty("version").GetUInt32() }, ct), ct);
        ((DateTime)(await ScalarAsync(w.Factory, "SELECT blocked_until FROM bookings.postponement_offers WHERE booking_id = @p", ct, id))!)
            .ShouldBe(At(Target, 15, 40).UtcDateTime);
        await FailsAsync(BookAsync(sara, w.SlugA, w.Haircut, w.Faisal, At(Target, 15, 35), ct), HttpStatusCode.Conflict, null, ct);

        // The database's hold refuses it too, past the application's checks.
        await using (var scope = w.Factory.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            using var system = services.GetRequiredService<ISystemDataScope>().Begin();
            var db = services.GetRequiredService<TrimmeDbContext>();
            var shop = new ShopId(w.Shops.A.ShopId);
            var offer = (await services.GetRequiredService<IBookableOfferCatalog>().FindAsync(shop, w.Haircut, null, ct)).ShouldNotBeNull();
            var pro = (await services.GetRequiredService<IProfessionalDirectory>().FindAsync(new ProfessionalId(w.Faisal), ct)).ShouldNotBeNull();
            db.Add(Booking.CreateWalkIn(
                new BookingId(Guid.CreateVersion7()), shop, "زائر", new BookedProfessional(pro.Id, pro.NameAr, pro.NameEn), BookingMapping.Snapshot(offer),
                At(Target, 15, 35), startsNow: false, null, BookingActor.System, DateTimeOffset.UtcNow, BookingChannel.Phone, bufferMinutes: 10));
            var refused = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
            DatabaseErrors.IsLostRace(refused).ShouldBeTrue();
        }

        // 15:40 is free; Noura accepts, and the booking holds what the offer held.
        var mine = await OkAsync(noura.GetAsync($"/api/v1/me/bookings/{id}", ct), ct);
        await OkAsync(noura.PostAsync($"/api/v1/me/bookings/{id}/postponement/accept", new { version = mine.GetProperty("version").GetUInt32() }, ct), ct);
        ((DateTime)(await ScalarAsync(w.Factory, "SELECT blocked_until FROM bookings.bookings WHERE id = @p", ct, id))!).ShouldBe(At(Target, 15, 40).UtcDateTime);

        // The owner then drops the cleaning time: a booking made before keeps its 10 minutes until it is placed again,
        // and then takes the shop's 0, the value its new place was checked with.
        var walkIn = await OkAsync(faisal.SendAsync(HttpMethod.Post, "/api/v1/shop/bookings/walk-in",
            new { serviceId = w.Haircut, professionalId = w.Faisal, startsAt = At(Target, 17), customerName = "زائر", channel = "Phone" }, ct, headers: Key()), ct, HttpStatusCode.Created);
        var walkInId = walkIn.GetProperty("id").GetGuid();
        await OkAsync(w.OwnerA.PutAsync("/api/v1/shop/booking-settings", Settings(false, 5, 0, 14, 0), ct), ct);
        await OkAsync(faisal.SendAsync(HttpMethod.Post, "/api/v1/shop/bookings/walk-in",
            new { serviceId = w.Haircut, professionalId = w.Faisal, startsAt = At(Target, 18, 30), customerName = "ضيف", channel = "Phone" }, ct, headers: Key()), ct, HttpStatusCode.Created);
        var version = (await OkAsync(faisal.GetAsync($"/api/v1/shop/bookings/{walkInId}", ct), ct)).GetProperty("booking").GetProperty("version").GetUInt32();
        var moved = await OkAsync(faisal.SendAsync(HttpMethod.Post, $"/api/v1/shop/bookings/{walkInId}/reschedule", new { startsAt = At(Target, 18), version }, ct,
            headers: Key()), ct);
        moved.GetProperty("endsAt").GetDateTimeOffset().ShouldBe(At(Target, 18, 30));
        (await ScalarAsync(w.Factory, "SELECT buffer_minutes FROM bookings.bookings WHERE id = @p", ct, walkInId)).ShouldBe(0);
    }

    [Fact]
    public async Task EachBarber_TakesTheirOwnTime_ForTheSameService_WhereverALengthIsUsed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "d149_lengths", ct);
        await OkAsync(w.OwnerA.PutAsync("/api/v1/shop/booking-settings", Settings(false, 5, 0, 14, 0), ct), ct);

        // A haircut is 30 minutes; Omar takes 45. An invalid length is refused; the price stays the shop's.
        await FailsAsync(w.OwnerA.PutAsync($"/api/v1/shop/professionals/{w.Omar}/services",
            new { services = new[] { new { serviceId = w.Haircut, durationMinutes = (int?)47 } } }, ct), HttpStatusCode.BadRequest, "durationMinutes", ct);
        var set = await OkAsync(w.OwnerA.PutAsync($"/api/v1/shop/professionals/{w.Omar}/services",
            new { services = new object[] { new { serviceId = w.Haircut, durationMinutes = (int?)45 }, new { serviceId = w.Beard, durationMinutes = (int?)null } } }, ct), ct);
        var haircut = set.GetProperty("services").EnumerateArray().Single(s => s.GetProperty("serviceId").GetGuid() == w.Haircut);
        haircut.GetProperty("durationMinutes").GetInt32().ShouldBe(30);
        haircut.GetProperty("ownDurationMinutes").GetInt32().ShouldBe(45);

        // The plain list keeps the lengths of the services Omar keeps (the older form).
        await OkAsync(w.Admin.PutAsync($"/api/v1/admin/professionals/{w.Omar}/services", new { serviceIds = new[] { w.Haircut, w.Beard } }, ct), ct);
        using var anonymous = ApiSession.Create(w.Factory);
        var publicHaircut = (await OkAsync(anonymous.GetAsync($"/api/v1/public/shops/{w.SlugA}/services", ct), ct)).EnumerateArray()
            .Single(s => s.GetProperty("id").GetGuid() == w.Haircut);
        publicHaircut.GetProperty("durationMinutes").GetInt32().ShouldBe(30);
        publicHaircut.GetProperty("price").GetDecimal().ShouldBe(60m);
        var own = publicHaircut.GetProperty("professionalDurations").EnumerateArray().Single();
        own.GetProperty("professionalId").GetGuid().ShouldBe(w.Omar);
        own.GetProperty("durationMinutes").GetInt32().ShouldBe(45);

        // Before 21:00 closing: Omar's last start is 20:15, Faisal's 20:30; «any barber» at 20:30 is Faisal alone.
        string Slots(string extra) => $"/api/v1/public/shops/{w.SlugA}/availability/slots?serviceId={w.Haircut}&date={Iso(Target)}{extra}";
        var omarTimes = await TimesAsync(anonymous, Slots($"&professionalId={w.Omar}"), ct);
        omarTimes.ShouldContain("20:15");
        omarTimes.ShouldNotContain("20:20");
        (await TimesAsync(anonymous, Slots($"&professionalId={w.Faisal}"), ct)).ShouldContain("20:30");
        var any = (await OkAsync(anonymous.GetAsync(Slots(string.Empty), ct), ct)).GetProperty("slots").EnumerateArray().ToList();
        any.Single(s => s.GetProperty("localTime").GetString() == "20:30").GetProperty("professionalIds").EnumerateArray().Select(p => p.GetGuid()).ShouldBe([w.Faisal]);
        any.Single(s => s.GetProperty("localTime").GetString() == "20:15").GetProperty("professionalIds").EnumerateArray().Select(p => p.GetGuid())
            .ShouldBe([w.Faisal, w.Omar], ignoreOrder: true);

        // A booking with Omar takes his 45 minutes; with any barber at 20:30 it goes to Faisal; services together add his own.
        using var customer = await CustomerAsync(w.Factory, "نورة", ct);
        var withOmar = await OkAsync(BookAsync(customer, w.SlugA, w.Haircut, w.Omar, At(Target, 10), ct), ct, HttpStatusCode.Created);
        withOmar.GetProperty("endsAt").GetDateTimeOffset().ShouldBe(At(Target, 10, 45));
        withOmar.GetProperty("item").GetProperty("durationMinutes").GetInt32().ShouldBe(45);
        withOmar.GetProperty("item").GetProperty("price").GetDecimal().ShouldBe(60m);
        (await OkAsync(BookAsync(customer, w.SlugA, w.Haircut, null, At(Target, 20, 30), ct), ct, HttpStatusCode.Created))
            .GetProperty("professional").GetProperty("id").GetGuid().ShouldBe(w.Faisal);
        var both = await OkAsync(customer.SendAsync(HttpMethod.Post, "/api/v1/bookings",
            new { shopSlug = w.SlugA, serviceIds = new[] { w.Haircut, w.Beard }, professionalId = w.Omar, startsAt = At(Target, 15), note = (string?)null }, ct,
            headers: Key()), ct, HttpStatusCode.Created);
        both.GetProperty("endsAt").GetDateTimeOffset().ShouldBe(At(Target, 16, 5));

        // The walk-in desk shows each barber's own length.
        using var faisal = await w.BarberAsync(w.Faisal, ct);
        var options = await OkAsync(faisal.GetAsync($"/api/v1/shop/availability/walk-in?serviceId={w.Haircut}&date={Iso(Target)}", ct), ct);
        options.GetProperty("professionals").EnumerateArray().ToDictionary(p => p.GetProperty("id").GetGuid(), p => p.GetProperty("durationMinutes").GetInt32())
            .ShouldBe(new Dictionary<Guid, int> { [w.Faisal] = 30, [w.Omar] = 45 }, ignoreOrder: true);

        // Moving Faisal's 12:00 booking to Omar: his 45 minutes run into his 12:40 booking, so it is refused (his 30 would fit).
        var at12 = await OkAsync(BookAsync(customer, w.SlugA, w.Haircut, w.Faisal, At(Target, 12), ct), ct, HttpStatusCode.Created);
        await OkAsync(BookAsync(customer, w.SlugA, w.Beard, w.Omar, At(Target, 12, 40), ct), ct, HttpStatusCode.Created);
        var id12 = at12.GetProperty("id").GetGuid();
        var version = (await OkAsync(faisal.GetAsync($"/api/v1/shop/bookings/{id12}", ct), ct)).GetProperty("booking").GetProperty("version").GetUInt32();
        (await OkAsync(faisal.GetAsync($"/api/v1/shop/bookings/{id12}/professional-options", ct), ct)).GetProperty("professionals").EnumerateArray()
            .Single(p => p.GetProperty("id").GetGuid() == w.Omar).GetProperty("free").GetBoolean().ShouldBeFalse();
        await FailsAsync(faisal.SendAsync(HttpMethod.Post, $"/api/v1/shop/bookings/{id12}/professional", new { professionalId = w.Omar, version }, ct, headers: Key()),
            HttpStatusCode.Conflict, "booking.professional_busy", ct);

        // Where it fits, the booking moves with his length.
        var at17 = await OkAsync(BookAsync(customer, w.SlugA, w.Haircut, w.Faisal, At(Target, 17), ct), ct, HttpStatusCode.Created);
        var id17 = at17.GetProperty("id").GetGuid();
        var version17 = (await OkAsync(faisal.GetAsync($"/api/v1/shop/bookings/{id17}", ct), ct)).GetProperty("booking").GetProperty("version").GetUInt32();
        var moved = await OkAsync(faisal.SendAsync(HttpMethod.Post, $"/api/v1/shop/bookings/{id17}/professional", new { professionalId = w.Omar, version = version17 }, ct,
            headers: Key()), ct);
        moved.GetProperty("endsAt").GetDateTimeOffset().ShouldBe(At(Target, 17, 45));
        moved.GetProperty("item").GetProperty("durationMinutes").GetInt32().ShouldBe(45);
    }

    [Fact]
    public async Task TheOwner_AddsABarberByMobile_WithLanguages_ThatThePublicPagesShow()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "d149_barbers", ct);
        var mobile = IdentityTestData.NewPhone();

        await FailsAsync(w.OwnerA.PostAsync("/api/v1/shop/professionals", new { nameAr = "أحمد", notificationsEnabled = true }, ct), HttpStatusCode.BadRequest, "whatsAppNumber", ct);
        await FailsAsync(w.OwnerA.PostAsync("/api/v1/shop/professionals",
            new { nameAr = "أحمد", whatsAppNumber = mobile, notificationsEnabled = true, languages = new[] { "ar", "fr" } }, ct), HttpStatusCode.BadRequest, "languages", ct);

        // No English name needed: the Arabic one stands in, and the page address is made for him.
        var created = await OkAsync(w.OwnerA.PostAsync("/api/v1/shop/professionals",
            new { nameAr = "أحمد", whatsAppNumber = mobile, notificationsEnabled = true, languages = new[] { "ur", "ar" } }, ct), ct, HttpStatusCode.Created);
        var barber = created.GetProperty("barber");
        barber.GetProperty("nameEn").GetString().ShouldBe("أحمد");
        barber.GetProperty("slug").GetString().ShouldNotBeNullOrWhiteSpace();
        barber.GetProperty("languages").EnumerateArray().Select(l => l.GetString()).ShouldBe(["ar", "ur"]);
        created.GetProperty("temporaryPassword").GetString().ShouldNotBeNullOrWhiteSpace();
        var id = barber.GetProperty("id").GetGuid();

        // Edits keep the languages unless they are sent.
        var version = barber.GetProperty("version").GetUInt32();
        var kept = await OkAsync(w.OwnerA.PutAsync($"/api/v1/shop/professionals/{id}", new { nameAr = "أحمد علي", version }, ct), ct);
        kept.GetProperty("languages").EnumerateArray().Select(l => l.GetString()).ShouldBe(["ar", "ur"]);
        var changed = await OkAsync(w.OwnerA.PutAsync($"/api/v1/shop/professionals/{id}",
            new { nameAr = "أحمد علي", nameEn = "Ahmed Ali", version = kept.GetProperty("version").GetUInt32(), languages = new[] { "en", "bn" } }, ct), ct);
        changed.GetProperty("languages").EnumerateArray().Select(l => l.GetString()).ShouldBe(["en", "bn"]);
        changed.GetProperty("nameEn").GetString().ShouldBe("Ahmed Ali");
        (await OkAsync(w.Admin.GetAsync($"/api/v1/admin/professionals/{id}", ct), ct)).GetProperty("languages").GetArrayLength().ShouldBe(2);

        using var anonymous = ApiSession.Create(w.Factory);
        (await OkAsync(anonymous.GetAsync($"/api/v1/public/shops/{w.SlugA}/professionals", ct), ct)).EnumerateArray()
            .Single(p => p.GetProperty("id").GetGuid() == id).GetProperty("languages").EnumerateArray().Select(l => l.GetString()).ShouldBe(["en", "bn"]);
        (await OkAsync(anonymous.GetAsync($"/api/v1/public/shops/{w.SlugA}/professionals/{changed.GetProperty("slug").GetString()}", ct), ct))
            .GetProperty("languages").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task TheShopsOwnSections_AreOrderedByTheOwner_AndShownOnThePublicPages()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "d149_sections", ct);
        async Task<Guid> SectionAsync(ApiSession owner, string ar, string? en) =>
            (await OkAsync(owner.PostAsync("/api/v1/shop/service-sections", new { nameAr = ar, nameEn = en }, ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();

        var hair = await SectionAsync(w.OwnerA, "الشعر", "Hair");
        var beard = await SectionAsync(w.OwnerA, "اللحية", null);
        var other = await SectionAsync(w.OwnerB, "العناية", "Care");
        using var faisal = await w.BarberAsync(w.Faisal, ct);
        await FailsAsync(faisal.PostAsync("/api/v1/shop/service-sections", new { nameAr = "س" }, ct), HttpStatusCode.Forbidden, null, ct);
        await FailsAsync(w.OwnerA.PostAsync("/api/v1/shop/service-sections", new { nameAr = " " }, ct), HttpStatusCode.BadRequest, "nameAr", ct);

        // A service goes in one of its own shop's sections only.
        async Task<JsonElement> PlaceAsync(Guid serviceId, Guid? sectionId, string name, int minutes)
        {
            var current = await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/services/{serviceId}", ct), ct);
            return await OkAsync(w.OwnerA.PutAsync($"/api/v1/shop/services/{serviceId}", new
            {
                nameAr = name, price = 60m, durationMinutes = minutes, onlineBookable = true, version = current.GetProperty("version").GetUInt32(), sectionId,
            }, ct), ct);
        }

        (await PlaceAsync(w.Haircut, hair, "حلاقة", 30)).GetProperty("sectionId").GetGuid().ShouldBe(hair);
        await PlaceAsync(w.Beard, beard, "لحية", 20);
        var current = await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/services/{w.Beard}", ct), ct);
        await FailsAsync(w.OwnerA.PutAsync($"/api/v1/shop/services/{w.Beard}", new
        {
            nameAr = "لحية", price = 60m, durationMinutes = 20, onlineBookable = true, version = current.GetProperty("version").GetUInt32(), sectionId = other,
        }, ct), HttpStatusCode.BadRequest, "sectionId", ct);
        await FailsAsync(w.OwnerB.PutAsync($"/api/v1/shop/service-sections/{hair}", new { nameAr = "س" }, ct), HttpStatusCode.NotFound, null, ct);

        // The owner orders them; the list must be complete.
        await FailsAsync(w.OwnerA.PutAsync("/api/v1/shop/service-sections/order", new { orderedIds = new[] { beard } }, ct), HttpStatusCode.BadRequest, "orderedIds", ct);
        var ordered = await OkAsync(w.OwnerA.PutAsync("/api/v1/shop/service-sections/order", new { orderedIds = new[] { beard, hair } }, ct), ct);
        ordered.EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).ShouldBe([beard, hair]);
        ordered.EnumerateArray().Select(s => s.GetProperty("serviceCount").GetInt32()).ShouldBe([1, 1]);
        (await OkAsync(w.OwnerA.PutAsync($"/api/v1/shop/service-sections/{beard}", new { nameAr = "الدقن", nameEn = "Beard" }, ct), ct))
            .GetProperty("nameAr").GetString().ShouldBe("الدقن");

        // Customers see the shop's order, and each service's section.
        using var anonymous = ApiSession.Create(w.Factory);
        var sections = (await OkAsync(anonymous.GetAsync($"/api/v1/public/shops/{w.SlugA}/service-sections", ct), ct)).EnumerateArray().ToList();
        sections.Select(s => s.GetProperty("nameAr").GetString()).ShouldBe(["الدقن", "الشعر"]);
        (await OkAsync(anonymous.GetAsync($"/api/v1/public/shops/{w.SlugA}/services", ct), ct)).EnumerateArray()
            .Single(s => s.GetProperty("id").GetGuid() == w.Haircut).GetProperty("sectionId").GetGuid().ShouldBe(hair);

        // The admin manages them from the shop's page; deleting a section leaves its services without one.
        (await OkAsync(w.Admin.GetAsync($"/api/v1/admin/shops/{w.Shops.A.ShopId}/service-sections", ct), ct)).GetArrayLength().ShouldBe(2);
        await OkAsync(w.Admin.DeleteAsync($"/api/v1/admin/shops/{w.Shops.A.ShopId}/service-sections/{hair}", ct), ct, HttpStatusCode.NoContent);
        (await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/services/{w.Haircut}", ct), ct)).GetProperty("sectionId").ValueKind.ShouldBe(JsonValueKind.Null);
        (await OkAsync(w.OwnerA.GetAsync("/api/v1/shop/service-sections", ct), ct)).EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).ShouldBe([beard]);
    }

    [Fact]
    public async Task EveryShop_IsAMensBarbershop()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "d149_category", ct);
        var profile = await OkAsync(w.OwnerA.GetAsync("/api/v1/shop/profile", ct), ct);
        profile.GetProperty("category").GetString().ShouldBe("Barbershop");
        object Profile(string category) => new
        {
            nameAr = profile.GetProperty("nameAr").GetString(), nameEn = profile.GetProperty("nameEn").GetString(), descriptionAr = (string?)null,
            descriptionEn = (string?)null, category, publicPhone = (string?)null, amenities = Array.Empty<string>(), version = profile.GetProperty("version").GetUInt32(),
        };
        await FailsAsync(w.OwnerA.PutAsync("/api/v1/shop/profile", Profile("Salon"), ct), HttpStatusCode.BadRequest, "category", ct);
        await FailsAsync(w.OwnerA.PutAsync("/api/v1/shop/profile", Profile("Unisex"), ct), HttpStatusCode.BadRequest, "category", ct);
        var detail = await OkAsync(w.Admin.GetAsync($"/api/v1/admin/shops/{w.Shops.A.ShopId}", ct), ct);
        await FailsAsync(w.Admin.PutAsync($"/api/v1/admin/shops/{w.Shops.A.ShopId}", new
        {
            nameAr = detail.GetProperty("nameAr").GetString(), nameEn = detail.GetProperty("nameEn").GetString(), category = "Salon", amenities = Array.Empty<string>(),
            isVerified = false, version = detail.GetProperty("version").GetUInt32(),
        }, ct), HttpStatusCode.BadRequest, "category", ct);
    }

    [Fact]
    public async Task TheMigration_MapsAShopsData_SectionsFromItsCategories_ABarbershop_14DaysAhead_AndBookingsHeldUntilTheirEnd()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "d149_migration", ct);
        async Task<Guid> CategoryAsync(string ar, string en, int order) =>
            (await OkAsync(w.Admin.PostAsync("/api/v1/admin/service-categories", new { nameAr = ar, nameEn = en, icon = "scissors", displayOrder = order }, ct), ct,
                HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var hairCategory = await CategoryAsync("الشعر", "Hair", 2);
        var beardCategory = await CategoryAsync("اللحية", "Beard", 1);
        using var customer = await CustomerAsync(w.Factory, "نورة", ct);
        var booking = (await OkAsync(BookAsync(customer, w.SlugA, w.Haircut, w.Faisal, At(Target, 10), ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();

        // Back to the schema before D-149, with a shop's data as it was then; then forward again.
        await using (var scope = w.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<TrimmeDbContext>().GetService<IMigrator>().MigrateAsync("20261118204915_CustomerPages", ct);
        }

        await ExecuteAsync(w.Factory, $"""
            UPDATE services.shop_services SET category_id = '{hairCategory}' WHERE id = '{w.Haircut}';
            UPDATE services.shop_services SET category_id = '{beardCategory}' WHERE id = '{w.Beard}';
            UPDATE shops.shops SET category = 'Salon' WHERE id = '{w.Shops.A.ShopId}';
            UPDATE administration.platform_settings SET booking_horizon_days = 30;
            """, ct);
        await w.Factory.MigrateAsync(ct);

        (await ScalarAsync(w.Factory, "SELECT category FROM shops.shops WHERE id = @p", ct, w.Shops.A.ShopId)).ShouldBe("Barbershop");
        (await ScalarAsync(w.Factory, "SELECT booking_horizon_days FROM administration.platform_settings", ct)).ShouldBe(14);
        (await ScalarAsync(w.Factory, "SELECT blocked_until = ends_at FROM bookings.bookings WHERE id = @p", ct, booking)).ShouldBe(true);
        var sections = (await OkAsync(w.OwnerA.GetAsync("/api/v1/shop/service-sections", ct), ct)).EnumerateArray().ToList();
        sections.Select(s => s.GetProperty("nameAr").GetString()).ShouldBe(["اللحية", "الشعر"]);
        var hairSection = sections.Single(s => s.GetProperty("nameAr").GetString() == "الشعر").GetProperty("id").GetGuid();
        (await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/services/{w.Haircut}", ct), ct)).GetProperty("sectionId").GetGuid().ShouldBe(hairSection);
        (await OkAsync(w.OwnerB.GetAsync("/api/v1/shop/service-sections", ct), ct)).GetArrayLength().ShouldBe(0);

        // The exclusion constraint is back, on the new range.
        await FailsAsync(BookAsync(customer, w.SlugA, w.Haircut, w.Faisal, At(Target, 10, 10), ct), HttpStatusCode.Conflict, null, ct);
    }
}
