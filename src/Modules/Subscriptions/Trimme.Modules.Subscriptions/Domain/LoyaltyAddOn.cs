using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.Modules.Subscriptions.Domain;

public readonly record struct LoyaltyAddOnPriceId(Guid Value) : IEntityId<LoyaltyAddOnPriceId>
{
    public static LoyaltyAddOnPriceId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct LoyaltyAddOnPeriodId(Guid Value) : IEntityId<LoyaltyAddOnPeriodId>
{
    public static LoyaltyAddOnPeriodId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

/// <summary>
/// One price version of the loyalty add-on (D-149), like a plan's price (R-SUB-02): SuperAdmin adds versions from a date,
/// never edits or deletes one, and each period keeps a snapshot of the version it was recorded with.
/// </summary>
public sealed class LoyaltyAddOnPrice : Entity<LoyaltyAddOnPriceId>
{
    private LoyaltyAddOnPrice(LoyaltyAddOnPriceId id)
        : base(id)
    {
        Currency = string.Empty;
    }

    private LoyaltyAddOnPrice()
    {
        Currency = string.Empty;
    }

    /// <summary>1, 2, 3… in the order the versions were added.</summary>
    public int VersionNumber { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>A new version, checked against the existing ones: from today or later, one per date, a valid amount.</summary>
    public static Result<LoyaltyAddOnPrice> Add(
        IReadOnlyCollection<LoyaltyAddOnPrice> existing, decimal amount, string currency, string platformCurrency, DateOnly effectiveFrom, DateOnly today,
        Guid? createdBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(existing);
        if (!PlanPricing.IsValidAmount(amount))
        {
            return PlanErrors.Field("amount", "validation.price_invalid");
        }

        if (!string.Equals(currency, platformCurrency, StringComparison.Ordinal))
        {
            return PlanErrors.Field("currency", "validation.invalid");
        }

        if (effectiveFrom < today)
        {
            return PlanErrors.Field("effectiveFrom", "validation.date_in_past");
        }

        if (existing.Any(p => p.EffectiveFrom == effectiveFrom))
        {
            return PlanErrors.Field("effectiveFrom", "validation.price_date_taken");
        }

        return new LoyaltyAddOnPrice(LoyaltyAddOnPriceId.From(Guid.CreateVersion7(now)))
        {
            VersionNumber = existing.Count + 1,
            Amount = amount,
            Currency = currency,
            EffectiveFrom = effectiveFrom,
            CreatedBy = createdBy,
            CreatedAt = now,
        };
    }

    /// <summary>The version in force on <paramref name="date"/> (the latest that started on or before it).</summary>
    public static LoyaltyAddOnPrice? On(IEnumerable<LoyaltyAddOnPrice> prices, DateOnly date) =>
        prices.Where(p => p.EffectiveFrom <= date).OrderByDescending(p => p.EffectiveFrom).FirstOrDefault();
}

/// <summary>
/// A shop's loyalty add-on period (D-149, the client's «إضافة مدفوعة»): the admin switches it on until an end date, with
/// the add-on price in force on its start, and can switch it off early. No payment is taken. While a period covers today
/// the shop's customers earn and redeem points; outside one, both stop by themselves and the points are kept.
/// </summary>
public sealed class LoyaltyAddOnPeriod : Entity<LoyaltyAddOnPeriodId>, IShopOwned
{
    public const int MaxNotesLength = 500;

    private LoyaltyAddOnPeriod(LoyaltyAddOnPeriodId id)
        : base(id)
    {
        Currency = string.Empty;
    }

    private LoyaltyAddOnPeriod()
    {
        Currency = string.Empty;
    }

    public ShopId ShopId { get; private set; }

    public DateOnly StartDate { get; private set; }

    /// <summary>The last day (inclusive) it was recorded for.</summary>
    public DateOnly EndDate { get; private set; }

    public LoyaltyAddOnPriceId PriceId { get; private set; }

    public int PriceVersionNumber { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    public string? Notes { get; private set; }

    public Guid? RecordedBy { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    /// <summary>The day the admin switched it off; from then on it is not in force.</summary>
    public DateOnly? StoppedOn { get; private set; }

    public Guid? StoppedBy { get; private set; }

    /// <summary>The last day it is in force: its end, or the day before it was switched off.</summary>
    public DateOnly LastDay => StoppedOn is { } stopped && stopped.AddDays(-1) < EndDate ? stopped.AddDays(-1) : EndDate;

    public static LoyaltyAddOnPeriod Record(
        ShopId shopId, DateOnly start, DateOnly end, LoyaltyAddOnPrice price, string? notes, Guid? by, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(price);
        return new LoyaltyAddOnPeriod(LoyaltyAddOnPeriodId.From(Guid.CreateVersion7(now)))
        {
            ShopId = shopId,
            StartDate = start,
            EndDate = end,
            PriceId = price.Id,
            PriceVersionNumber = price.VersionNumber,
            Amount = price.Amount,
            Currency = price.Currency,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            RecordedBy = by,
            RecordedAt = now,
        };
    }

    /// <summary>Switched off from <paramref name="today"/> (a period still ahead is switched off from its start).</summary>
    public void Stop(DateOnly today, Guid? by)
    {
        StoppedOn ??= today > StartDate ? today : StartDate;
        StoppedBy ??= by;
    }

    public bool CoversOrFollows(DateOnly today) => StoppedOn is null && EndDate >= today;
}

public static class LoyaltyAddOnRules
{
    /// <summary>In force on <paramref name="today"/> (platform calendar, like subscriptions, D-077).</summary>
    public static bool IsActive(DateOnly? start, DateOnly? end, DateOnly today) => start is { } s && end is { } e && s <= today && today <= e;
}

public static class LoyaltyAddOnErrors
{
    public static Error NoSubscription() =>
        Error.Conflict("loyalty_add_on.no_subscription", "The loyalty add-on goes with a subscription; assign one first.");

    public static Error NoPrice(DateOnly date) =>
        Error.BusinessRule("loyalty_add_on.no_price", "The loyalty add-on has no price on that date; SuperAdmin adds one first.")
            .WithDetail("date", date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

    public static Error NotActive() => Error.Conflict("loyalty_add_on.not_active", "The loyalty add-on is not in force.");

    public static Error EndBeforeStart() => PlanErrors.Field("endDate", "validation.out_of_range");
}
