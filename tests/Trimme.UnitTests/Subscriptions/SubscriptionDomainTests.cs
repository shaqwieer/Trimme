using Shouldly;
using Trimme.BuildingBlocks.Application.Platform;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.Modules.Administration.Domain;
using Trimme.Modules.Subscriptions.Domain;

namespace Trimme.UnitTests.Subscriptions;

/// <summary>
/// R-SUB-01..04: period arithmetic without month-end drift, the status calculator at its boundaries, versioned plan
/// prices, and the subscription rules (no overlap, no future gap, overrides on the period in force).
/// </summary>
public sealed class SubscriptionDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 27);
    private static readonly ShopId Shop = new(Guid.CreateVersion7());

    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    private static DateOnly P(string iso) => DateOnly.ParseExact(iso, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static PlanDetails Details(BillingIntervalUnit unit = BillingIntervalUnit.Month, int count = 12) =>
        new("سنوي", "Annual", null, null, [PlanFeature.Of("ميزة", "Feature")], 6, null, unit, count, null, 7, AvailableToNewShops: true);

    private static SubscriptionPlan NewPlan(BillingIntervalUnit unit = BillingIntervalUnit.Month, int count = 12, params (decimal Amount, DateOnly From)[] prices)
    {
        var plan = SubscriptionPlan.Create(new SubscriptionPlanId(Guid.CreateVersion7()), Details(unit, count), 1, Now).Value;
        foreach (var (amount, from) in prices)
        {
            plan.AddPrice(new PlanPriceId(Guid.CreateVersion7()), amount, "SAR", "SAR", from, from, null, Now).IsSuccess.ShouldBeTrue();
        }

        return plan;
    }

    private static PriceSnapshot Snapshot(SubscriptionPlan plan, DateOnly start) =>
        plan.PriceOn(start) is { } p ? new PriceSnapshot(p.Id, p.Amount, p.Currency) : throw new InvalidOperationException("no price");

    private static ShopSubscription Assign(SubscriptionPlan plan, DateOnly start, DateOnly? end = null) =>
        ShopSubscription.Assign(new ShopSubscriptionId(Guid.CreateVersion7()), Shop, plan, start, end ?? plan.PeriodEnd(start), Snapshot(plan, start), null, null, Today, Now).Value;

    // ------------------------------------------------------------------ period arithmetic

    [Theory]
    [InlineData("2026-10-01", 12, "2027-09-30")] // design a-subs: renewed 1 Oct 2026 for a year → until 30 Sep 2027
    [InlineData("2026-01-15", 1, "2026-02-14")]
    [InlineData("2026-01-31", 1, "2026-02-28")] // no 31 Feb: the period runs to the month end, not the 27th
    [InlineData("2026-01-30", 1, "2026-02-28")]
    [InlineData("2026-03-31", 1, "2026-04-30")]
    [InlineData("2028-02-29", 12, "2029-02-28")] // leap day + a year
    [InlineData("2026-12-31", 1, "2027-01-30")]
    [InlineData("2026-08-31", 6, "2027-02-28")]
    public void MonthPeriods_EndTheDayBeforeTheSameDate_OrAtTheMonthEndWhenItDoesNotExist(string start, int months, string end) =>
        SubscriptionDates.End(P(start), BillingIntervalUnit.Month, months).ShouldBe(P(end));

    [Theory]
    [InlineData(1, "2026-09-27")]
    [InlineData(30, "2026-10-26")]
    [InlineData(365, "2027-09-26")]
    public void DayPeriods_CountTheStartDay(int days, string end)
    {
        SubscriptionDates.End(Today, BillingIntervalUnit.Day, days).ShouldBe(P(end));
        SubscriptionDates.EndAfterDays(Today, days).ShouldBe(P(end));
    }

    [Fact]
    public void MonthlyRenewalChain_FromAMonthEnd_NeverDriftsOrLosesADay()
    {
        var start = D(2026, 1, 31);
        var ends = new List<DateOnly>();
        for (var i = 0; i < 14; i++)
        {
            var end = SubscriptionDates.End(start, BillingIntervalUnit.Month, 1);
            (end.DayNumber - start.DayNumber + 1).ShouldBeInRange(28, 31);
            ends.Add(end);
            start = end.AddDays(1);
        }

        ends[0].ShouldBe(D(2026, 2, 28));
        ends[1].ShouldBe(D(2026, 3, 31));
        ends.Skip(1).ShouldAllBe(e => e.AddDays(1).Day == 1, "after the first clamp every period is a calendar month");
    }

    // ------------------------------------------------------------------ status calculator

    [Theory]
    [InlineData(20, 14, SubscriptionStatus.Active)]
    [InlineData(14, 14, SubscriptionStatus.Active)] // 15 days left
    [InlineData(13, 14, SubscriptionStatus.ExpiringSoon)] // 14 days left = threshold
    [InlineData(0, 14, SubscriptionStatus.ExpiringSoon)] // the last day is still covered
    [InlineData(-1, 14, SubscriptionStatus.Expired)]
    [InlineData(-400, 14, SubscriptionStatus.Expired)]
    [InlineData(29, 30, SubscriptionStatus.ExpiringSoon)] // the threshold is a setting
    public void Status_FollowsDaysRemaining_AndTheThreshold(int endOffset, int threshold, SubscriptionStatus expected) =>
        SubscriptionStatusCalculator.Calculate(false, Today.AddDays(endOffset), Today, threshold).ShouldBe(expected);

    [Fact]
    public void Status_SuspendedWins_AndNoSubscriptionIsNone()
    {
        SubscriptionStatusCalculator.Calculate(true, Today.AddDays(100), Today, 14).ShouldBe(SubscriptionStatus.Suspended);
        SubscriptionStatusCalculator.Calculate(true, Today.AddDays(-5), Today, 14).ShouldBe(SubscriptionStatus.Suspended);
        SubscriptionStatusCalculator.Calculate(false, null, Today, 14).ShouldBe(SubscriptionStatus.None);
    }

    [Fact]
    public void DaysRemaining_CountsTheEndDay_AndIsZeroOnceExpired()
    {
        SubscriptionStatusCalculator.DaysRemaining(Today, Today).ShouldBe(1);
        SubscriptionStatusCalculator.DaysRemaining(Today.AddDays(72), Today).ShouldBe(73);
        SubscriptionStatusCalculator.DaysRemaining(Today.AddDays(-3), Today).ShouldBe(0);
    }

    [Fact]
    public void PlatformCalendar_IsRiyadhTime()
    {
        var settings = PlatformSettings.CreateDefault(Now).ToSnapshot();
        settings.LocalDate(new DateTimeOffset(2026, 9, 27, 20, 59, 0, TimeSpan.Zero)).ShouldBe(D(2026, 9, 27));
        settings.LocalDate(new DateTimeOffset(2026, 9, 27, 21, 0, 0, TimeSpan.Zero)).ShouldBe(D(2026, 9, 28), "midnight in Riyadh is 21:00 UTC");
    }

    // ------------------------------------------------------------------ plans and prices

    [Fact]
    public void Prices_AreVersioned_AndTheVersionInForceDependsOnTheDate()
    {
        var plan = NewPlan(prices: [(1900m, D(2023, 1, 1)), (2400m, D(2025, 1, 1))]);
        plan.Prices.Select(p => p.VersionNumber).ShouldBe([1, 2]);
        plan.PriceOn(D(2022, 12, 31)).ShouldBeNull();
        plan.PriceOn(D(2024, 12, 31))!.Amount.ShouldBe(1900m);
        plan.PriceOn(D(2025, 1, 1))!.Amount.ShouldBe(2400m);
        plan.PriceOn(Today)!.VersionNumber.ShouldBe(2);
    }

    [Fact]
    public void AddPrice_NeverRewritesHistory()
    {
        var plan = NewPlan(prices: [(1900m, Today)]);
        Outcome Add(decimal amount, DateOnly from, string currency = "SAR") =>
            new(plan.AddPrice(new PlanPriceId(Guid.CreateVersion7()), amount, currency, "SAR", from, Today, null, Now));

        Add(2000m, Today.AddDays(-1)).Code.ShouldBe("validation.date_in_past");
        Add(2000m, Today).Code.ShouldBe("validation.price_date_taken");
        Add(10.005m, Today.AddDays(5)).Code.ShouldBe("validation.price_invalid");
        Add(1_000_000.01m, Today.AddDays(5)).Code.ShouldBe("validation.price_invalid");
        Add(2000m, Today.AddDays(5), "USD").Code.ShouldBe("validation.invalid");
        Add(2400m, Today.AddDays(30)).Code.ShouldBeNull();
        plan.Prices.Count.ShouldBe(2);
        plan.PriceOn(Today)!.Amount.ShouldBe(1900m, "a future version does not change today's price");

        plan.Archive(Now);
        Add(3000m, Today.AddDays(60)).Code.ShouldBe("plan.archived");
    }

    [Fact]
    public void Publish_NeedsAPrice_AndArchiveIsFinal()
    {
        var plan = NewPlan();
        plan.Publish(Now).Error!.Code.ShouldBe("plan.no_price");
        plan.AddPrice(new PlanPriceId(Guid.CreateVersion7()), 199m, "SAR", "SAR", Today.AddDays(3), Today, null, Now);
        plan.Publish(Now).IsSuccess.ShouldBeTrue("a scheduled price is enough to publish");
        plan.Deactivate(Now).IsSuccess.ShouldBeTrue();
        plan.Status.ShouldBe(PlanStatus.Inactive);
        plan.Archive(Now).IsSuccess.ShouldBeTrue();
        plan.AvailableToNewShops.ShouldBeFalse();
        plan.Update(Details(), Now).Error!.Code.ShouldBe("plan.archived");
        plan.Publish(Now).Error!.Code.ShouldBe("plan.archived");
    }

    [Theory]
    [InlineData(BillingIntervalUnit.Month, 0, false)]
    [InlineData(BillingIntervalUnit.Month, 36, true)]
    [InlineData(BillingIntervalUnit.Month, 37, false)]
    [InlineData(BillingIntervalUnit.Day, 1095, true)]
    [InlineData(BillingIntervalUnit.Day, 1096, false)]
    public void Interval_IsBounded(BillingIntervalUnit unit, int count, bool valid) =>
        SubscriptionPlan.Create(new SubscriptionPlanId(Guid.CreateVersion7()), Details(unit, count), 1, Now).IsSuccess.ShouldBe(valid);

    // ------------------------------------------------------------------ subscriptions

    [Fact]
    public void Assign_CannotStartInTheFuture_ButMayBeBackdated()
    {
        var plan = NewPlan(prices: [(1900m, D(2020, 1, 1))]);
        ShopSubscription.Assign(new ShopSubscriptionId(Guid.CreateVersion7()), Shop, plan, Today.AddDays(1), Today.AddDays(40), Snapshot(plan, Today), null, null, Today, Now)
            .Error!.FieldErrors!["startDate"].ShouldBe(["validation.date_in_future"]);
        var backdated = Assign(plan, Today.AddDays(-400));
        backdated.EndDate.ShouldBe(Today.AddDays(-400).AddMonths(12).AddDays(-1));
    }

    [Fact]
    public void Renew_ContinuesTheDayAfter_OrRestartsByTodayAfterALapse()
    {
        var plan = NewPlan(prices: [(1900m, D(2020, 1, 1))]);
        var active = Assign(plan, Today.AddDays(-10));
        active.NextRenewalStart(Today).ShouldBe(active.EndDate.AddDays(1));
        active.Renew(plan, active.EndDate, active.EndDate.AddDays(30), Snapshot(plan, Today), null, null, Today, Now).Error!.FieldErrors!["startDate"].ShouldBe(["validation.period_overlap"]);
        active.Renew(plan, active.EndDate.AddDays(2), active.EndDate.AddDays(30), Snapshot(plan, Today), null, null, Today, Now).Error!.FieldErrors!["startDate"].ShouldBe(["validation.period_gap"]);
        var next = active.EndDate.AddDays(1);
        active.Renew(plan, next, plan.PeriodEnd(next), Snapshot(plan, next), null, null, Today, Now).IsSuccess.ShouldBeTrue();
        active.Periods.Count.ShouldBe(2);
        active.PeriodInForce(Today).PeriodStart.ShouldBe(Today.AddDays(-10), "the renewal is in the future; today is still in the first period");

        var lapsed = Assign(plan, Today.AddDays(-100), Today.AddDays(-40));
        lapsed.NextRenewalStart(Today).ShouldBe(Today);
        lapsed.Renew(plan, Today.AddDays(-20), Today.AddDays(10), Snapshot(plan, Today), null, null, Today, Now).IsSuccess.ShouldBeTrue("a past gap is allowed");
        lapsed.EndDate.ShouldBe(Today.AddDays(10));
    }

    [Fact]
    public void Override_AppliesToThePeriodInForce_AndKeepsThePreviousValues()
    {
        var plan = NewPlan(prices: [(1900m, D(2020, 1, 1))]);
        var subscription = Assign(plan, Today.AddDays(-10));
        var firstEnd = subscription.EndDate;
        var next = firstEnd.AddDays(1);
        subscription.Renew(plan, next, plan.PeriodEnd(next), Snapshot(plan, next), null, null, Today, Now);

        subscription.Override(new SubscriptionOverrideId(Guid.CreateVersion7()), null, firstEnd.AddDays(10), "Goodwill", null, Today, Now)
            .Error!.FieldErrors!["endDate"].ShouldBe(["validation.period_not_latest"]);
        subscription.Override(new SubscriptionOverrideId(Guid.CreateVersion7()), null, null, "Nothing", null, Today, Now).IsFailure.ShouldBeTrue();

        var record = subscription.Override(new SubscriptionOverrideId(Guid.CreateVersion7()), 1500m, null, "Discount", null, Today, Now).Value;
        record.PreviousAmount.ShouldBe(1900m);
        record.NewAmount.ShouldBe(1500m);
        subscription.PeriodInForce(Today).Amount.ShouldBe(1500m);
        subscription.PeriodInForce(Today).IsOverridden.ShouldBeTrue();
        subscription.LatestPeriod.Amount.ShouldBe(1900m, "the renewal keeps its own snapshot");
    }

    [Fact]
    public void Override_OfTheLatestEnd_MovesTheCoverage()
    {
        var plan = NewPlan(prices: [(199m, D(2020, 1, 1))]);
        var subscription = Assign(plan, Today.AddDays(-10), Today.AddDays(5));
        subscription.Override(new SubscriptionOverrideId(Guid.CreateVersion7()), null, Today.AddDays(-11), "Too early", null, Today, Now).IsFailure.ShouldBeTrue();
        var record = subscription.Override(new SubscriptionOverrideId(Guid.CreateVersion7()), null, Today.AddDays(35), "Extension", null, Today, Now).Value;
        record.PreviousEnd.ShouldBe(Today.AddDays(5));
        subscription.EndDate.ShouldBe(Today.AddDays(35));

        var coverage = new SubscriptionCoverage(Shop);
        coverage.CopyFrom(subscription);
        coverage.EndDate.ShouldBe(Today.AddDays(35));
        subscription.Suspend("Review", Now);
        coverage.CopyFrom(subscription);
        coverage.IsSuspended.ShouldBeTrue();
    }

    // ------------------------------------------------------------------ settings

    [Fact]
    public void Settings_Defaults_MatchTheSpec_AndChangesNameTheFields()
    {
        var settings = PlatformSettings.CreateDefault(Now);
        var snapshot = settings.ToSnapshot();
        snapshot.ShouldBe(new PlatformSettingsSnapshot(
            60, 30, 5, 120, 7, 30, 14, SubscriptionEnforcement.HideAndBlockNewOnlineBookings, true, "ar", "SAR", "Asia/Riyadh", "SA", 24.7136, 46.6753, 11));

        var changed = settings.Update(settings.Editable with { ReminderOffsetMinutes = 45, MapDefaultZoom = 12 }, null, Now);
        changed.ShouldBe(["reminderOffsetMinutes", "mapDefaultZoom"]);
        settings.Update(settings.Editable, null, Now).ShouldBeEmpty();
    }

    private readonly record struct Outcome(Trimme.BuildingBlocks.Domain.Results.Result<PlanPrice> Result)
    {
        public string? Code => Result.IsSuccess
            ? null
            : Result.Error!.FieldErrors?.Values.SelectMany(v => v).FirstOrDefault() ?? Result.Error.Code;
    }
}
