using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Trimme.IntegrationTests.Identity;
using Trimme.IntegrationTests.Infrastructure;
using static Trimme.IntegrationTests.Bookings.BookingTestData;

namespace Trimme.IntegrationTests.Bookings;

/// <summary>
/// The shop's operations board (spec §13, R-SD-01…05, D-100): overview KPIs and load, the calendar's lanes and business
/// days (a window past midnight keeps its bookings), the appointments list's status chips, and the walk-in choices.
/// </summary>
public sealed class ShopDashboardTests(PostgresFixture postgres)
{
    private static DateOnly Target => TodayAt(DateTimeOffset.UtcNow).AddDays(2);

    private static async Task<JsonElement> WalkInAsync(ApiSession owner, Guid serviceId, Guid professionalId, DateTimeOffset? start, CancellationToken ct) =>
        await OkAsync(owner.PostAsync("/api/v1/shop/bookings/walk-in", new { serviceId, professionalId, startsAt = start, customerName = "زائر الحي" }, ct), ct, HttpStatusCode.Created);

    private static async Task CancelAsync(ApiSession owner, JsonElement booking, CancellationToken ct) =>
        await OkAsync(owner.PostAsync($"/api/v1/shop/bookings/{booking.GetProperty("id").GetGuid()}/transitions",
            new { to = "CancelledByShop", reason = "إغلاق مبكر", version = booking.GetProperty("version").GetUInt32() }, ct), ct);

    private static JsonElement Lane(JsonElement list, Guid id) =>
        list.EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == id);

    [Fact]
    public async Task Overview_CountsTheBusinessDay_WithEachProfessionalsLoad_AndTheLastWeekByHour()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "board_overview", ct);
        using var noura = await CustomerAsync(w.Factory, "نورة", ct);

        // A shop-wide break 13:00–14:00 on the day, and Faisal on leave the day after.
        await OkAsync(w.OwnerA.PostAsync("/api/v1/shop/schedule/breaks",
            new { professionalId = (Guid?)null, label = "صلاة الظهر", weekdays = (string[]?)null, date = Iso(Target), startMinute = 780, endMinute = 840 }, ct), ct, HttpStatusCode.Created);
        await OkAsync(w.OwnerA.PostAsync("/api/v1/shop/schedule/time-off",
            new { professionalId = w.Faisal, kind = "Vacation", startDate = Iso(Target.AddDays(1)), endDate = Iso(Target.AddDays(1)) }, ct), ct, HttpStatusCode.Created);

        await WalkInAsync(w.OwnerA, w.Haircut, w.Faisal, At(Target, 10), ct);
        await WalkInAsync(w.OwnerA, w.Haircut, w.Faisal, At(Target, 11), ct);
        var cancelled = await WalkInAsync(w.OwnerA, w.Haircut, w.Omar, At(Target, 10), ct);
        await CancelAsync(w.OwnerA, cancelled, ct);
        await OkAsync(BookAsync(noura, w.SlugA, w.Haircut, w.Omar, At(Target, 12), ct), ct, HttpStatusCode.Created);
        await WalkInAsync(w.OwnerA, w.Haircut, w.Faisal, At(Target.AddDays(-1), 10), ct);

        var overview = await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/dashboard/overview?date={Iso(Target)}", ct), ct);
        overview.ToString().ShouldNotContain("+966");
        var kpis = overview.GetProperty("kpis");
        kpis.GetProperty("total").GetInt32().ShouldBe(3);
        kpis.GetProperty("previousDayTotal").GetInt32().ShouldBe(1);
        kpis.GetProperty("cancelled").GetInt32().ShouldBe(1);
        kpis.GetProperty("completed").GetInt32().ShouldBe(0);
        kpis.GetProperty("noShow").GetInt32().ShouldBe(0);
        kpis.GetProperty("pendingUpcoming").GetInt32().ShouldBe(0, "auto-confirm (D-006)");

        // Minutes-based load: 12 open hours minus the one-hour break = 660 bookable minutes each.
        var faisal = Lane(overview.GetProperty("professionals"), w.Faisal);
        faisal.GetProperty("bookings").GetInt32().ShouldBe(2);
        faisal.GetProperty("bookedMinutes").GetInt32().ShouldBe(60);
        faisal.GetProperty("availableMinutes").GetInt32().ShouldBe(660);
        faisal.GetProperty("onLeave").GetBoolean().ShouldBeFalse();
        var omar = Lane(overview.GetProperty("professionals"), w.Omar);
        omar.GetProperty("bookedMinutes").GetInt32().ShouldBe(30);
        kpis.GetProperty("freeMinutes").GetInt32().ShouldBe((660 - 60) + (660 - 30));

        overview.GetProperty("upcoming").EnumerateArray().Select(b => b.GetProperty("startsAt").GetDateTimeOffset())
            .ShouldBe([At(Target, 10), At(Target, 11), At(Target, 12)]);
        var hourly = overview.GetProperty("hourly").EnumerateArray().ToDictionary(h => h.GetProperty("hour").GetInt32(), h => h.GetProperty("count").GetInt32());
        hourly[10].ShouldBe(2, "the day before counts in the last seven days; the cancellation does not");
        hourly[11].ShouldBe(1);
        hourly[12].ShouldBe(1);
        hourly[13].ShouldBe(0);

        var leave = await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/dashboard/overview?date={Iso(Target.AddDays(1))}", ct), ct);
        var faisalOff = Lane(leave.GetProperty("professionals"), w.Faisal);
        faisalOff.GetProperty("onLeave").GetBoolean().ShouldBeTrue();
        faisalOff.GetProperty("availableMinutes").GetInt32().ShouldBe(0);

        // Shop B sees its own empty day.
        (await OkAsync(w.OwnerB.GetAsync($"/api/v1/shop/dashboard/overview?date={Iso(Target)}", ct), ct)).GetProperty("kpis").GetProperty("total").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Calendar_ShowsLanes_Bookings_AndKeepsTheHoursAfterMidnightInTheirBusinessDay()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "board_calendar", ct);

        // Open 09:00 until 01:00 the next morning, every day.
        var schedule = await OkAsync(w.OwnerA.GetAsync("/api/v1/shop/schedule", ct), ct);
        var late = Enum.GetValues<DayOfWeek>().Select(d => new { day = d.ToString(), startMinute = 540, endMinute = 1500 }).ToArray();
        await OkAsync(w.OwnerA.PutAsync("/api/v1/shop/schedule/opening-hours",
            new { intervals = late, version = schedule.GetProperty("openingHours").GetProperty("version").GetUInt32() }, ct), ct);
        await OkAsync(w.OwnerA.PostAsync("/api/v1/shop/schedule/time-off",
            new { professionalId = w.Omar, kind = "Sick", startDate = Iso(Target), endDate = Iso(Target), startMinute = 900, endMinute = 960 }, ct), ct, HttpStatusCode.Created);

        var night = await WalkInAsync(w.OwnerA, w.Haircut, w.Faisal, At(Target.AddDays(1), 0, 30), ct);
        var morning = await WalkInAsync(w.OwnerA, w.Haircut, w.Faisal, At(Target, 10), ct);
        var cancelled = await WalkInAsync(w.OwnerA, w.Haircut, w.Omar, At(Target, 11), ct);
        await CancelAsync(w.OwnerA, cancelled, ct);

        var calendar = await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/calendar?from={Iso(Target)}", ct), ct);
        calendar.ToString().ShouldNotContain("+966");
        var day = calendar.GetProperty("days").EnumerateArray().ShouldHaveSingleItem();
        day.GetProperty("open")[0].GetProperty("end").GetDateTimeOffset().ShouldBe(At(Target.AddDays(1), 1));
        day.GetProperty("bookings").EnumerateArray().Select(b => b.GetProperty("id").GetGuid())
            .ShouldBe([morning.GetProperty("id").GetGuid(), night.GetProperty("id").GetGuid()], "00:30 belongs to the day whose window it is in; cancellations are not drawn");
        day.GetProperty("bookings")[0].GetProperty("customerName").GetString().ShouldBe("زائر الحي");
        var omarLane = day.GetProperty("lanes").EnumerateArray().Single(l => l.GetProperty("professionalId").GetGuid() == w.Omar);
        omarLane.GetProperty("timeOff")[0].GetProperty("start").GetDateTimeOffset().ShouldBe(At(Target, 15));

        // The next day starts without it; a week at most; one professional on demand.
        var next = await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/calendar?from={Iso(Target.AddDays(1))}", ct), ct);
        next.GetProperty("days")[0].GetProperty("bookings").GetArrayLength().ShouldBe(0);
        var week = await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/calendar?from={Iso(Target)}&to={Iso(Target.AddDays(6))}&professionalId={w.Omar}", ct), ct);
        week.GetProperty("days").GetArrayLength().ShouldBe(7);
        week.GetProperty("professionals").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ShouldBe([w.Omar]);
        await FailsAsync(w.OwnerA.GetAsync($"/api/v1/shop/calendar?from={Iso(Target)}&to={Iso(Target.AddDays(7))}", ct), HttpStatusCode.BadRequest, "validation.out_of_range", ct);

        // Another shop's calendar never shows these.
        var other = await OkAsync(w.OwnerB.GetAsync($"/api/v1/shop/calendar?from={Iso(Target)}", ct), ct);
        other.GetProperty("days")[0].GetProperty("bookings").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task AppointmentsList_TakesAnyOfSeveralStatuses_AndCountsEveryChipWithTheOtherFilters()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var w = await ArrangeAsync(postgres, "board_list", ct, clock);
        clock.SetUtcNow(At(Target, 10, 2).ToUniversalTime());
        using var owner = await IdentityTestData.SignInStaffAsync(w.Factory, w.Shops.A.OwnerEmail, ct);

        var now = await WalkInAsync(owner, w.Haircut, w.Omar, null, ct);
        now.GetProperty("status").GetString().ShouldBe("Arrived");
        await WalkInAsync(owner, w.Haircut, w.Faisal, At(Target, 12), ct);
        await WalkInAsync(owner, w.Haircut, w.Faisal, At(Target, 13), ct);
        await CancelAsync(owner, await WalkInAsync(owner, w.Haircut, w.Omar, At(Target, 14), ct), ct);

        var list = await OkAsync(owner.GetAsync($"/api/v1/shop/bookings?from={Iso(Target)}&to={Iso(Target)}&status=Confirmed&status=Arrived", ct), ct);
        list.GetProperty("total").GetInt32().ShouldBe(3);
        var counts = list.GetProperty("counts");
        counts.GetProperty("all").GetInt32().ShouldBe(4);
        counts.GetProperty("confirmed").GetInt32().ShouldBe(2);
        counts.GetProperty("arrived").GetInt32().ShouldBe(1);
        counts.GetProperty("cancelled").GetInt32().ShouldBe(1);
        counts.GetProperty("pending").GetInt32().ShouldBe(0);

        // The chips follow the professional filter too.
        var faisal = await OkAsync(owner.GetAsync($"/api/v1/shop/bookings?from={Iso(Target)}&to={Iso(Target)}&professionalId={w.Faisal}&status=CancelledByShop&status=CancelledByCustomer", ct), ct);
        faisal.GetProperty("total").GetInt32().ShouldBe(0);
        faisal.GetProperty("counts").GetProperty("all").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task WalkInOptions_ListTheActiveAssignedProfessionals_WhetherFreeNow_AndTheDaysFreeStarts()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var w = await ArrangeAsync(postgres, "board_walkin", ct, clock);
        var nasser = (await OkAsync(w.Admin.PostAsync("/api/v1/admin/professionals", new { shopId = w.Shops.A.ShopId, nameAr = "ناصر", nameEn = "Nasser" }, ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await OkAsync(w.Admin.PutAsync($"/api/v1/admin/professionals/{nasser}/services", new { serviceIds = new[] { w.Haircut } }, ct), ct);
        await OkAsync(w.Admin.PostAsync($"/api/v1/admin/professionals/{nasser}/disable", new { reason = "إجازة طويلة" }, ct), ct);

        clock.SetUtcNow(At(Target, 10, 2).ToUniversalTime());
        using var owner = await IdentityTestData.SignInStaffAsync(w.Factory, w.Shops.A.OwnerEmail, ct);
        await WalkInAsync(owner, w.Haircut, w.Faisal, At(Target, 10), ct);

        var options = await OkAsync(owner.GetAsync($"/api/v1/shop/availability/walk-in?serviceId={w.Haircut}", ct), ct);
        options.GetProperty("date").GetString().ShouldBe(Iso(Target));
        options.GetProperty("durationMinutes").GetInt32().ShouldBe(30);
        var pros = options.GetProperty("professionals").EnumerateArray().ToDictionary(p => p.GetProperty("id").GetGuid());
        pros.Keys.ShouldBe([w.Faisal, w.Omar], ignoreOrder: true);
        pros[w.Faisal].GetProperty("freeNow").GetBoolean().ShouldBeFalse();
        pros[w.Faisal].GetProperty("nextFreeAt").GetDateTimeOffset().ShouldBe(At(Target, 10, 30));
        pros[w.Omar].GetProperty("freeNow").GetBoolean().ShouldBeTrue();
        pros[w.Faisal].GetProperty("starts").EnumerateArray().Select(s => s.GetDateTimeOffset()).ShouldNotContain(At(Target, 10, 15));

        // A service that cannot be booked online is still offered at the desk.
        var deskOnly = (await OkAsync(owner.PostAsync("/api/v1/shop/services", new { nameAr = "حلاقة سريعة", price = 20m, durationMinutes = 15, onlineBookable = false }, ct), ct, HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        using var admin = await IdentityTestData.SignInNewStaffAsync(w.Factory, Trimme.Modules.Identity.Domain.SystemRoles.SuperAdmin, ct); // after the clock jump
        await OkAsync(admin.PutAsync($"/api/v1/admin/professionals/{w.Omar}/services", new { serviceIds = new[] { w.Haircut, w.Beard, deskOnly } }, ct), ct);
        var desk = await OkAsync(owner.GetAsync($"/api/v1/shop/availability/walk-in?serviceId={deskOnly}&date={Iso(Target.AddDays(1))}", ct), ct);
        desk.GetProperty("professionals").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ShouldBe([w.Omar]);
        desk.GetProperty("professionals")[0].GetProperty("freeNow").GetBoolean().ShouldBeFalse("not today");
        desk.GetProperty("professionals")[0].GetProperty("starts").GetArrayLength().ShouldBeGreaterThan(0);

        await FailsAsync(owner.GetAsync($"/api/v1/shop/availability/walk-in?serviceId={w.Haircut}&date={Iso(Target.AddDays(-1))}", ct), HttpStatusCode.BadRequest, "validation.out_of_range", ct);
        await FailsAsync(owner.GetAsync($"/api/v1/shop/availability/walk-in?serviceId={w.ServiceB}", ct), HttpStatusCode.NotFound, null, ct);
        await FailsAsync(owner.GetAsync("/api/v1/shop/availability/walk-in", ct), HttpStatusCode.NotFound, null, ct);
    }
}
