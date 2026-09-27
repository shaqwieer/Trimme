using Trimme.BuildingBlocks.Application.Platform;
using Trimme.Modules.Subscriptions.Domain;

namespace Trimme.Modules.Subscriptions.Application;

public sealed record PlanFeatureDto(string Ar, string En);

public sealed record PlanPriceResponse(Guid Id, int VersionNumber, decimal Amount, string Currency, DateOnly EffectiveFrom, DateTimeOffset CreatedAt);

/// <summary>
/// A plan as SuperAdmin manages it. <c>CurrentPrice</c> is the version in force today (platform calendar) and
/// <c>UpcomingPrice</c> the next scheduled one; <c>Prices</c> is the full history, newest first.
/// </summary>
public sealed record PlanResponse(
    Guid Id,
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    IReadOnlyList<PlanFeatureDto> Features,
    int? MaxProfessionals,
    int? MaxServices,
    BillingIntervalUnit IntervalUnit,
    int IntervalCount,
    int? TrialDays,
    int? GraceDays,
    bool AvailableToNewShops,
    PlanStatus Status,
    int DisplayOrder,
    PlanPriceResponse? CurrentPrice,
    PlanPriceResponse? UpcomingPrice,
    IReadOnlyList<PlanPriceResponse> Prices,
    int SubscriptionCount,
    uint Version);

/// <summary>One recorded period with the plan name and price as they were charged (never recomputed).</summary>
public sealed record SubscriptionPeriodResponse(
    Guid Id,
    PeriodKind Kind,
    Guid PlanId,
    string PlanNameAr,
    string PlanNameEn,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    Guid? PlanPriceId,
    int? PriceVersionNumber,
    decimal Amount,
    string Currency,
    bool IsOverridden,
    decimal? StandardAmount,
    string? PricingReason,
    string? Notes,
    DateTimeOffset RecordedAt);

public sealed record SubscriptionOverrideResponse(
    Guid Id,
    Guid PeriodId,
    decimal PreviousAmount,
    decimal? NewAmount,
    DateOnly PreviousEnd,
    DateOnly? NewEnd,
    string Reason,
    DateTimeOffset OverriddenAt);

/// <summary>
/// A shop's subscription for admins. <c>Exists</c> is false (status <c>None</c>) until a plan is assigned;
/// <c>NextStart</c> is the default start date of the next assignment or renewal.
/// </summary>
public sealed record AdminShopSubscriptionResponse(
    Guid ShopId,
    string ShopNameAr,
    string ShopNameEn,
    bool Exists,
    SubscriptionStatus Status,
    Guid? PlanId,
    string? PlanNameAr,
    string? PlanNameEn,
    DateOnly? StartDate,
    DateOnly? EndDate,
    int DaysRemaining,
    decimal? CurrentAmount,
    string? Currency,
    bool IsSuspended,
    string? SuspensionReason,
    DateOnly Today,
    DateOnly NextStart,
    int ExpiringSoonThresholdDays,
    IReadOnlyList<SubscriptionPeriodResponse> Periods,
    IReadOnlyList<SubscriptionOverrideResponse> Overrides,
    uint? Version);

public sealed record AdminSubscriptionListItem(
    Guid ShopId,
    string ShopNameAr,
    string ShopNameEn,
    string ShopSlug,
    Guid PlanId,
    string PlanNameAr,
    string PlanNameEn,
    DateOnly StartDate,
    DateOnly EndDate,
    int DaysRemaining,
    SubscriptionStatus Status,
    decimal CurrentAmount,
    string Currency,
    int RenewalCount);

/// <summary>Counts over every shop (a shop without a subscription counts as <c>None</c>).</summary>
public sealed record SubscriptionCounts(int Active, int ExpiringSoon, int Expired, int Suspended, int None);

public sealed record AdminSubscriptionListResponse(
    IReadOnlyList<AdminSubscriptionListItem> Items,
    int Page,
    int PageSize,
    int Total,
    SubscriptionCounts Counts,
    int ExpiringSoonThresholdDays,
    DateOnly Today);

public sealed record ShopRenewalResponse(DateOnly PeriodStart, DateOnly PeriodEnd, string PlanNameAr, string PlanNameEn, decimal Amount, string Currency);

/// <summary>The signed-in shop's own subscription, read-only (renewals go through the platform admins).</summary>
public sealed record ShopSubscriptionResponse(
    SubscriptionStatus Status,
    string? PlanNameAr,
    string? PlanNameEn,
    DateOnly? StartDate,
    DateOnly? EndDate,
    int DaysRemaining,
    int ElapsedPercent,
    int ExpiringSoonThresholdDays,
    bool HiddenFromDiscovery,
    IReadOnlyList<ShopRenewalResponse> Renewals);

internal static class SubscriptionView
{
    public static PlanPriceResponse Price(PlanPrice p) => new(p.Id.Value, p.VersionNumber, p.Amount, p.Currency, p.EffectiveFrom, p.CreatedAt);

    public static PlanResponse Plan(SubscriptionPlan plan, DateOnly today, int subscriptionCount) => new(
        plan.Id.Value,
        plan.NameAr,
        plan.NameEn,
        plan.DescriptionAr,
        plan.DescriptionEn,
        [.. plan.Features.Select(f => new PlanFeatureDto(f.Ar, f.En))],
        plan.MaxProfessionals,
        plan.MaxServices,
        plan.IntervalUnit,
        plan.IntervalCount,
        plan.TrialDays,
        plan.GraceDays,
        plan.AvailableToNewShops,
        plan.Status,
        plan.DisplayOrder,
        plan.PriceOn(today) is { } current ? Price(current) : null,
        plan.Prices.Where(p => p.EffectiveFrom > today).OrderBy(p => p.EffectiveFrom).FirstOrDefault() is { } next ? Price(next) : null,
        [.. plan.Prices.OrderByDescending(p => p.EffectiveFrom).Select(Price)],
        subscriptionCount,
        plan.Version);

    public static SubscriptionStatus Status(ShopSubscription? subscription, DateOnly today, PlatformSettingsSnapshot settings) =>
        subscription is null
            ? SubscriptionStatus.None
            : SubscriptionStatusCalculator.Calculate(subscription.IsSuspended, subscription.EndDate, today, settings.ExpiringSoonThresholdDays);

    public static SubscriptionPeriodResponse Period(SubscriptionPeriod p, IReadOnlyDictionary<PlanPriceId, int> versions) => new(
        p.Id.Value,
        p.Kind,
        p.PlanId.Value,
        p.PlanNameAr,
        p.PlanNameEn,
        p.PeriodStart,
        p.PeriodEnd,
        p.PlanPriceId?.Value,
        p.PlanPriceId is { } id && versions.TryGetValue(id, out var number) ? number : null,
        p.Amount,
        p.Currency,
        p.IsOverridden,
        p.StandardAmount,
        p.PricingReason,
        p.Notes,
        p.RecordedAt);

    /// <summary>Whether discovery hides the shop because of its subscription (D-014); the shop's own status is separate.</summary>
    public static bool HiddenBySubscription(SubscriptionStatus status, PlatformSettingsSnapshot settings) =>
        settings.ExpiredSubscriptionEnforcement == SubscriptionEnforcement.HideAndBlockNewOnlineBookings
        && status is SubscriptionStatus.None or SubscriptionStatus.Expired or SubscriptionStatus.Suspended;
}
