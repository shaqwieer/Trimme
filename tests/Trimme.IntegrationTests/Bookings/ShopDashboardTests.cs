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

    /// <summary>A walk-in booked by the barber themselves: walk-ins are the barbers', not the owner's (D-146).</summary>
    private static async Task<JsonElement> WalkInAsync(BookingWorld w, Guid serviceId, Guid professionalId, DateTimeOffset? start, CancellationToken ct)
    {
        using var barber = await w.BarberAsync(professionalId, ct);
        return await OkAsync(barber.PostAsync("/api/v1/shop/bookings/walk-in", new { serviceId, professionalId, startsAt = start, customerName = "زائر الحي", customerPhone = "0501234567" }, ct), ct, HttpStatusCode.Created);
    }

    /// <summary>The booking's own barber cancels it: the owner only watches (D-147).</summary>
    private static async Task CancelAsync(BookingWorld w, JsonElement booking, CancellationToken ct)
    {
        var id = booking.GetProperty("id").GetGuid();
        using var barber = await w.BarberOfAsync(id, ct);
        await OkAsync(barber.PostAsync($"/api/v1/shop/bookings/{id}/transitions",
            new { to = "CancelledByShop", reason = "إغلاق مبكر", version = booking.GetProperty("version").GetUInt32() }, ct), ct);
    }

    private static JsonElement Lane(JsonElement list, Guid id) =>
        list.EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == id);

    [Fact]
    public async Task Overview_CountsTheBusinessDay_WithEachProfessionalsLoad_AndTheLastWeekByHour()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "board_overview", ct);
        using var noura = await CustomerAsync(w.Factory, "نورة", ct);

        // A weekly break for everyone 13:00–14:00 on the day's weekday (the owner's breaks are weekly, D-147), and Faisal on
        // leave the day after.
        await OkAsync(w.OwnerA.PostAsync("/api/v1/shop/schedule/breaks",
            new { professionalId = (Guid?)null, label = "صلاة الظهر", weekdays = new[] { Target.DayOfWeek.ToString() }, startMinute = 780, endMinute = 840 }, ct), ct, HttpStatusCode.Created);
        await OkAsync(w.OwnerA.PostAsync("/api/v1/shop/schedule/time-off",
            new { professionalId = w.Faisal, kind = "Vacation", startDate = Iso(Target.AddDays(1)), endDate = Iso(Target.AddDays(1)) }, ct), ct, HttpStatusCode.Created);

        await WalkInAsync(w, w.Haircut, w.Faisal, At(Target, 10), ct);
        await WalkInAsync(w, w.Haircut, w.Faisal, At(Target, 11), ct);
        var cancelled = await WalkInAsync(w, w.Haircut, w.Omar, At(Target, 10), ct);
        await CancelAsync(w, cancelled, ct);
        await OkAsync(BookAsync(noura, w.SlugA, w.Haircut, w.Omar, At(Target, 12), ct), ct, HttpStatusCode.Created);
        await WalkInAsync(w, w.Haircut, w.Faisal, At(Target.AddDays(-1), 10), ct);

        var overview = await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/dashboard/overview?date={Iso(Target)}", ct), ct);
        overview.ToString().ShouldNotContain("+966");
        var kpis = overview.GetProperty("kpis");
        kpis.GetProperty("total").GetInt32().ShouldBe(3);
        kpis.GetProperty("previousDayTotal").GetInt32().ShouldBe(1);
        kpis.GetProperty("cancelled").GetInt32().ShouldBe(1);
        kpis.GetProperty("completed").GetInt32().ShouldBe(0);
        kpis.GetProperty("noShow").GetInt32().ShouldBe(0);
        kpis.TryGetProperty("pendingUpcoming", out _).ShouldBeFalse("every online booking is confirmed at once (D-155)");

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

        var night = await WalkInAsync(w, w.Haircut, w.Faisal, At(Target.AddDays(1), 0, 30), ct);
        var morning = await WalkInAsync(w, w.Haircut, w.Faisal, At(Target, 10), ct);
        var cancelled = await WalkInAsync(w, w.Haircut, w.Omar, At(Target, 11), ct);
        await CancelAsync(w, cancelled, ct);

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
        await w.EnsureBarberLoginAsync(w.Omar, ct);
        await w.EnsureBarberLoginAsync(w.Faisal, ct);
        clock.SetUtcNow(At(Target, 10, 2).ToUniversalTime());
        using var owner = await IdentityTestData.SignInStaffAsync(w.Factory, w.Shops.A.OwnerEmail, ct);

        var now = await WalkInAsync(w, w.Haircut, w.Omar, null, ct);
        now.GetProperty("status").GetString().ShouldBe("Arrived");
        await WalkInAsync(w, w.Haircut, w.Faisal, At(Target, 12), ct);
        await WalkInAsync(w, w.Haircut, w.Faisal, At(Target, 13), ct);
        await CancelAsync(w, await WalkInAsync(w, w.Haircut, w.Omar, At(Target, 14), ct), ct);

        var list = await OkAsync(owner.GetAsync($"/api/v1/shop/bookings?from={Iso(Target)}&to={Iso(Target)}&status=Confirmed&status=Arrived", ct), ct);
        list.GetProperty("total").GetInt32().ShouldBe(3);
        var counts = list.GetProperty("counts");
        counts.GetProperty("all").GetInt32().ShouldBe(4);
        counts.GetProperty("confirmed").GetInt32().ShouldBe(2);
        counts.GetProperty("arrived").GetInt32().ShouldBe(1);
        counts.GetProperty("cancelled").GetInt32().ShouldBe(1);
        counts.TryGetProperty("pending", out _).ShouldBeFalse("D-155: nothing waits for the shop");

        // The chips follow the professional filter too.
        var faisal = await OkAsync(owner.GetAsync($"/api/v1/shop/bookings?from={Iso(Target)}&to={Iso(Target)}&professionalId={w.Faisal}&status=CancelledByShop&status=CancelledByCustomer", ct), ct);
        faisal.GetProperty("total").GetInt32().ShouldBe(0);
        faisal.GetProperty("counts").GetProperty("all").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task WalkInOptions_ShowEachBarberWhetherFreeNow_AndTheDaysFreeStarts_AndTheOwnerHasNone()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var w = await ArrangeAsync(postgres, "board_walkin", ct, clock);
        await w.EnsureBarberLoginAsync(w.Faisal, ct);
        await w.EnsureBarberLoginAsync(w.Omar, ct);

        clock.SetUtcNow(At(Target, 10, 2).ToUniversalTime());
        using var owner = await IdentityTestData.SignInStaffAsync(w.Factory, w.Shops.A.OwnerEmail, ct);
        using var faisal = await w.BarberAsync(w.Faisal, ct);
        using var omar = await w.BarberAsync(w.Omar, ct);
        await WalkInAsync(w, w.Haircut, w.Faisal, At(Target, 10), ct);

        // Walk-ins are the barbers' (D-146): the owner neither sees the choices nor books one.
        await FailsAsync(owner.GetAsync($"/api/v1/shop/availability/walk-in?serviceId={w.Haircut}", ct), HttpStatusCode.Forbidden, null, ct);
        await FailsAsync(owner.PostAsync("/api/v1/shop/bookings/walk-in", new { serviceId = w.Haircut, professionalId = w.Omar, customerName = "زائر الحي" }, ct),
            HttpStatusCode.Forbidden, null, ct);

        // Each barber is offered themselves first, then the colleagues they may book for (D-147): busy until 10:30, or free now.
        var options = await OkAsync(faisal.GetAsync($"/api/v1/shop/availability/walk-in?serviceId={w.Haircut}", ct), ct);
        options.GetProperty("date").GetString().ShouldBe(Iso(Target));
        options.GetProperty("durationMinutes").GetInt32().ShouldBe(30);
        options.GetProperty("professionals").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ShouldBe([w.Faisal, w.Omar]);
        var own = options.GetProperty("professionals")[0];
        own.GetProperty("freeNow").GetBoolean().ShouldBeFalse();
        own.GetProperty("nextFreeAt").GetDateTimeOffset().ShouldBe(At(Target, 10, 30));
        own.GetProperty("starts").EnumerateArray().Select(s => s.GetDateTimeOffset()).ShouldNotContain(At(Target, 10, 15));

        // D-158: the same starts with their local time, and when the barber works from now on, so the walk-in shows the
        // hour, then its minutes, with the taken ones greyed, as the customer's booking does.
        var times = own.GetProperty("times").EnumerateArray().ToList();
        times.Select(t => t.GetProperty("startsAt").GetDateTimeOffset())
            .ShouldBe(own.GetProperty("starts").EnumerateArray().Select(s => s.GetDateTimeOffset()));
        times[0].GetProperty("localTime").GetString().ShouldBe("10:30");
        var open = own.GetProperty("open").EnumerateArray().ToList();
        open.ShouldNotBeEmpty();
        open[0].GetProperty("start").GetDateTimeOffset().ShouldBe(At(Target, 10, 2), "today, from now on");
        open.ShouldAllBe(o => o.GetProperty("end").GetDateTimeOffset() > o.GetProperty("start").GetDateTimeOffset());
        options.GetProperty("professionals")[1].GetProperty("freeNow").GetBoolean().ShouldBeTrue("Omar is free now, so Faisal may book him");
        var omars = (await OkAsync(omar.GetAsync($"/api/v1/shop/availability/walk-in?serviceId={w.Haircut}", ct), ct)).GetProperty("professionals");
        omars.EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ShouldBe([w.Omar, w.Faisal]);
        omars[0].GetProperty("freeNow").GetBoolean().ShouldBeTrue();

        // A service that cannot be booked online is still offered at the shop, to the barbers who do it.
        var deskOnly = (await OkAsync(owner.PostAsync("/api/v1/shop/services", new { nameAr = "حلاقة سريعة", price = 20m, durationMinutes = 15, onlineBookable = false }, ct), ct, HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        using var admin = await IdentityTestData.SignInNewStaffAsync(w.Factory, Trimme.Modules.Identity.Domain.SystemRoles.SuperAdmin, ct); // after the clock jump
        await OkAsync(admin.PutAsync($"/api/v1/admin/professionals/{w.Omar}/services", new { serviceIds = new[] { w.Haircut, w.Beard, deskOnly } }, ct), ct);
        var desk = await OkAsync(omar.GetAsync($"/api/v1/shop/availability/walk-in?serviceId={deskOnly}&date={Iso(Target.AddDays(1))}", ct), ct);
        desk.GetProperty("professionals").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ShouldBe([w.Omar]);
        desk.GetProperty("professionals")[0].GetProperty("freeNow").GetBoolean().ShouldBeFalse("not today");
        desk.GetProperty("professionals")[0].GetProperty("starts").GetArrayLength().ShouldBeGreaterThan(0);
        // Another day: the whole working day, opening before or at its first free start.
        var deskOpen = desk.GetProperty("professionals")[0].GetProperty("open")[0].GetProperty("start").GetDateTimeOffset();
        deskOpen.ShouldBeLessThanOrEqualTo(desk.GetProperty("professionals")[0].GetProperty("times")[0].GetProperty("startsAt").GetDateTimeOffset());
        deskOpen.ShouldBeGreaterThan(At(Target, 23, 0));
        (await OkAsync(faisal.GetAsync($"/api/v1/shop/availability/walk-in?serviceId={deskOnly}&date={Iso(Target.AddDays(1))}", ct), ct))
            .GetProperty("professionals").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ShouldBe([w.Omar], "Faisal does not do it, Omar does");

        await FailsAsync(faisal.GetAsync($"/api/v1/shop/availability/walk-in?serviceId={w.Haircut}&date={Iso(Target.AddDays(-1))}", ct), HttpStatusCode.BadRequest, "validation.out_of_range", ct);
        await FailsAsync(faisal.GetAsync($"/api/v1/shop/availability/walk-in?serviceId={w.ServiceB}", ct), HttpStatusCode.NotFound, null, ct);
        await FailsAsync(faisal.GetAsync("/api/v1/shop/availability/walk-in", ct), HttpStatusCode.NotFound, null, ct);
    }
}
