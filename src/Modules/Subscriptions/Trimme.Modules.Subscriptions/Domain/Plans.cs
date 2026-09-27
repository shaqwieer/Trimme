using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Results;

namespace Trimme.Modules.Subscriptions.Domain;

public readonly record struct SubscriptionPlanId(Guid Value) : IEntityId<SubscriptionPlanId>
{
    public static SubscriptionPlanId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct PlanPriceId(Guid Value) : IEntityId<PlanPriceId>
{
    public static PlanPriceId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

public enum BillingIntervalUnit
{
    Day,
    Month,
}

/// <summary>Draft (being prepared) → Published (offered) → Inactive (not offered, may return) → Archived (final).</summary>
public enum PlanStatus
{
    Draft,
    Published,
    Inactive,
    Archived,
}

/// <summary>One localized feature line of a plan ("حتى ٦ حلاقين" / "Up to 6 professionals"). Stored as JSON.</summary>
public sealed class PlanFeature
{
    public string Ar { get; set; } = string.Empty;

    public string En { get; set; } = string.Empty;

    public static PlanFeature Of(string ar, string en) => new() { Ar = ar, En = en };
}

/// <summary>The editable details of a plan (everything except its prices and status).</summary>
public sealed record PlanDetails(
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    IReadOnlyList<PlanFeature> Features,
    int? MaxProfessionals,
    int? MaxServices,
    BillingIntervalUnit IntervalUnit,
    int IntervalCount,
    int? TrialDays,
    int? GraceDays,
    bool AvailableToNewShops);

/// <summary>
/// A subscription plan of the platform (spec §7, §15), managed by SuperAdmin only and entirely data-driven: nothing
/// about plans is hardcoded (R-NEG-08). Its price lives in an append-only history of versions (<see cref="PlanPrice"/>)
/// so a price change never rewrites what an existing subscription was charged (R-SUB-02).
/// </summary>
public sealed class SubscriptionPlan : AggregateRoot<SubscriptionPlanId>, IConcurrencyVersioned
{
    public const int MaxNameLength = 80;
    public const int MaxDescriptionLength = 500;
    public const int MaxFeatures = 12;

    private readonly List<PlanPrice> _prices = [];

    private SubscriptionPlan(SubscriptionPlanId id, PlanDetails details, int displayOrder, DateTimeOffset now)
        : base(id)
    {
        NameAr = details.NameAr;
        NameEn = details.NameEn;
        Apply(details);
        Status = PlanStatus.Draft;
        DisplayOrder = displayOrder;
        CreatedAt = now;
    }

    private SubscriptionPlan()
    {
        NameAr = NameEn = string.Empty;
    }

    public string NameAr { get; private set; }

    public string NameEn { get; private set; }

    public string? DescriptionAr { get; private set; }

    public string? DescriptionEn { get; private set; }

    public List<PlanFeature> Features { get; private set; } = [];

    /// <summary>Null means no limit.</summary>
    public int? MaxProfessionals { get; private set; }

    public int? MaxServices { get; private set; }

    public BillingIntervalUnit IntervalUnit { get; private set; }

    public int IntervalCount { get; private set; }

    public int? TrialDays { get; private set; }

    public int? GraceDays { get; private set; }

    public bool AvailableToNewShops { get; private set; }

    public PlanStatus Status { get; private set; }

    public int DisplayOrder { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public IReadOnlyList<PlanPrice> Prices => _prices;

    public static Result<SubscriptionPlan> Create(SubscriptionPlanId id, PlanDetails details, int displayOrder, DateTimeOffset now) =>
        Validate(details) is { } invalid ? invalid : new SubscriptionPlan(id, details, displayOrder, now);

    public Result Update(PlanDetails details, DateTimeOffset now)
    {
        if (Status == PlanStatus.Archived)
        {
            return PlanErrors.Archived();
        }

        if (Validate(details) is { } invalid)
        {
            return invalid;
        }

        Apply(details);
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Adds a price version effective from <paramref name="effectiveFrom"/>. Versions are never edited or deleted; the
    /// new one applies only to assignments and renewals whose period starts on or after its date.
    /// </summary>
    public Result<PlanPrice> AddPrice(
        PlanPriceId id, decimal amount, string currency, string platformCurrency, DateOnly effectiveFrom, DateOnly today, Guid? createdBy, DateTimeOffset now)
    {
        if (Status == PlanStatus.Archived)
        {
            return PlanErrors.Archived();
        }

        if (!PlanPricing.IsValidAmount(amount))
        {
            return PlanErrors.Field("amount", "validation.price_invalid");
        }

        if (!string.Equals(currency, platformCurrency, StringComparison.Ordinal))
        {
            return PlanErrors.Field("currency", "validation.invalid");
        }

        // History is never rewritten: a new version cannot start in the past, and only one version starts on a date.
        if (effectiveFrom < today)
        {
            return PlanErrors.Field("effectiveFrom", "validation.date_in_past");
        }

        if (_prices.Any(p => p.EffectiveFrom == effectiveFrom))
        {
            return PlanErrors.Field("effectiveFrom", "validation.price_date_taken");
        }

        var price = new PlanPrice(id, Id, _prices.Count + 1, amount, currency, effectiveFrom, createdBy, now);
        _prices.Add(price);
        UpdatedAt = now;
        return price;
    }

    /// <summary>The price version in force on <paramref name="date"/> (the latest that started on or before it).</summary>
    public PlanPrice? PriceOn(DateOnly date) =>
        _prices.Where(p => p.EffectiveFrom <= date).OrderByDescending(p => p.EffectiveFrom).FirstOrDefault();

    public Result Publish(DateTimeOffset now)
    {
        if (Status == PlanStatus.Archived)
        {
            return PlanErrors.Archived();
        }

        if (_prices.Count == 0)
        {
            return PlanErrors.NoPrice();
        }

        Status = PlanStatus.Published;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result Deactivate(DateTimeOffset now)
    {
        if (Status == PlanStatus.Archived)
        {
            return PlanErrors.Archived();
        }

        Status = PlanStatus.Inactive;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result Archive(DateTimeOffset now)
    {
        if (Status == PlanStatus.Archived)
        {
            return PlanErrors.Archived();
        }

        Status = PlanStatus.Archived;
        AvailableToNewShops = false;
        UpdatedAt = now;
        return Result.Success();
    }

    public void MoveTo(int displayOrder) => DisplayOrder = displayOrder;

    /// <summary>The last day (inclusive) of a period of this plan's interval that starts on <paramref name="start"/>.</summary>
    public DateOnly PeriodEnd(DateOnly start) => SubscriptionDates.End(start, IntervalUnit, IntervalCount);

    private void Apply(PlanDetails details)
    {
        NameAr = details.NameAr.Trim();
        NameEn = details.NameEn.Trim();
        DescriptionAr = string.IsNullOrWhiteSpace(details.DescriptionAr) ? null : details.DescriptionAr.Trim();
        DescriptionEn = string.IsNullOrWhiteSpace(details.DescriptionEn) ? null : details.DescriptionEn.Trim();
        Features = [.. details.Features.Select(f => PlanFeature.Of(f.Ar.Trim(), f.En.Trim()))];
        MaxProfessionals = details.MaxProfessionals;
        MaxServices = details.MaxServices;
        IntervalUnit = details.IntervalUnit;
        IntervalCount = details.IntervalCount;
        TrialDays = details.TrialDays;
        GraceDays = details.GraceDays;
        AvailableToNewShops = details.AvailableToNewShops;
    }

    private static Error? Validate(PlanDetails details)
    {
        var maxCount = details.IntervalUnit == BillingIntervalUnit.Month ? 36 : 1095;
        if (details.IntervalCount < 1 || details.IntervalCount > maxCount)
        {
            return PlanErrors.Field("intervalCount", "validation.invalid");
        }

        if (details.Features.Count > MaxFeatures || details.Features.Any(f => string.IsNullOrWhiteSpace(f.Ar) || string.IsNullOrWhiteSpace(f.En)))
        {
            return PlanErrors.Field("features", "validation.invalid");
        }

        return details.MaxProfessionals is < 1 || details.MaxServices is < 1 || details.TrialDays is < 0 or > 365 || details.GraceDays is < 0 or > 365
            ? PlanErrors.Field("limits", "validation.invalid")
            : null;
    }
}

/// <summary>
/// One price version of a plan. Immutable: once written it is never changed, and subscriptions keep a snapshot of the
/// version (id, amount, currency) they were charged, so history cannot drift (R-SUB-02).
/// </summary>
public sealed class PlanPrice : Entity<PlanPriceId>
{
    internal PlanPrice(PlanPriceId id, SubscriptionPlanId planId, int versionNumber, decimal amount, string currency, DateOnly effectiveFrom, Guid? createdBy, DateTimeOffset createdAt)
        : base(id)
    {
        PlanId = planId;
        VersionNumber = versionNumber;
        Amount = amount;
        Currency = currency;
        EffectiveFrom = effectiveFrom;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
    }

    private PlanPrice()
    {
        Currency = string.Empty;
    }

    public SubscriptionPlanId PlanId { get; private set; }

    /// <summary>1, 2, 3… in the order the versions were added.</summary>
    public int VersionNumber { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}

public static class PlanPricing
{
    public const decimal MaxAmount = 1_000_000m;

    /// <summary>0 to <see cref="MaxAmount"/>, at most 2 decimals (halalas).</summary>
    public static bool IsValidAmount(decimal amount) => amount is >= 0 and <= MaxAmount && decimal.Round(amount, 2) == amount;
}

public static class SubscriptionDates
{
    /// <summary>
    /// The last day (inclusive) of a period: start + interval − 1 day (design a-subs: renewed on 1 Oct 2026 for a year →
    /// until 30 Sep 2027). When month arithmetic clamps (31 Jan + 1 month has no 31 Feb), the period runs to the end of
    /// the target month instead, so the next period starts on the 1st and a chain of renewals never loses a day.
    /// </summary>
    public static DateOnly End(DateOnly start, BillingIntervalUnit unit, int count)
    {
        if (unit == BillingIntervalUnit.Day)
        {
            return start.AddDays(count - 1);
        }

        var target = start.AddMonths(count);
        return target.Day < start.Day
            ? new DateOnly(target.Year, target.Month, DateTime.DaysInMonth(target.Year, target.Month))
            : target.AddDays(-1);
    }

    public static DateOnly EndAfterDays(DateOnly start, int days) => start.AddDays(days - 1);
}

public static class PlanErrors
{
    public static Error NotFound() => Error.NotFound("plan.not_found", "The plan was not found.");

    public static Error Archived() => Error.Conflict("plan.archived", "An archived plan cannot change.");

    public static Error NoPrice() => Error.BusinessRule("plan.no_price", "A plan needs a price before it can be published.");

    public static Error NotOffered() => Field("planId", "validation.plan_not_offered");

    public static Error NoPriceOn(DateOnly date) => Field("startDate", "validation.plan_no_price").WithDetail("date", date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

    public static Error Field(string field, string code) =>
        Error.Validation("validation.failed", "The request is invalid.",
            new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [code] });
}
