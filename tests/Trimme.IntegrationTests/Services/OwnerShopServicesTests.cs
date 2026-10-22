using System.Net;
using Shouldly;
using Trimme.IntegrationTests.Infrastructure;
using static Trimme.IntegrationTests.Bookings.BookingTestData;

namespace Trimme.IntegrationTests.Services;

/// <summary>
/// D-137: the owner adds and edits the shop's own services again, and picks which of the shop's own barbers do each, so
/// a new service can be booked at once. The desk (ShopStaff) cannot. Another shop's barber is refused, as for the admin
/// (D-127).
/// </summary>
public sealed class OwnerShopServicesTests(PostgresFixture postgres)
{
    private static object Body(decimal price, int duration, IEnumerable<Guid>? professionalIds, uint? version = null) => new
    {
        nameAr = "حلاقة أطفال",
        nameEn = "Kids cut",
        descriptionAr = (string?)null,
        descriptionEn = (string?)null,
        categoryId = (Guid?)null,
        price,
        durationMinutes = duration,
        onlineBookable = true,
        professionalIds,
        version,
    };

    private static Guid[] Barbers(System.Text.Json.JsonElement service) =>
        [.. service.GetProperty("professionalIds").EnumerateArray().Select(p => p.GetGuid())];

    [Fact]
    public async Task Owner_AddsAndEditsAService_WithTheShopsOwnBarbers_AndTheDeskCannot()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "svc_owner_build", ct);
        using var anonymous = ApiSession.Create(w.Factory);
        var target = TodayAt(DateTimeOffset.UtcNow).AddDays(2);

        // The desk has no Shop.Services.Manage; another shop's barber is refused.
        (await w.StaffA.PostAsync("/api/v1/shop/services", Body(40m, 30, [w.Faisal]), ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await FailsAsync(w.OwnerA.PostAsync("/api/v1/shop/services", Body(40m, 30, [w.ProB]), ct), HttpStatusCode.BadRequest, "professionalIds", ct))
            .ShouldContain("validation.invalid");

        var created = await OkAsync(w.OwnerA.PostAsync("/api/v1/shop/services", Body(40m, 30, [w.Faisal]), ct), ct, HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();
        Barbers(created).ShouldBe([w.Faisal]);
        created.GetProperty("assignedProfessionalCount").GetInt32().ShouldBe(1);

        // Bookable at once, with Faisal only.
        var slots = await OkAsync(anonymous.GetAsync($"/api/v1/public/shops/{w.SlugA}/availability/slots?serviceId={id}&date={Iso(target)}", ct), ct);
        slots.GetProperty("slots").GetArrayLength().ShouldBeGreaterThan(0);
        slots.GetProperty("slots").EnumerateArray()
            .SelectMany(s => s.GetProperty("professionalIds").EnumerateArray().Select(p => p.GetGuid())).Distinct().ShouldBe([w.Faisal]);

        // The edit: price, duration and barbers; the desk cannot; another shop's barber is refused and changes nothing.
        var version = created.GetProperty("version").GetUInt32();
        (await w.StaffA.PutAsync($"/api/v1/shop/services/{id}", Body(45m, 35, null, version), ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await FailsAsync(w.OwnerA.PutAsync($"/api/v1/shop/services/{id}", Body(45m, 35, [w.Faisal, w.ProB], version), ct), HttpStatusCode.BadRequest, "professionalIds", ct);
        var edited = await OkAsync(w.OwnerA.PutAsync($"/api/v1/shop/services/{id}", Body(45m, 35, [w.Faisal, w.Omar], version), ct), ct);
        edited.GetProperty("price").GetDecimal().ShouldBe(45m);
        edited.GetProperty("durationMinutes").GetInt32().ShouldBe(35);
        Barbers(edited).ShouldBe([w.Faisal, w.Omar], ignoreOrder: true);

        // Leaving the barbers out keeps them; an empty list takes them all off.
        var kept = await OkAsync(w.OwnerA.PutAsync($"/api/v1/shop/services/{id}", Body(45m, 35, null, edited.GetProperty("version").GetUInt32()), ct), ct);
        Barbers(kept).ShouldBe([w.Faisal, w.Omar], ignoreOrder: true);
        var none = await OkAsync(w.OwnerA.PutAsync($"/api/v1/shop/services/{id}", Body(45m, 35, [], kept.GetProperty("version").GetUInt32()), ct), ct);
        Barbers(none).ShouldBeEmpty();

        // The list carries each service's barbers; the desk can read the list, and another shop cannot see the service.
        var list = await OkAsync(w.StaffA.GetAsync("/api/v1/shop/services", ct), ct);
        Barbers(list.EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == w.Haircut)).ShouldNotBeEmpty();
        (await w.OwnerB.GetAsync($"/api/v1/shop/services/{id}", ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Created without barbers (an older client) still works: no one does it yet.
        var bare = await OkAsync(w.OwnerA.PostAsync("/api/v1/shop/services", Body(30m, 20, null), ct), ct, HttpStatusCode.Created);
        Barbers(bare).ShouldBeEmpty();
    }
}
