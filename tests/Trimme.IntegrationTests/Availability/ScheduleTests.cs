using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Trimme.BuildingBlocks.Application.Scheduling;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.IntegrationTests.Identity;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.IntegrationTests.Tenancy;
using Trimme.Modules.Identity.Domain;

namespace Trimme.IntegrationTests.Availability;

/// <summary>
/// R-AVL-02/03 (D-082): the shop manages its hours, professionals' hours, breaks, time off, closures and the pause;
/// the public slot API follows every change, returns only bookable slots, and never crosses shops.
/// </summary>
public sealed class ScheduleTests(PostgresFixture postgres)
{
    private static readonly TimeZoneInfo Riyadh = TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");

    private static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Riyadh).DateTime);

    /// <summary>Two days ahead: clear of the one-hour lead time whatever the time of day.</summary>
    private static DateOnly Target => Today.AddDays(2);

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static object[] EveryDay(int start, int end) =>
        [.. Enum.GetValues<DayOfWeek>().Select(d => new { day = d.ToString(), startMinute = start, endMinute = end })];

    private static async Task<JsonElement> OkAsync(Task<HttpResponseMessage> call, CancellationToken ct, HttpStatusCode status = HttpStatusCode.OK)
    {
        using var response = await call;
        response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync(ct));
        return status == HttpStatusCode.NoContent ? default : await response.JsonAsync(ct);
    }

    private static async Task FailsAsync(Task<HttpResponseMessage> call, HttpStatusCode status, string? contains, CancellationToken ct)
    {
        using var response = await call;
        var body = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.ShouldBe(status, body);
        if (contains is not null)
        {
            body.ShouldContain(contains);
        }
    }

    private static async Task<string[]> SlotTimesAsync(ApiSession session, string slug, Guid serviceId, DateOnly date, CancellationToken ct, Guid? professionalId = null)
    {
        var json = await SlotsAsync(session, slug, $"serviceId={serviceId}", date, ct, professionalId);
        json.GetProperty("bookable").GetBoolean().ShouldBeTrue(json.ToString());
        return [.. json.GetProperty("slots").EnumerateArray().Select(s => s.GetProperty("localTime").GetString()!)];
    }

    private static Task<JsonElement> SlotsAsync(ApiSession session, string slug, string item, DateOnly date, CancellationToken ct, Guid? professionalId = null) =>
        OkAsync(session.GetAsync($"/api/v1/public/shops/{slug}/availability/slots?{item}&date={Iso(date)}{(professionalId is { } p ? $"&professionalId={p}" : string.Empty)}", ct), ct);

    [Fact]
    public async Task Shop_ManagesSchedule_AndThePublicSlotsFollowEveryChange()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var s = await ArrangeAsync("avl_flow", ct);
        using var anonymous = ApiSession.Create(s.Factory);

        // No hours yet: the shop is closed. Staff read the schedule but cannot change it (R-AVL-03).
        (await SlotTimesAsync(anonymous, s.SlugA, s.ServiceA, Target, ct)).ShouldBeEmpty();
        var schedule = await OkAsync(s.StaffA.GetAsync("/api/v1/shop/schedule", ct), ct);
        schedule.GetProperty("openingHours").GetProperty("version").ValueKind.ShouldBe(JsonValueKind.Null);
        schedule.GetProperty("timeZone").GetString().ShouldBe("Asia/Riyadh");
        schedule.GetProperty("professionals")[0].GetProperty("followsShopHours").GetBoolean().ShouldBeTrue();
        await FailsAsync(s.StaffA.PutAsync("/api/v1/shop/schedule/opening-hours", new { intervals = EveryDay(540, 720) }, ct), HttpStatusCode.Forbidden, null, ct);
        await FailsAsync(s.StaffA.PostAsync("/api/v1/shop/online-booking/pause", new { }, ct), HttpStatusCode.Forbidden, null, ct);

        // Opening hours 09:00–12:00 every day: a 30-minute service on the 5-minute grid, 09:00 … 11:30.
        await FailsAsync(s.OwnerA.PutAsync("/api/v1/shop/schedule/opening-hours", new { intervals = new[] { new { day = "Sunday", startMinute = 540, endMinute = 530 } } }, ct),
            HttpStatusCode.BadRequest, "validation.hours_invalid", ct);
        var hours = await OkAsync(s.OwnerA.PutAsync("/api/v1/shop/schedule/opening-hours", new { intervals = EveryDay(540, 720) }, ct), ct);
        var times = await SlotTimesAsync(anonymous, s.SlugA, s.ServiceA, Target, ct);
        times.Length.ShouldBe(31);
        times[0].ShouldBe("09:00");
        times[1].ShouldBe("09:05");
        times[^1].ShouldBe("11:30");
        await FailsAsync(s.OwnerA.PutAsync("/api/v1/shop/schedule/opening-hours", new { intervals = EveryDay(540, 780), version = hours.GetProperty("version").GetUInt32() - 1 }, ct),
            HttpStatusCode.Conflict, null, ct);

        // The professional works 10:00–11:00 only: the intersection with the shop's hours.
        await OkAsync(s.OwnerA.PutAsync($"/api/v1/shop/professionals/{s.ProA}/working-hours", new { followsShopHours = false, intervals = EveryDay(600, 660) }, ct), ct);
        (await SlotTimesAsync(anonymous, s.SlugA, s.ServiceA, Target, ct)).ShouldBe(["10:00", "10:05", "10:10", "10:15", "10:20", "10:25", "10:30"]);

        // A break for everyone on the target weekday, 10:15–10:30.
        var breakBody = new { label = "صلاة", weekdays = new[] { Target.DayOfWeek.ToString() }, startMinute = 615, endMinute = 630 };
        var created = await OkAsync(s.OwnerA.PostAsync("/api/v1/shop/schedule/breaks", breakBody, ct), ct, HttpStatusCode.Created);
        (await SlotTimesAsync(anonymous, s.SlugA, s.ServiceA, Target, ct)).ShouldBe(["10:30"]);
        (await SlotTimesAsync(anonymous, s.SlugA, s.ServiceA, Target.AddDays(1), ct)).Length.ShouldBe(7, "other weekdays keep their slots");
        await OkAsync(s.OwnerA.DeleteAsync($"/api/v1/shop/schedule/breaks/{created.GetProperty("id").GetGuid()}", ct), ct, HttpStatusCode.NoContent);

        // Whole-day time off on the target date; a closure the day after; the date strip shows both.
        var timeOff = await OkAsync(s.OwnerA.PostAsync("/api/v1/shop/schedule/time-off", new { professionalId = s.ProA, kind = "Vacation", startDate = Iso(Target), endDate = Iso(Target) }, ct), ct, HttpStatusCode.Created);
        timeOff.GetProperty("allDay").GetBoolean().ShouldBeTrue();
        timeOff.GetProperty("state").GetString().ShouldBe("Scheduled");
        var closure = await OkAsync(s.OwnerA.PostAsync("/api/v1/shop/schedule/closures", new { startDate = Iso(Target.AddDays(1)), endDate = Iso(Target.AddDays(1)), reason = "اليوم الوطني" }, ct), ct, HttpStatusCode.Created);
        var dates = await OkAsync(anonymous.GetAsync($"/api/v1/public/shops/{s.SlugA}/availability/dates?serviceId={s.ServiceA}&from={Iso(Target)}&to={Iso(Target.AddDays(2))}", ct), ct);
        dates.GetProperty("dates").EnumerateArray().Select(d => d.GetProperty("slotCount").GetInt32()).ShouldBe([0, 0, 7]);

        schedule = await OkAsync(s.OwnerA.GetAsync("/api/v1/shop/schedule", ct), ct);
        schedule.GetProperty("timeOff").GetArrayLength().ShouldBe(1);
        schedule.GetProperty("closures")[0].GetProperty("reason").GetString().ShouldBe("اليوم الوطني");

        // Pause: no slots and a reason; the shop page itself stays reachable (D-013). Resume restores them.
        var profileVersion = (await OkAsync(s.OwnerA.GetAsync("/api/v1/shop/profile", ct), ct)).GetProperty("version").GetUInt32();
        var paused = await OkAsync(s.OwnerA.PostAsync("/api/v1/shop/online-booking/pause", new { reason = "ازدحام" }, ct), ct);
        paused.GetProperty("paused").GetBoolean().ShouldBeTrue();
        paused.GetProperty("reason").GetString().ShouldBe("ازدحام");
        (await OkAsync(s.OwnerA.GetAsync("/api/v1/shop/profile", ct), ct)).GetProperty("version").GetUInt32()
            .ShouldBe(profileVersion, "pausing never makes an open profile form stale (D-083)");
        await FailsAsync(s.OwnerA.PostAsync("/api/v1/shop/online-booking/pause", new { }, ct), HttpStatusCode.Conflict, "shop.online_booking_already_paused", ct);
        var blocked = await SlotsAsync(anonymous, s.SlugA, $"serviceId={s.ServiceA}", Target.AddDays(2), ct);
        blocked.GetProperty("bookable").GetBoolean().ShouldBeFalse();
        blocked.GetProperty("blockedReason").GetString().ShouldBe("shop.paused");
        blocked.GetProperty("slots").GetArrayLength().ShouldBe(0);
        (await anonymous.GetAsync($"/api/v1/public/shops/{s.SlugA}", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await OkAsync(s.StaffA.GetAsync("/api/v1/shop/schedule", ct), ct)).GetProperty("onlineBookingPaused").GetBoolean().ShouldBeTrue();
        await OkAsync(s.OwnerA.PostAsync("/api/v1/shop/online-booking/resume", null, ct), ct);

        // Removing the time off and the closure brings the slots back.
        await OkAsync(s.OwnerA.DeleteAsync($"/api/v1/shop/schedule/time-off/{timeOff.GetProperty("id").GetGuid()}", ct), ct, HttpStatusCode.NoContent);
        await OkAsync(s.OwnerA.DeleteAsync($"/api/v1/shop/schedule/closures/{closure.GetProperty("id").GetGuid()}", ct), ct, HttpStatusCode.NoContent);
        (await SlotTimesAsync(anonymous, s.SlugA, s.ServiceA, Target, ct)).Length.ShouldBe(7);
        (await SlotTimesAsync(anonymous, s.SlugA, s.ServiceA, Target.AddDays(1), ct)).Length.ShouldBe(7);

        // Following the shop's hours again restores the full day.
        await OkAsync(s.OwnerA.PutAsync($"/api/v1/shop/professionals/{s.ProA}/working-hours", new { followsShopHours = true, version = (uint?)null }, ct), ct, HttpStatusCode.Conflict);
        var own = (await OkAsync(s.OwnerA.GetAsync("/api/v1/shop/schedule", ct), ct)).GetProperty("professionals")[0];
        await OkAsync(s.OwnerA.PutAsync($"/api/v1/shop/professionals/{s.ProA}/working-hours", new { followsShopHours = true, version = own.GetProperty("version").GetUInt32() }, ct), ct);
        (await SlotTimesAsync(anonymous, s.SlugA, s.ServiceA, Target, ct)).Length.ShouldBe(31);
    }

    [Fact]
    public async Task CrossShop_ScheduleIds_Are404_ForEveryVerb_AndNothingChanges()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var s = await ArrangeAsync("avl_tenancy", ct);
        await OkAsync(s.OwnerA.PutAsync("/api/v1/shop/schedule/opening-hours", new { intervals = EveryDay(540, 720) }, ct), ct);
        var closure = (await OkAsync(s.OwnerA.PostAsync("/api/v1/shop/schedule/closures", new { startDate = Iso(Target), endDate = Iso(Target) }, ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var breakId = (await OkAsync(s.OwnerA.PostAsync("/api/v1/shop/schedule/breaks", new { label = "غداء", professionalId = s.ProA, weekdays = new[] { "Monday" }, startMinute = 780, endMinute = 825 }, ct), ct, HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        var timeOff = (await OkAsync(s.OwnerA.PostAsync("/api/v1/shop/schedule/time-off", new { professionalId = s.ProA, kind = "Sick", startDate = Iso(Target), endDate = Iso(Target), startMinute = 600, endMinute = 660 }, ct), ct, HttpStatusCode.Created))
            .GetProperty("id").GetGuid();

        // Shop B guesses shop A's ids: every read and write is "not found".
        var b = s.OwnerB;
        await FailsAsync(b.PutAsync($"/api/v1/shop/schedule/closures/{closure}", new { startDate = Iso(Target), endDate = Iso(Target), version = 1 }, ct), HttpStatusCode.NotFound, "schedule.closure_not_found", ct);
        await FailsAsync(b.DeleteAsync($"/api/v1/shop/schedule/closures/{closure}", ct), HttpStatusCode.NotFound, null, ct);
        await FailsAsync(b.PutAsync($"/api/v1/shop/schedule/breaks/{breakId}", new { label = "x", weekdays = new[] { "Monday" }, startMinute = 0, endMinute = 5, version = 1 }, ct), HttpStatusCode.NotFound, null, ct);
        await FailsAsync(b.DeleteAsync($"/api/v1/shop/schedule/breaks/{breakId}", ct), HttpStatusCode.NotFound, null, ct);
        await FailsAsync(b.PutAsync($"/api/v1/shop/schedule/time-off/{timeOff}", new { professionalId = s.ProA, kind = "Sick", startDate = Iso(Target), endDate = Iso(Target), version = 1 }, ct), HttpStatusCode.NotFound, null, ct);
        await FailsAsync(b.DeleteAsync($"/api/v1/shop/schedule/time-off/{timeOff}", ct), HttpStatusCode.NotFound, null, ct);

        // …and shop A's professional cannot be referenced by shop B at all.
        await FailsAsync(b.PutAsync($"/api/v1/shop/professionals/{s.ProA}/working-hours", new { followsShopHours = false, intervals = EveryDay(0, 60) }, ct), HttpStatusCode.NotFound, "professional.not_found", ct);
        await FailsAsync(b.PostAsync("/api/v1/shop/schedule/time-off", new { professionalId = s.ProA, kind = "Vacation", startDate = Iso(Target), endDate = Iso(Target) }, ct), HttpStatusCode.NotFound, null, ct);
        await FailsAsync(b.PostAsync("/api/v1/shop/schedule/breaks", new { label = "x", professionalId = s.ProA, weekdays = new[] { "Monday" }, startMinute = 0, endMinute = 5 }, ct), HttpStatusCode.NotFound, null, ct);
        await FailsAsync(b.PostAsync("/api/v1/shop/schedule/time-off/preview", new { professionalId = s.ProA, kind = "Vacation", startDate = Iso(Target), endDate = Iso(Target) }, ct), HttpStatusCode.NotFound, null, ct);

        // Shop B's own schedule shows none of shop A's rows; shop A's are untouched.
        var bSchedule = await OkAsync(b.GetAsync("/api/v1/shop/schedule", ct), ct);
        bSchedule.GetProperty("closures").GetArrayLength().ShouldBe(0);
        bSchedule.GetProperty("breaks").GetArrayLength().ShouldBe(0);
        bSchedule.GetProperty("timeOff").GetArrayLength().ShouldBe(0);
        bSchedule.GetProperty("professionals").EnumerateArray().Select(p => p.GetProperty("professionalId").GetGuid()).ShouldBe([s.ProB]);
        bSchedule.GetProperty("openingHours").GetProperty("intervals").GetArrayLength().ShouldBe(0);
        var aSchedule = await OkAsync(s.OwnerA.GetAsync("/api/v1/shop/schedule", ct), ct);
        aSchedule.GetProperty("closures").GetArrayLength().ShouldBe(1);
        aSchedule.GetProperty("breaks").GetArrayLength().ShouldBe(1);
        aSchedule.GetProperty("timeOff")[0].GetProperty("startMinute").GetInt32().ShouldBe(600);

        // The customer and admin user types have no shop schedule.
        using var customer = await IdentityTestData.SignInCustomerAsync(s.Factory, IdentityTestData.NewPhone(), ct);
        (await customer.GetAsync("/api/v1/shop/schedule", ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PublicAvailability_Gates_ShopSubscriptionItemAndProfessional()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var s = await ArrangeAsync("avl_gates", ct, subscribeA: false);
        using var anonymous = ApiSession.Create(s.Factory);
        await OkAsync(s.OwnerA.PutAsync("/api/v1/shop/schedule/opening-hours", new { intervals = EveryDay(540, 720) }, ct), ct);

        // D-078: no subscription in force → no slots, with the reason.
        var none = await SlotsAsync(anonymous, s.SlugA, $"serviceId={s.ServiceA}", Target, ct);
        none.GetProperty("bookable").GetBoolean().ShouldBeFalse();
        none.GetProperty("blockedReason").GetString().ShouldBe("subscription.none");
        await SubscribeAsync(s.Admin, s.Shops.A.ShopId, ct);
        (await SlotTimesAsync(anonymous, s.SlugA, s.ServiceA, Target, ct)).Length.ShouldBe(31);

        // Bad requests and unknown items.
        await FailsAsync(anonymous.GetAsync($"/api/v1/public/shops/no-such-shop/availability/slots?serviceId={s.ServiceA}&date={Iso(Target)}", ct), HttpStatusCode.NotFound, "shop.not_found", ct);
        await FailsAsync(anonymous.GetAsync($"/api/v1/public/shops/{s.SlugA}/availability/slots?date={Iso(Target)}", ct), HttpStatusCode.BadRequest, null, ct);
        await FailsAsync(anonymous.GetAsync($"/api/v1/public/shops/{s.SlugA}/availability/slots?serviceId={Guid.NewGuid()}&date={Iso(Target)}", ct), HttpStatusCode.NotFound, "availability.offer_not_found", ct);
        await FailsAsync(anonymous.GetAsync($"/api/v1/public/shops/{s.SlugA}/availability/slots?serviceId={s.ServiceB}&date={Iso(Target)}", ct), HttpStatusCode.NotFound, "availability.offer_not_found", ct);
        await FailsAsync(anonymous.GetAsync($"/api/v1/public/shops/{s.SlugA}/availability/dates?serviceId={s.ServiceA}&from={Iso(Target)}&to={Iso(Target.AddDays(40))}", ct), HttpStatusCode.BadRequest, null, ct);

        // A professional of another shop, or one not assigned to the service.
        await FailsAsync(anonymous.GetAsync($"/api/v1/public/shops/{s.SlugA}/availability/slots?serviceId={s.ServiceA}&professionalId={s.ProB}&date={Iso(Target)}", ct), HttpStatusCode.NotFound, "professional.not_found", ct);
        var unassigned = await CreateProfessionalAsync(s.Admin, s.Shops.A.ShopId, "بدر", "Badr", ct);
        await FailsAsync(anonymous.GetAsync($"/api/v1/public/shops/{s.SlugA}/availability/slots?serviceId={s.ServiceA}&professionalId={unassigned}&date={Iso(Target)}", ct),
            HttpStatusCode.UnprocessableEntity, "availability.professional_not_eligible", ct);
        (await SlotTimesAsync(anonymous, s.SlugA, s.ServiceA, Target, ct, s.ProA)).Length.ShouldBe(31);

        // A service customers cannot book online.
        var walkInOnly = (await OkAsync(s.OwnerA.PostAsync("/api/v1/shop/services", new { nameAr = "خدمة في المحل", price = 20m, durationMinutes = 15, onlineBookable = false }, ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await FailsAsync(anonymous.GetAsync($"/api/v1/public/shops/{s.SlugA}/availability/slots?serviceId={walkInOnly}&date={Iso(Target)}", ct), HttpStatusCode.UnprocessableEntity, "availability.offer_not_online_bookable", ct);

        // A package: one contiguous appointment with a professional assigned to every item.
        var beard = (await OkAsync(s.OwnerA.PostAsync("/api/v1/shop/services", new { nameAr = "لحية", price = 30m, durationMinutes = 20, onlineBookable = true }, ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var package = (await OkAsync(s.OwnerA.PostAsync("/api/v1/shop/packages", new { nameAr = "باقة", price = 80m, durationMinutes = 60, serviceIds = new[] { s.ServiceA, beard } }, ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        (await SlotsAsync(anonymous, s.SlugA, $"packageId={package}", Target, ct)).GetProperty("slots").GetArrayLength().ShouldBe(0, "nobody does both services yet");
        await OkAsync(s.Admin.PutAsync($"/api/v1/admin/professionals/{s.ProA}/services", new { serviceIds = new[] { s.ServiceA, beard } }, ct), ct);
        var packageSlots = (await SlotsAsync(anonymous, s.SlugA, $"packageId={package}", Target, ct)).GetProperty("slots");
        packageSlots.GetArrayLength().ShouldBe(25, "60 minutes between 09:00 and 12:00 on the 5-minute grid");
        packageSlots[0].GetProperty("professionalIds").EnumerateArray().Select(p => p.GetGuid()).ShouldBe([s.ProA]);

        // A disabled professional drops out; a suspended shop is not found.
        await OkAsync(s.Admin.PostAsync($"/api/v1/admin/professionals/{s.ProA}/disable", new { reason = "Left the shop" }, ct), ct);
        (await SlotTimesAsync(anonymous, s.SlugA, s.ServiceA, Target, ct)).ShouldBeEmpty();
        await OkAsync(s.Admin.PostAsync($"/api/v1/admin/shops/{s.Shops.A.ShopId}/suspend", new { reason = "Review" }, ct), ct);
        await FailsAsync(anonymous.GetAsync($"/api/v1/public/shops/{s.SlugA}/availability/slots?serviceId={s.ServiceA}&date={Iso(Target)}", ct), HttpStatusCode.NotFound, "shop.not_found", ct);
    }

    [Fact]
    public async Task ConflictPreview_ListsOverlappingBookings_WithoutPhones_AndBookedTimeIsNotOffered()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var s = await ArrangeAsync("avl_conflicts", ct);
        using var anonymous = ApiSession.Create(s.Factory);
        await OkAsync(s.OwnerA.PutAsync("/api/v1/shop/schedule/opening-hours", new { intervals = EveryDay(540, 720) }, ct), ct);
        var tenOClock = new DateTimeOffset(Target.Year, Target.Month, Target.Day, 10, 0, 0, TimeSpan.FromHours(3));
        s.Bookings.Appointments.Add(new BookedAppointment(Guid.NewGuid(), new ProfessionalId(s.ProA), tenOClock, tenOClock.AddMinutes(30), "Test Customer", "حلاقة", "Haircut"));

        // The booked half hour is not offered (and no 30-minute item may overlap it).
        var times = await SlotTimesAsync(anonymous, s.SlugA, s.ServiceA, Target, ct);
        times.ShouldNotContain("10:00");
        times.ShouldNotContain("09:35");
        times.ShouldContain("09:30");
        times.ShouldContain("10:30");

        // Time off, a break and a closure over it list the booking; nothing is saved by a preview.
        foreach (var (path, body) in new (string, object)[]
                 {
                     ("time-off", new { professionalId = s.ProA, kind = "Sick", startDate = Iso(Target), endDate = Iso(Target) }),
                     ("breaks", new { label = "صلاة", weekdays = new[] { Target.DayOfWeek.ToString() }, startMinute = 600, endMinute = 615 }),
                     ("closures", new { startDate = Iso(Target), endDate = Iso(Target) }),
                 })
        {
            using var response = await s.OwnerA.PostAsync($"/api/v1/shop/schedule/{path}/preview", body, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
            var affected = (await response.JsonAsync(ct)).GetProperty("affectedBookings");
            affected.GetArrayLength().ShouldBe(1, path);
            affected[0].GetProperty("customerName").GetString().ShouldBe("Test Customer");
            text.ShouldNotContain("phone", Case.Insensitive);
            text.ShouldNotContain("+966");
        }

        // A break that does not touch the booking lists nothing.
        var clear = await OkAsync(s.OwnerA.PostAsync("/api/v1/shop/schedule/breaks/preview", new { label = "x", weekdays = new[] { Target.DayOfWeek.ToString() }, startMinute = 660, endMinute = 690 }, ct), ct);
        clear.GetProperty("affectedBookings").GetArrayLength().ShouldBe(0);
        (await OkAsync(s.OwnerA.GetAsync("/api/v1/shop/schedule", ct), ct)).GetProperty("breaks").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task DemoSeed_GivesTheDemoShopsHoursBreaksAndBookableSlots()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await TrimmeApiFactory.CreateMigratedAsync(postgres, "avl_seed", ct);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            foreach (var seeder in scope.ServiceProvider.GetServices<IDevSeeder>().OrderBy(x => x.Order))
            {
                await seeder.SeedAsync(scope.ServiceProvider, ct);
            }
        }

        using var anonymous = ApiSession.Create(factory);
        var haircut = Guid.Parse("0199a0de-5a10-7000-8000-000000000401");
        var dates = await OkAsync(anonymous.GetAsync($"/api/v1/public/shops/{DemoData.AlAsala.Slug}/availability/dates?serviceId={haircut}", ct), ct);
        dates.GetProperty("bookable").GetBoolean().ShouldBeTrue();
        dates.GetProperty("dates").EnumerateArray().Count(d => d.GetProperty("slotCount").GetInt32() > 0).ShouldBeGreaterThanOrEqualTo(10, "open six days a week");

        // Asr prayer (15:30–16:00) is never offered at Al Asala.
        var sunday = Enumerable.Range(1, 7).Select(Today.AddDays).First(d => d.DayOfWeek == DayOfWeek.Sunday);
        var times = await SlotTimesAsync(anonymous, DemoData.AlAsala.Slug, haircut, sunday, ct);
        times.ShouldNotContain("15:30");
        times.ShouldNotContain("15:10", "a 30-minute haircut from 15:10 runs into the prayer break");
        times.ShouldContain("16:00");

        using var owner = await IdentityTestData.SignInStaffAsync(factory, DemoData.AlAsala.OwnerEmail, ct, DemoData.DefaultPassword);
        var schedule = await OkAsync(owner.GetAsync("/api/v1/shop/schedule", ct), ct);
        schedule.GetProperty("breaks").GetArrayLength().ShouldBe(3);
        schedule.GetProperty("professionals").EnumerateArray().Count(p => !p.GetProperty("followsShopHours").GetBoolean()).ShouldBe(1, "Sultan has his own hours");
    }

    private async Task<Arranged> ArrangeAsync(string prefix, CancellationToken ct, bool subscribeA = true)
    {
        var bookings = new FakeBookings();
        var factory = new TrimmeApiFactory(await postgres.CreateDatabaseAsync(prefix, ct))
        {
            ConfigureTestServices = services => services.AddSingleton<IBookedTimeReader>(bookings),
        };
        await factory.MigrateAsync(ct);
        var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shops = await ShopTestData.CreateTwoShopsAsync(factory, admin, ct);
        var ownerA = await IdentityTestData.SignInStaffAsync(factory, shops.A.OwnerEmail, ct);
        var staffA = await IdentityTestData.SignInStaffAsync(factory, shops.A.StaffEmail, ct);
        var ownerB = await IdentityTestData.SignInStaffAsync(factory, shops.B.OwnerEmail, ct);

        var serviceA = (await OkAsync(ownerA.PostAsync("/api/v1/shop/services", new { nameAr = "حلاقة", price = 60m, durationMinutes = 30, onlineBookable = true }, ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var serviceB = (await OkAsync(ownerB.PostAsync("/api/v1/shop/services", new { nameAr = "حلاقة", price = 50m, durationMinutes = 30, onlineBookable = true }, ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var proA = await CreateProfessionalAsync(admin, shops.A.ShopId, "فيصل", "Faisal", ct);
        var proB = await CreateProfessionalAsync(admin, shops.B.ShopId, "عمر", "Omar", ct);
        await OkAsync(admin.PutAsync($"/api/v1/admin/professionals/{proA}/services", new { serviceIds = new[] { serviceA } }, ct), ct);
        await OkAsync(admin.PutAsync($"/api/v1/admin/professionals/{proB}/services", new { serviceIds = new[] { serviceB } }, ct), ct);
        if (subscribeA)
        {
            await SubscribeAsync(admin, shops.A.ShopId, ct);
        }

        var slugA = (await OkAsync(admin.GetAsync($"/api/v1/admin/shops/{shops.A.ShopId}", ct), ct)).GetProperty("slug").GetString()!;
        return new Arranged(factory, bookings, admin, shops, ownerA, staffA, ownerB, slugA, serviceA, serviceB, proA, proB);
    }

    private static async Task<Guid> CreateProfessionalAsync(ApiSession admin, Guid shopId, string nameAr, string nameEn, CancellationToken ct) =>
        (await OkAsync(admin.PostAsync("/api/v1/admin/professionals", new { shopId, nameAr, nameEn }, ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();

    /// <summary>A published plan and a standard subscription, so the shop takes online bookings (D-078).</summary>
    private static async Task SubscribeAsync(ApiSession admin, Guid shopId, CancellationToken ct)
    {
        var plan = new
        {
            nameAr = "باقة", nameEn = "Plan", descriptionAr = (string?)null, descriptionEn = (string?)null, features = new[] { new { ar = "ظهور", en = "Listed" } },
            maxProfessionals = (int?)null, maxServices = (int?)null, intervalUnit = "Month", intervalCount = 12, trialDays = (int?)null, graceDays = (int?)null,
            availableToNewShops = true, initialPrice = 1900m,
        };
        var planId = (await OkAsync(admin.PostAsync("/api/v1/admin/subscription-plans", plan, ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await OkAsync(admin.PostAsync($"/api/v1/admin/subscription-plans/{planId}/publish", null, ct), ct);
        await OkAsync(admin.PostAsync($"/api/v1/admin/shops/{shopId}/subscription/assign", new { planId }, ct), ct);
    }

    private sealed record Arranged(
        TrimmeApiFactory Factory,
        FakeBookings Bookings,
        ApiSession Admin,
        TwoShops Shops,
        ApiSession OwnerA,
        ApiSession StaffA,
        ApiSession OwnerB,
        string SlugA,
        Guid ServiceA,
        Guid ServiceB,
        Guid ProA,
        Guid ProB) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Admin.Dispose();
            OwnerA.Dispose();
            StaffA.Dispose();
            OwnerB.Dispose();
            await Factory.DisposeAsync();
        }
    }

    /// <summary>Stands in for the Bookings module (Phase 10): the appointments the test adds.</summary>
    private sealed class FakeBookings : IBookedTimeReader
    {
        public List<BookedAppointment> Appointments { get; } = [];

        public Task<IReadOnlyList<BusyTime>> GetBusyAsync(
            ShopId shopId, IReadOnlyCollection<ProfessionalId> professionalIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BusyTime>>(
            [
                .. Appointments.Where(a => professionalIds.Contains(a.ProfessionalId) && a.StartsAt < to && from < a.EndsAt)
                    .Select(a => new BusyTime(a.ProfessionalId, a.StartsAt, a.EndsAt)),
            ]);

        public Task<IReadOnlyList<BookedAppointment>> GetAppointmentsAsync(
            ShopId shopId, ProfessionalId? professionalId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BookedAppointment>>(
                [.. Appointments.Where(a => (professionalId is null || a.ProfessionalId == professionalId) && a.StartsAt < to && from < a.EndsAt)]);
    }
}
