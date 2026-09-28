using Shouldly;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.Modules.Availability.Domain;
using Trimme.Modules.Availability.Domain.Engine;

namespace Trimme.UnitTests.Availability;

/// <summary>Schedule rules (D-082): weekly hours, breaks, closures and time off, and the instant-set arithmetic.</summary>
public sealed class ScheduleDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 4);
    private static readonly ShopId Shop = new(Guid.CreateVersion7());
    private static readonly ProfessionalId Pro = new(Guid.CreateVersion7());

    private static string? FieldError(Trimme.BuildingBlocks.Domain.Results.Error? error) =>
        error?.FieldErrors?.SelectMany(f => f.Value.Select(code => $"{f.Key}:{code}")).Single();

    [Theory]
    [InlineData(540, 1380, null)]
    [InlineData(1260, 1560, null)] // 21:00–02:00, past midnight
    [InlineData(0, 1440, null)] // the whole day
    [InlineData(540, 540, "intervals:validation.hours_invalid")]
    [InlineData(540, 2000, "intervals:validation.hours_invalid")] // more than 24 hours
    [InlineData(1440, 1500, "intervals:validation.hours_invalid")] // starts on the next day
    [InlineData(542, 600, "intervals:validation.hours_invalid")] // not on a 5-minute step
    public void WeeklyIntervals_AreOnFiveMinuteSteps_AndAtMost24Hours(int start, int end, string? error) =>
        FieldError(ScheduleRules.ValidateWeek([new WeeklyInterval(DayOfWeek.Sunday, start, end)])).ShouldBe(error);

    [Fact]
    public void WeeklyIntervals_MayNotOverlap_IncludingThePartAfterMidnight()
    {
        ScheduleRules.ValidateWeek([new(DayOfWeek.Sunday, 540, 780), new(DayOfWeek.Sunday, 840, 1380)]).ShouldBeNull("a split shift");
        FieldError(ScheduleRules.ValidateWeek([new(DayOfWeek.Sunday, 540, 780), new(DayOfWeek.Sunday, 720, 900)])).ShouldBe("intervals:validation.hours_overlap");
        FieldError(ScheduleRules.ValidateWeek([new(DayOfWeek.Thursday, 1260, 1560), new(DayOfWeek.Friday, 60, 600)]))
            .ShouldBe("intervals:validation.hours_overlap", "Thursday runs to 02:00 on Friday");
        ScheduleRules.ValidateWeek([new(DayOfWeek.Thursday, 1260, 1560), new(DayOfWeek.Friday, 120, 600)]).ShouldBeNull("back to back");
        FieldError(ScheduleRules.ValidateWeek([new(DayOfWeek.Saturday, 1320, 1560), new(DayOfWeek.Sunday, 0, 600)]))
            .ShouldBe("intervals:validation.hours_overlap", "Saturday night runs into Sunday of the next week");
    }

    [Fact]
    public void OpeningHours_ReplaceTheWholeWeek_Sorted()
    {
        var hours = ShopOpeningHours.Create(EntityId.New<OpeningHoursId>(), Shop, [new(DayOfWeek.Monday, 540, 600), new(DayOfWeek.Sunday, 540, 600)], Now).Value;
        hours.Week().Select(i => i.Day).ShouldBe([DayOfWeek.Sunday, DayOfWeek.Monday]);

        hours.Replace([new(DayOfWeek.Sunday, 540, 530)], Now).IsFailure.ShouldBeTrue();
        hours.Week().Count.ShouldBe(2, "a refused edit changes nothing");
    }

    [Fact]
    public void WorkingHours_FollowTheShop_OrUseTheirOwnWeek()
    {
        var hours = ProfessionalWorkingHours.Create(EntityId.New<WorkingHoursId>(), Shop, Pro, followsShopHours: true, [new(DayOfWeek.Sunday, 1, 2)], Now).Value;
        hours.Week().ShouldBeNull("the professional's invalid intervals are ignored while they follow the shop");

        hours.Replace(false, [new(DayOfWeek.Sunday, 720, 1320)], Now).IsSuccess.ShouldBeTrue();
        hours.Week()!.Single().StartMinute.ShouldBe(720);
        hours.Replace(false, [new(DayOfWeek.Sunday, 1, 2)], Now).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Breaks_AreWeeklyOrOnce_WithinADay()
    {
        ScheduleBreak Create(DayOfWeek[] days, DateOnly? date, int start = 930, int end = 960, string label = "صلاة العصر") =>
            ScheduleBreak.Create(EntityId.New<ScheduleBreakId>(), Shop, null, label, days, date, start, end, Today, Now) is { IsSuccess: true } ok
                ? ok.Value
                : throw new ShouldAssertException("expected success");

        Create([DayOfWeek.Friday, DayOfWeek.Friday, DayOfWeek.Monday], null).Weekdays.ShouldBe([DayOfWeek.Monday, DayOfWeek.Friday]);
        Create([], Today).ToRule().AppliesOn(Today).ShouldBeTrue();

        string? Error(DayOfWeek[] days, DateOnly? date, int start = 930, int end = 960, string label = "x") =>
            FieldError(ScheduleBreak.Create(EntityId.New<ScheduleBreakId>(), Shop, null, label, days, date, start, end, Today, Now).Error);

        Error([], null).ShouldBe("weekdays:validation.break_days");
        Error([DayOfWeek.Monday], Today).ShouldBe("weekdays:validation.break_days", "weekly or once, not both");
        Error([], Today.AddDays(-1)).ShouldBe("date:validation.date_in_past");
        Error([DayOfWeek.Monday], null, 1400, 1450).ShouldBe("endMinute:validation.hours_invalid", "a break stays within its day");
        Error([DayOfWeek.Monday], null, label: " ").ShouldBe("label:validation.required");
        Error([DayOfWeek.Monday], null, label: new string('x', 61)).ShouldBe("label:validation.too_long");
    }

    [Fact]
    public void Closures_AreInclusiveRanges_NotInThePast()
    {
        var closure = ShopClosure.Create(EntityId.New<ShopClosureId>(), Shop, Today, Today.AddDays(2), " اليوم الوطني ", Today, Now).Value;
        closure.Reason.ShouldBe("اليوم الوطني");
        closure.Range().Contains(Today.AddDays(2)).ShouldBeTrue();
        closure.UpdatedAt.ShouldBeNull();

        FieldError(closure.Update(Today.AddDays(2), Today, null, Today, Now).Error).ShouldBe("endDate:validation.range_invalid");
        FieldError(closure.Update(Today.AddDays(-3), Today.AddDays(-1), null, Today, Now).Error).ShouldBe("endDate:validation.date_in_past");
        FieldError(closure.Update(Today, Today.AddDays(400), null, Today, Now).Error).ShouldBe("endDate:validation.range_invalid");
        closure.Update(Today, Today, null, Today, Now).IsSuccess.ShouldBeTrue();
        closure.UpdatedAt.ShouldBe(Now);
    }

    [Fact]
    public void TimeOff_IsAnInstantRange_ThatMustEndInTheFuture()
    {
        var span = new InstantRange(Now.AddHours(-2), Now.AddDays(3));
        var entry = ProfessionalTimeOff.Create(EntityId.New<TimeOffId>(), Shop, Pro, TimeOffKind.Vacation, span, allDay: true, null, Now).Value;
        entry.Span().ShouldBe(span, "time off already running can be recorded");

        FieldError(entry.Update(TimeOffKind.Sick, new InstantRange(Now.AddDays(-3), Now.AddDays(-1)), false, null, Now).Error).ShouldBe("endDate:validation.date_in_past");
        FieldError(entry.Update(TimeOffKind.Sick, new InstantRange(Now.AddDays(1), Now), false, null, Now).Error).ShouldBe("endDate:validation.range_invalid");
        FieldError(entry.Update((TimeOffKind)42, span, false, null, Now).Error).ShouldBe("kind:validation.invalid");
    }

    [Fact]
    public void InstantSets_MergeIntersectAndSubtract()
    {
        var t = new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero);
        InstantRange R(int from, int to) => new(t.AddHours(from), t.AddHours(to));

        var merged = InstantSet.From([R(5, 6), R(1, 3), R(3, 4), R(2, 2)]);
        merged.Ranges.ShouldBe([R(1, 4), R(5, 6)], "adjacent ranges merge; empty ones vanish");

        merged.Intersect(InstantSet.From([R(0, 2), R(3, 5)])).Ranges.ShouldBe([R(1, 2), R(3, 4)]);
        InstantSet.From([R(0, 10)]).Subtract(InstantSet.From([R(2, 3), R(5, 7), R(9, 12)])).Ranges.ShouldBe([R(0, 2), R(3, 5), R(7, 9)]);

        merged.Contains(R(1, 4)).ShouldBeTrue();
        merged.Contains(R(3, 5)).ShouldBeFalse("it crosses a gap");
        merged.Contains(R(5, 6)).ShouldBeTrue();
    }

    [Fact]
    public void TheClock_ResolvesLocalTimes_InTheShopsZone()
    {
        var riyadh = new ShopClock(TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh"));
        riyadh.At(Today, 540).ShouldBe(new DateTimeOffset(2026, 10, 4, 6, 0, 0, TimeSpan.Zero));
        riyadh.At(Today, 1500).ShouldBe(new DateTimeOffset(2026, 10, 4, 22, 0, 0, TimeSpan.Zero), "01:00 the next day");
        riyadh.Date(new DateTimeOffset(2026, 10, 4, 21, 0, 0, TimeSpan.Zero)).ShouldBe(Today.AddDays(1));

        var newYork = new ShopClock(TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));
        newYork.At(new DateOnly(2026, 3, 8), 150).ShouldBe(new DateTimeOffset(2026, 3, 8, 3, 0, 0, TimeSpan.FromHours(-4)), "02:30 does not exist: the gap's end");
        newYork.At(new DateOnly(2026, 11, 1), 90).ShouldBe(new DateTimeOffset(2026, 11, 1, 1, 30, 0, TimeSpan.FromHours(-4)), "the first 01:30");
    }
}
