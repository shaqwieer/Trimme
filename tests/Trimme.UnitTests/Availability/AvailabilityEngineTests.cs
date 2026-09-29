using Shouldly;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.Modules.Availability.Domain.Engine;

namespace Trimme.UnitTests.Availability;

/// <summary>
/// R-AVL-01/02 (D-082): the availability engine combines opening hours, closures, working hours, breaks, time off,
/// bookings, duration, lead time, horizon and slot step, and returns only bookable starts. Riyadh has no daylight
/// saving; New York proves the spring gap and the autumn repeat are handled on real instants.
/// </summary>
public sealed class AvailabilityEngineTests
{
    private static readonly TimeZoneInfo Riyadh = TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private static readonly ProfessionalId A = new(Guid.Parse("00000000-0000-0000-0000-00000000000a"));
    private static readonly ProfessionalId B = new(Guid.Parse("00000000-0000-0000-0000-00000000000b"));

    /// <summary>Sunday 4 October 2026 (Riyadh); Monday is the 5th, Thursday the 8th.</summary>
    private static readonly DateOnly Sunday = new(2026, 10, 4);

    /// <summary>Riyadh midnight at the start of <see cref="Sunday"/>.</summary>
    private static readonly DateTimeOffset SundayMidnight = Local(Sunday, 0, 0);

    private static DateTimeOffset Local(DateOnly date, int hour, int minute) =>
        new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.FromHours(3)).AddHours(hour).AddMinutes(minute);

    private static int M(int hour, int minute = 0) => (hour * 60) + minute;

    private static BookingPolicy Policy(int step = 5, int lead = 0, int horizon = 30) => new(lead, horizon, step);

    private static ShopCalendar Shop(params WeeklyInterval[] hours) => new(Riyadh, hours, []);

    private static ProfessionalCalendar Pro(ProfessionalId id, WeeklyInterval[]? hours = null, BreakRule[]? breaks = null, InstantRange[]? blocked = null) =>
        new(id, hours, breaks ?? [], blocked ?? []);

    private static WeeklyInterval Day(DayOfWeek day, int start, int end) => new(day, start, end);

    private static IReadOnlyList<AvailableSlot> Find(
        ShopCalendar shop, ProfessionalCalendar[] pros, DateOnly from, DateOnly to, int duration, BookingPolicy? policy = null, DateTimeOffset? now = null) =>
        AvailabilityEngine.FindSlots(shop, pros, new AvailabilityQuery(from, to, duration, now ?? SundayMidnight, policy ?? Policy()));

    private static string[] Times(IEnumerable<AvailableSlot> slots) => [.. slots.Select(s => $"{s.Date:MM-dd} {s.LocalTime:HH:mm}")];

    [Fact]
    public void Slots_AreTheIntersection_OfOpeningHoursAndWorkingHours()
    {
        var shop = Shop(Day(DayOfWeek.Sunday, M(9), M(12)));
        var slots = Find(shop, [Pro(A, [Day(DayOfWeek.Sunday, M(10), M(14))])], Sunday, Sunday, 30, Policy(step: 30));

        Times(slots).ShouldBe(["10-04 10:00", "10-04 10:30", "10-04 11:00", "10-04 11:30"]);
        slots[0].StartsAt.ShouldBe(Local(Sunday, 10, 0));
        slots[0].EndsAt.ShouldBe(Local(Sunday, 10, 30));
    }

    [Fact]
    public void AProfessionalWithoutOwnHours_FollowsTheShop()
    {
        var slots = Find(Shop(Day(DayOfWeek.Sunday, M(9), M(11))), [Pro(A)], Sunday, Sunday, 60, Policy(step: 30));

        Times(slots).ShouldBe(["10-04 09:00", "10-04 09:30", "10-04 10:00"]);
    }

    [Theory]
    [InlineData(5, new[] { "09:00", "09:05", "09:10", "09:15", "09:20", "09:25", "09:30" })]
    [InlineData(10, new[] { "09:00", "09:10", "09:20", "09:30" })]
    [InlineData(15, new[] { "09:00", "09:15", "09:30" })]
    public void SlotStep_SetsTheGrid(int step, string[] expected)
    {
        var slots = Find(Shop(Day(DayOfWeek.Sunday, M(9), M(10))), [Pro(A)], Sunday, Sunday, 30, Policy(step));

        slots.Select(s => s.LocalTime.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)).ShouldBe(expected);
    }

    [Fact]
    public void TheGrid_CountsFromLocalMidnight_SoAShopOpeningAt0805_OffersFiveMinuteStarts()
    {
        var shop = Shop(Day(DayOfWeek.Sunday, M(8, 5), M(9)));

        Times(Find(shop, [Pro(A)], Sunday, Sunday, 20, Policy(step: 5)))
            .ShouldBe(["10-04 08:05", "10-04 08:10", "10-04 08:15", "10-04 08:20", "10-04 08:25", "10-04 08:30", "10-04 08:35", "10-04 08:40"]);
        Times(Find(shop, [Pro(A)], Sunday, Sunday, 20, Policy(step: 15))).ShouldBe(["10-04 08:15", "10-04 08:30"], "08:05 is not on a 15-minute grid");
    }

    [Fact]
    public void Breaks_AreRemoved_AndAnItemMayNotRunIntoOne()
    {
        var shop = Shop(Day(DayOfWeek.Sunday, M(9), M(12)));
        var asr = new BreakRule([DayOfWeek.Sunday], null, M(10), M(10, 30));

        Times(Find(shop, [Pro(A, breaks: [asr])], Sunday, Sunday, 45, Policy(step: 15)))
            .ShouldBe(["10-04 09:00", "10-04 09:15", "10-04 10:30", "10-04 10:45", "10-04 11:00", "10-04 11:15"]);
    }

    [Fact]
    public void AOneOffBreak_AppliesOnlyOnItsDate()
    {
        var shop = Shop(Day(DayOfWeek.Sunday, M(9), M(10)), Day(DayOfWeek.Monday, M(9), M(10)));
        var once = new BreakRule([], Sunday.AddDays(1), M(9), M(9, 30));

        Times(Find(shop, [Pro(A, breaks: [once])], Sunday, Sunday.AddDays(1), 30, Policy(step: 30)))
            .ShouldBe(["10-04 09:00", "10-04 09:30", "10-05 09:30"]);
    }

    [Fact]
    public void TimeOff_SpanningDays_RemovesOnlyItsHours()
    {
        var shop = Shop(Day(DayOfWeek.Sunday, M(9), M(12)), Day(DayOfWeek.Monday, M(9), M(12)), Day(DayOfWeek.Tuesday, M(9), M(12)));
        var leave = new InstantRange(Local(Sunday, 11, 0), Local(Sunday.AddDays(2), 10, 0));

        Times(Find(shop, [Pro(A, blocked: [leave])], Sunday, Sunday.AddDays(2), 60, Policy(step: 60)))
            .ShouldBe(["10-04 09:00", "10-04 10:00", "10-06 10:00", "10-06 11:00"]);
    }

    [Fact]
    public void ExistingBookings_BlockOverlaps_ButNotBackToBackStarts()
    {
        var shop = Shop(Day(DayOfWeek.Sunday, M(9), M(12)));
        var booked = new InstantRange(Local(Sunday, 10, 0), Local(Sunday, 10, 30));

        Times(Find(shop, [Pro(A, blocked: [booked])], Sunday, Sunday, 30, Policy(step: 30)))
            .ShouldBe(["10-04 09:00", "10-04 09:30", "10-04 10:30", "10-04 11:00", "10-04 11:30"]);
    }

    [Fact]
    public void AClosure_ClosesTheWholeDay()
    {
        var shop = new ShopCalendar(Riyadh, [Day(DayOfWeek.Sunday, M(9), M(10)), Day(DayOfWeek.Monday, M(9), M(10))], [new DateRange(Sunday.AddDays(1), Sunday.AddDays(1))]);

        Times(Find(shop, [Pro(A)], Sunday, Sunday.AddDays(1), 60, Policy(step: 60))).ShouldBe(["10-04 09:00"]);
    }

    [Fact]
    public void AWindowPastMidnight_BelongsToItsOpeningDay_ButItsSlotsAreDatedByTheirStart()
    {
        var thursday = Sunday.AddDays(4);
        var friday = thursday.AddDays(1);
        var late = Day(DayOfWeek.Thursday, M(21), M(26)); // 21:00–02:00

        var open = Find(Shop(late), [Pro(A)], thursday, friday, 60, Policy(step: 60));
        Times(open).ShouldBe(["10-08 21:00", "10-08 22:00", "10-08 23:00", "10-09 00:00", "10-09 01:00"]);
        open[^1].Period.ShouldBe(DayPeriod.Evening, "the hours after midnight are evening");
        open[^1].EndsAt.ShouldBe(Local(friday, 2, 0));

        // An item may run across midnight inside the window.
        Times(Find(Shop(late), [Pro(A)], thursday, thursday, 60, Policy(step: 30))).ShouldContain("10-08 23:30");

        // Closing Friday leaves Thursday night's window; closing Thursday removes it, including its hours after midnight.
        var fridayClosed = new ShopCalendar(Riyadh, [late], [new DateRange(friday, friday)]);
        Times(Find(fridayClosed, [Pro(A)], friday, friday, 60, Policy(step: 60))).ShouldBe(["10-09 00:00", "10-09 01:00"]);
        var thursdayClosed = new ShopCalendar(Riyadh, [late], [new DateRange(thursday, thursday)]);
        Find(thursdayClosed, [Pro(A)], thursday, friday, 60, Policy(step: 60)).ShouldBeEmpty();
    }

    [Fact]
    public void AnItemLongerThanTheRemainingWindow_HasNoSlot()
    {
        var shop = Shop(Day(DayOfWeek.Sunday, M(9), M(10)));

        Find(shop, [Pro(A)], Sunday, Sunday, 90).ShouldBeEmpty();
        Times(Find(shop, [Pro(A)], Sunday, Sunday, 60)).ShouldBe(["10-04 09:00"]);
    }

    [Fact]
    public void PastTimes_AndTheLeadTime_AreExcluded_ExactlyAtTheBoundary()
    {
        var shop = Shop(Day(DayOfWeek.Sunday, M(9), M(13)));
        var now = Local(Sunday, 10, 2);

        Find(shop, [Pro(A)], Sunday, Sunday, 30, Policy(step: 5, lead: 0), now)[0].LocalTime.ShouldBe(new TimeOnly(10, 5), "10:00 has passed");
        Find(shop, [Pro(A)], Sunday, Sunday, 30, Policy(step: 5, lead: 60), now)[0].LocalTime.ShouldBe(new TimeOnly(11, 5), "11:02 is the earliest start");

        // A start exactly at now + lead is allowed.
        var exact = Local(Sunday, 10, 0);
        Find(shop, [Pro(A)], Sunday, Sunday, 30, Policy(step: 30, lead: 60), exact)[0].StartsAt.ShouldBe(Local(Sunday, 11, 0));
    }

    [Fact]
    public void TheHorizon_EndsOnTheLastBookableDate_Inclusive()
    {
        var everyDay = Enum.GetValues<DayOfWeek>().Select(d => Day(d, M(9), M(10))).ToArray();
        var policy = Policy(step: 60, horizon: 3);

        AvailabilityEngine.BookableDates(Riyadh, SundayMidnight, policy).ShouldBe((Sunday, Sunday.AddDays(2)));
        Times(Find(Shop(everyDay), [Pro(A)], Sunday, Sunday.AddDays(6), 60, policy)).ShouldBe(["10-04 09:00", "10-05 09:00", "10-06 09:00"]);
        Find(Shop(everyDay), [Pro(A)], Sunday.AddDays(3), Sunday.AddDays(3), 60, policy).ShouldBeEmpty();
        Find(Shop(everyDay), [Pro(A)], Sunday.AddDays(-1), Sunday.AddDays(-1), 60, policy).ShouldBeEmpty("yesterday");
    }

    [Fact]
    public void AnyProfessional_CarriesEveryFreeCandidate_AndDropsASlotNobodyCanTake()
    {
        var shop = Shop(Day(DayOfWeek.Sunday, M(9), M(11)));
        var aBusy = new InstantRange(Local(Sunday, 10, 0), Local(Sunday, 11, 0));
        var bBusy = new InstantRange(Local(Sunday, 9, 0), Local(Sunday, 10, 0));

        var slots = Find(shop, [Pro(A, blocked: [aBusy]), Pro(B)], Sunday, Sunday, 60, Policy(step: 60));
        slots.Select(s => (s.LocalTime.Hour, string.Join(",", s.Professionals.Select(p => p == A ? "A" : "B")))).ShouldBe([(9, "A,B"), (10, "B")]);

        Find(shop, [Pro(A, blocked: [aBusy]), Pro(B, blocked: [bBusy])], Sunday, Sunday, 60, Policy(step: 60))
            .Select(s => s.LocalTime.Hour).ShouldBe([9, 10], "each hour has one free professional");
        Find(shop, [Pro(A, blocked: [aBusy, bBusy]), Pro(B, blocked: [aBusy, bBusy])], Sunday, Sunday, 60, Policy(step: 60)).ShouldBeEmpty();
    }

    [Fact]
    public void SlotsAreGroupedIntoPeriods_ByLocalHour()
    {
        AvailabilityEngine.PeriodOf(4).ShouldBe(DayPeriod.Evening);
        AvailabilityEngine.PeriodOf(5).ShouldBe(DayPeriod.Morning);
        AvailabilityEngine.PeriodOf(11).ShouldBe(DayPeriod.Morning);
        AvailabilityEngine.PeriodOf(12).ShouldBe(DayPeriod.Afternoon);
        AvailabilityEngine.PeriodOf(16).ShouldBe(DayPeriod.Afternoon);
        AvailabilityEngine.PeriodOf(17).ShouldBe(DayPeriod.Evening);
    }

    [Fact]
    public void IsBookable_UsesTheSameRules_ForOneStart()
    {
        var shop = Shop(Day(DayOfWeek.Sunday, M(9), M(12)));
        var pro = Pro(A, breaks: [new BreakRule([DayOfWeek.Sunday], null, M(10), M(10, 30))]);
        var policy = Policy(step: 5);

        AvailabilityEngine.IsBookable(shop, pro, Local(Sunday, 9, 0), 30, SundayMidnight, policy).ShouldBeTrue();
        AvailabilityEngine.IsBookable(shop, pro, Local(Sunday, 9, 2), 30, SundayMidnight, policy).ShouldBeFalse("off the 5-minute grid");
        AvailabilityEngine.IsBookable(shop, pro, Local(Sunday, 9, 45), 30, SundayMidnight, policy).ShouldBeFalse("runs into the break");
        AvailabilityEngine.IsBookable(shop, pro, Local(Sunday, 11, 45), 30, SundayMidnight, policy).ShouldBeFalse("runs past closing");
    }

    [Fact]
    public void Riyadh_HasNoDaylightSaving_EveryDayIs24Hours()
    {
        var everyHour = Shop(Day(DayOfWeek.Sunday, 0, M(24)));

        Find(everyHour, [Pro(A)], Sunday, Sunday, 60, Policy(step: 60)).Count.ShouldBe(24);
    }

    [Fact]
    public void DaylightSaving_SpringForward_SkipsTheMissingHour_OnRealInstants()
    {
        // New York, Sunday 8 March 2026: 02:00 EST jumps to 03:00 EDT.
        var day = new DateOnly(2026, 3, 8);
        var shop = new ShopCalendar(NewYork, [Day(DayOfWeek.Sunday, 0, M(6))], []);
        var now = new DateTimeOffset(2026, 3, 7, 0, 0, 0, TimeSpan.Zero);

        var slots = AvailabilityEngine.FindSlots(shop, [Pro(A)], new AvailabilityQuery(day, day, 60, now, Policy(step: 60)));
        slots.Select(s => s.LocalTime.Hour).ShouldBe([0, 1, 3, 4, 5], "02:00 does not exist that night");
        (slots[2].StartsAt - slots[1].StartsAt).ShouldBe(TimeSpan.FromHours(1));
        slots[^1].EndsAt.ShouldBe(new DateTimeOffset(2026, 3, 8, 6, 0, 0, TimeSpan.FromHours(-4)));

        // A 90-minute item from 01:00 lasts 90 real minutes (ends 03:30 on the clock).
        AvailabilityEngine.FindSlots(shop, [Pro(A)], new AvailabilityQuery(day, day, 90, now, Policy(step: 60)))
            .Single(s => s.LocalTime.Hour == 1).EndsAt.ShouldBe(new DateTimeOffset(2026, 3, 8, 3, 30, 0, TimeSpan.FromHours(-4)));
    }

    [Fact]
    public void DaylightSaving_FallBack_OffersTheRepeatedHourTwice_AsDistinctInstants()
    {
        // New York, Sunday 1 November 2026: 02:00 EDT falls back to 01:00 EST.
        var day = new DateOnly(2026, 11, 1);
        var shop = new ShopCalendar(NewYork, [Day(DayOfWeek.Sunday, 0, M(4))], []);
        var now = new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero);

        var slots = AvailabilityEngine.FindSlots(shop, [Pro(A)], new AvailabilityQuery(day, day, 60, now, Policy(step: 60)));
        slots.Select(s => s.LocalTime.Hour).ShouldBe([0, 1, 1, 2, 3], "00:00–04:00 on the clock is five real hours");
        slots.Select(s => s.StartsAt).ShouldBeUnique();
        slots[2].StartsAt.ShouldBe(new DateTimeOffset(2026, 11, 1, 1, 0, 0, TimeSpan.FromHours(-5)));
    }

    [Fact]
    public void InvalidInputs_GiveNoSlots()
    {
        var shop = Shop(Day(DayOfWeek.Sunday, M(9), M(10)));

        Find(shop, [], Sunday, Sunday, 30).ShouldBeEmpty("no professional");
        Find(shop, [Pro(A)], Sunday, Sunday, 0).ShouldBeEmpty("no duration");
        Find(shop, [Pro(A)], Sunday, Sunday, 30, Policy(step: 7)).ShouldBeEmpty("unsupported step");
        Find(shop, [Pro(A)], Sunday.AddDays(1), Sunday, 30).ShouldBeEmpty("reversed range");
    }

    [Fact]
    public void DayOf_GivesTheBusinessDaysWorkingTime_BreaksAndTimeOff_IncludingTheHoursAfterMidnight()
    {
        // Thursday 18:00 until 01:00 on Friday; a break on Friday 00:00–00:15 still belongs to Thursday's window.
        var thursday = Sunday.AddDays(4);
        var shop = Shop(Day(DayOfWeek.Thursday, M(18), M(25)));
        var pro = Pro(
            A,
            hours: [Day(DayOfWeek.Thursday, M(19), M(25))],
            breaks: [new BreakRule([DayOfWeek.Friday], null, 0, M(0, 15))],
            blocked: [new InstantRange(Local(thursday, 20, 0), Local(thursday, 21, 0))]);

        var day = AvailabilityEngine.DayOf(shop, pro, thursday);

        day.Working.Ranges.ShouldBe([new InstantRange(Local(thursday, 19, 0), Local(thursday.AddDays(1), 1, 0))]);
        day.Breaks.Ranges.ShouldBe([new InstantRange(Local(thursday.AddDays(1), 0, 0), Local(thursday.AddDays(1), 0, 15))]);
        day.Blocked.Ranges.ShouldBe([new InstantRange(Local(thursday, 20, 0), Local(thursday, 21, 0))]);
        AvailabilityEngine.OpenWindows(shop, thursday).Ranges.ShouldBe([new InstantRange(Local(thursday, 18, 0), Local(thursday.AddDays(1), 1, 0))]);

        // Friday's own day has no window, and a closure empties Thursday.
        AvailabilityEngine.DayOf(shop, pro, thursday.AddDays(1)).Working.Ranges.ShouldBeEmpty();
        var closed = new ShopCalendar(Riyadh, shop.OpeningHours, [new DateRange(thursday, thursday)]);
        AvailabilityEngine.DayOf(closed, pro, thursday).Working.Ranges.ShouldBeEmpty();
        AvailabilityEngine.OpenWindows(closed, thursday).Ranges.ShouldBeEmpty();
    }
}
