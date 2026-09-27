using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.Modules.Subscriptions.Domain;

public readonly record struct ShopSubscriptionId(Guid Value) : IEntityId<ShopSubscriptionId>
{
    public static ShopSubscriptionId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct SubscriptionPeriodId(Guid Value) : IEntityId<SubscriptionPeriodId>
{
    public static SubscriptionPeriodId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct SubscriptionOverrideId(Guid Value) : IEntityId<SubscriptionOverrideId>
{
    public static SubscriptionOverrideId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

/// <summary>Spec §15 statuses. <c>None</c> is a shop that has never had a subscription.</summary>
public enum SubscriptionStatus
{
    None,
    Active,
    ExpiringSoon,
    Expired,
    Suspended,
}

public enum PeriodKind
{
    Assigned,
    Renewed,
}

/// <summary>The plan price a period was charged: the version, its amount and currency, copied at recording time.</summary>
public sealed record PriceSnapshot(PlanPriceId? PriceId, decimal Amount, string Currency);

/// <summary>
/// SuperAdmin pricing of a non-standard period (D-081): the explicit total recorded instead of the plan price, the plan
/// price it replaces (null when the plan had none on the start date), and the reason. Kept on the period for good.
/// </summary>
public sealed record CustomPricing(decimal Total, decimal? StandardAmount, string Reason);

/// <summary>
/// A shop's platform subscription (spec §15), recorded manually by admins — v1 collects no payment. It keeps every
/// period (assignment and renewals) with the plan name and price snapshot of that moment, plus every override, so the
/// commercial history never changes when a plan or its price does (R-SUB-02/03).
/// </summary>
/// <remarks>
/// Coverage never has a gap in the future (D-077): the first period starts on or before the day it is recorded, and a
/// renewal either continues the day after the last period or, once the subscription has lapsed, starts no later than
/// today. So "today" is always inside a period or after the last one, and the status depends only on
/// <see cref="EndDate"/>, a stored column the admin list filters on in SQL.
/// </remarks>
public sealed class ShopSubscription : AggregateRoot<ShopSubscriptionId>, IShopOwned, IConcurrencyVersioned
{
    public const int MaxReasonLength = 500;

    private readonly List<SubscriptionPeriod> _periods = [];
    private readonly List<SubscriptionOverride> _overrides = [];

    private ShopSubscription(ShopSubscriptionId id, ShopId shopId, DateTimeOffset now)
        : base(id)
    {
        ShopId = shopId;
        CreatedAt = now;
    }

    private ShopSubscription()
    {
    }

    public ShopId ShopId { get; private set; }

    /// <summary>The plan of the latest period.</summary>
    public SubscriptionPlanId PlanId { get; private set; }

    /// <summary>First day of the first period (platform calendar, D-077).</summary>
    public DateOnly StartDate { get; private set; }

    /// <summary>Last day (inclusive) of the latest period.</summary>
    public DateOnly EndDate { get; private set; }

    public bool IsSuspended { get; private set; }

    public string? SuspensionReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public IReadOnlyList<SubscriptionPeriod> Periods => _periods;

    public IReadOnlyList<SubscriptionOverride> Overrides => _overrides;

    public SubscriptionPeriod LatestPeriod => _periods.OrderByDescending(p => p.PeriodStart).First();

    /// <summary>The period covering <paramref name="today"/>, or the latest one once the subscription has lapsed.</summary>
    public SubscriptionPeriod PeriodInForce(DateOnly today) =>
        _periods.FirstOrDefault(p => p.PeriodStart <= today && today <= p.PeriodEnd) ?? LatestPeriod;

    /// <summary>The default start of a renewal: the day after the last period, or today once it has lapsed.</summary>
    public DateOnly NextRenewalStart(DateOnly today) => EndDate >= today.AddDays(-1) ? EndDate.AddDays(1) : today;

    /// <summary>
    /// A period is standard when it starts today or later and lasts exactly the plan's interval. Anything else — a
    /// custom number of days or a back-dated start — needs SuperAdmin custom pricing with an explicit total (D-081).
    /// </summary>
    public static bool IsStandardPeriod(SubscriptionPlan plan, DateOnly start, DateOnly end, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return start >= today && end == plan.PeriodEnd(start);
    }

    /// <summary>Starts a subscription with its first period; it cannot start in the future, and a past start needs custom pricing.</summary>
    public static Result<ShopSubscription> Assign(
        ShopSubscriptionId id, ShopId shopId, SubscriptionPlan plan, DateOnly start, DateOnly end, PriceSnapshot price, CustomPricing? custom,
        string? notes, Guid? by, DateOnly today, DateTimeOffset now)
    {
        if (start > today)
        {
            return PlanErrors.Field("startDate", "validation.date_in_future");
        }

        if (end < start)
        {
            return PlanErrors.Field("durationDays", "validation.invalid");
        }

        if (CheckPricing(plan, start, end, today, custom) is { } pricing)
        {
            return pricing;
        }

        var subscription = new ShopSubscription(id, shopId, now);
        subscription.AddPeriod(PeriodKind.Assigned, plan, start, end, price, custom, notes, by, now);
        return subscription;
    }

    /// <summary>Records a renewal period; its price snapshot comes from the plan version in force on its start date.</summary>
    public Result<SubscriptionPeriod> Renew(
        SubscriptionPlan plan, DateOnly start, DateOnly end, PriceSnapshot price, CustomPricing? custom, string? notes, Guid? by, DateOnly today, DateTimeOffset now)
    {
        if (end < start)
        {
            return PlanErrors.Field("durationDays", "validation.invalid");
        }

        // Periods never overlap, and never leave a future gap: continue the day after, or restart by today.
        if (start <= EndDate)
        {
            return PlanErrors.Field("startDate", "validation.period_overlap");
        }

        if (start != EndDate.AddDays(1) && start > today)
        {
            return PlanErrors.Field("startDate", "validation.period_gap");
        }

        if (CheckPricing(plan, start, end, today, custom) is { } pricing)
        {
            return pricing;
        }

        return AddPeriod(PeriodKind.Renewed, plan, start, end, price, custom, notes, by, now);
    }

    /// <summary>
    /// SuperAdmin override of the period in force: a shop-specific price and/or end date, with a reason. The previous
    /// values are kept in the override record, so the change itself is part of the history. Only the latest period's
    /// end can move (an earlier one would overlap the next or open a gap before it).
    /// </summary>
    public Result<SubscriptionOverride> Override(
        SubscriptionOverrideId id, decimal? price, DateOnly? end, string reason, Guid? by, DateOnly today, DateTimeOffset now)
    {
        var period = PeriodInForce(today);
        if (price is null && end is null)
        {
            return PlanErrors.Field("price", "validation.required");
        }

        if (price is { } amount && !PlanPricing.IsValidAmount(amount))
        {
            return PlanErrors.Field("price", "validation.price_invalid");
        }

        if (end is { } newEnd)
        {
            if (!ReferenceEquals(period, LatestPeriod))
            {
                return PlanErrors.Field("endDate", "validation.period_not_latest");
            }

            if (newEnd < period.PeriodStart)
            {
                return PlanErrors.Field("endDate", "validation.invalid");
            }
        }

        var record = new SubscriptionOverride(id, Id, ShopId, period.Id, period.Amount, price, period.PeriodEnd, end, reason, by, now);
        _overrides.Add(record);
        period.ApplyOverride(price, end);
        EndDate = LatestPeriod.PeriodEnd;
        UpdatedAt = now;
        return record;
    }

    public void Suspend(string reason, DateTimeOffset now)
    {
        IsSuspended = true;
        SuspensionReason = reason;
        UpdatedAt = now;
    }

    public void Reinstate(DateTimeOffset now)
    {
        IsSuspended = false;
        SuspensionReason = null;
        UpdatedAt = now;
    }

    private static Error? CheckPricing(SubscriptionPlan plan, DateOnly start, DateOnly end, DateOnly today, CustomPricing? custom)
    {
        if (custom is null)
        {
            return IsStandardPeriod(plan, start, end, today) ? null : SubscriptionErrors.CustomPricingRequired();
        }

        if (!PlanPricing.IsValidAmount(custom.Total))
        {
            return PlanErrors.Field("price", "validation.price_invalid");
        }

        return string.IsNullOrWhiteSpace(custom.Reason) ? PlanErrors.Field("reason", "validation.reason_required") : null;
    }

    private SubscriptionPeriod AddPeriod(
        PeriodKind kind, SubscriptionPlan plan, DateOnly start, DateOnly end, PriceSnapshot price, CustomPricing? custom, string? notes, Guid? by, DateTimeOffset now)
    {
        var period = new SubscriptionPeriod(
            EntityId.New<SubscriptionPeriodId>(), Id, ShopId, kind, plan.Id, plan.NameAr, plan.NameEn, start, end, price, notes, by, now);
        if (custom is not null)
        {
            period.ApplyCustomPricing(custom);
        }

        _periods.Add(period);
        PlanId = plan.Id;
        StartDate = _periods.Min(p => p.PeriodStart);
        EndDate = end;
        UpdatedAt = now;
        return period;
    }
}

/// <summary>One recorded period (assignment or renewal) with the plan name and price as they were then.</summary>
public sealed class SubscriptionPeriod : Entity<SubscriptionPeriodId>, IShopOwned
{
    internal SubscriptionPeriod(
        SubscriptionPeriodId id, ShopSubscriptionId subscriptionId, ShopId shopId, PeriodKind kind, SubscriptionPlanId planId,
        string planNameAr, string planNameEn, DateOnly start, DateOnly end, PriceSnapshot price, string? notes, Guid? recordedBy, DateTimeOffset recordedAt)
        : base(id)
    {
        SubscriptionId = subscriptionId;
        ShopId = shopId;
        Kind = kind;
        PlanId = planId;
        PlanNameAr = planNameAr;
        PlanNameEn = planNameEn;
        PeriodStart = start;
        PeriodEnd = end;
        PlanPriceId = price.PriceId;
        Amount = price.Amount;
        Currency = price.Currency;
        Notes = notes;
        RecordedBy = recordedBy;
        RecordedAt = recordedAt;
    }

    private SubscriptionPeriod()
    {
        PlanNameAr = PlanNameEn = Currency = string.Empty;
    }

    public ShopSubscriptionId SubscriptionId { get; private set; }

    public ShopId ShopId { get; private set; }

    public PeriodKind Kind { get; private set; }

    public SubscriptionPlanId PlanId { get; private set; }

    public string PlanNameAr { get; private set; }

    public string PlanNameEn { get; private set; }

    /// <summary>First day, in the shop's time zone.</summary>
    public DateOnly PeriodStart { get; private set; }

    /// <summary>Last day (inclusive), in the shop's time zone.</summary>
    public DateOnly PeriodEnd { get; private set; }

    /// <summary>The plan price version charged (null when the amount came only from an override).</summary>
    public PlanPriceId? PlanPriceId { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    public bool IsOverridden { get; private set; }

    /// <summary>For a SuperAdmin-priced period (D-081): what the plan would have charged, if it had a price then.</summary>
    public decimal? StandardAmount { get; private set; }

    /// <summary>Why a SuperAdmin priced this period explicitly (D-081); null for a standard period.</summary>
    public string? PricingReason { get; private set; }

    public string? Notes { get; private set; }

    public Guid? RecordedBy { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    internal void ApplyCustomPricing(CustomPricing custom)
    {
        StandardAmount = custom.StandardAmount;
        Amount = custom.Total;
        PricingReason = custom.Reason;
        IsOverridden = true;
    }

    internal void ApplyOverride(decimal? price, DateOnly? end)
    {
        if (price is { } amount)
        {
            Amount = amount;
        }

        if (end is { } newEnd)
        {
            PeriodEnd = newEnd;
        }

        IsOverridden = true;
    }
}

/// <summary>An audited SuperAdmin override: what the current period was, what it became, why and by whom.</summary>
public sealed class SubscriptionOverride : Entity<SubscriptionOverrideId>, IShopOwned
{
    internal SubscriptionOverride(
        SubscriptionOverrideId id, ShopSubscriptionId subscriptionId, ShopId shopId, SubscriptionPeriodId periodId,
        decimal previousAmount, decimal? newAmount, DateOnly previousEnd, DateOnly? newEnd, string reason, Guid? by, DateTimeOffset at)
        : base(id)
    {
        SubscriptionId = subscriptionId;
        ShopId = shopId;
        PeriodId = periodId;
        PreviousAmount = previousAmount;
        NewAmount = newAmount;
        PreviousEnd = previousEnd;
        NewEnd = newEnd;
        Reason = reason;
        OverriddenBy = by;
        OverriddenAt = at;
    }

    private SubscriptionOverride()
    {
        Reason = string.Empty;
    }

    public ShopSubscriptionId SubscriptionId { get; private set; }

    public ShopId ShopId { get; private set; }

    public SubscriptionPeriodId PeriodId { get; private set; }

    public decimal PreviousAmount { get; private set; }

    public decimal? NewAmount { get; private set; }

    public DateOnly PreviousEnd { get; private set; }

    public DateOnly? NewEnd { get; private set; }

    public string Reason { get; private set; }

    public Guid? OverriddenBy { get; private set; }

    public DateTimeOffset OverriddenAt { get; private set; }
}

/// <summary>Computed status (R-SUB-04). Dates are the shop's local calendar days; a period covers its whole end day.</summary>
public static class SubscriptionStatusCalculator
{
    public static SubscriptionStatus Calculate(bool suspended, DateOnly? end, DateOnly today, int expiringSoonThresholdDays)
    {
        if (end is not { } lastDay)
        {
            return SubscriptionStatus.None;
        }

        if (suspended)
        {
            return SubscriptionStatus.Suspended;
        }

        if (today > lastDay)
        {
            return SubscriptionStatus.Expired;
        }

        return DaysRemaining(lastDay, today) <= expiringSoonThresholdDays ? SubscriptionStatus.ExpiringSoon : SubscriptionStatus.Active;
    }

    /// <summary>Whole days left including today (the end day counts as 1); 0 once expired.</summary>
    public static int DaysRemaining(DateOnly end, DateOnly today) => Math.Max(0, end.DayNumber - today.DayNumber + 1);

    /// <summary>The shop's local date for an instant (spec §6: store UTC, convert at the boundaries).</summary>
    public static DateOnly LocalDate(DateTimeOffset instant, string timeZone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(timeZone)).DateTime);
}

public static class SubscriptionErrors
{
    public static Error NoSubscription() => Error.NotFound("subscription.not_found", "The shop has no subscription.");

    /// <summary>A custom duration or a back-dated start: only SuperAdmin, with an explicit total and a reason (D-081).</summary>
    public static Error CustomPricingRequired() =>
        Error.Forbidden("subscription.custom_pricing_required", "A custom duration or a past start date needs a SuperAdmin override with an explicit total and a reason.");

    public static Error AlreadyAssigned() => Error.Conflict("subscription.already_assigned", "The shop already has a subscription; renew it instead.");
}

/// <summary>
/// The platform read model of a shop's coverage: its last covered day and whether it is suspended, nothing commercial.
/// Deliberately not shop-owned, so discovery and booking gates can read any shop's status without a tenant scope
/// (D-078); it is written only together with the shop's <see cref="ShopSubscription"/>, in the same unit of work.
/// </summary>
public sealed class SubscriptionCoverage
{
    public SubscriptionCoverage(ShopId shopId)
    {
        ShopId = shopId;
    }

    private SubscriptionCoverage()
    {
    }

    public ShopId ShopId { get; private set; }

    public DateOnly EndDate { get; private set; }

    public bool IsSuspended { get; private set; }

    public void CopyFrom(ShopSubscription subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        EndDate = subscription.EndDate;
        IsSuspended = subscription.IsSuspended;
    }
}
