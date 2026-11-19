using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Notifications;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Subscriptions.Domain;

namespace Trimme.Modules.Subscriptions.Application;

/// <summary>One recorded loyalty add-on period (D-149), with the add-on price version it was recorded with.</summary>
public sealed record LoyaltyAddOnPeriodResponse(
    Guid Id,
    DateOnly StartDate,
    DateOnly EndDate,
    DateOnly LastDay,
    DateOnly? StoppedOn,
    int PriceVersionNumber,
    decimal Amount,
    string Currency,
    string? Notes,
    DateTimeOffset RecordedAt);

/// <summary>
/// The shop's loyalty add-on (D-149): whether it is in force today, its first and last day, the days left, and every
/// period recorded (newest first). Customers earn and redeem points only while it is in force; points are kept after.
/// </summary>
public sealed record LoyaltyAddOnResponse(bool Active, DateOnly? StartDate, DateOnly? EndDate, int DaysRemaining, IReadOnlyList<LoyaltyAddOnPeriodResponse> Periods);

/// <summary>
/// WhatsApp messages to the shop's customers this calendar month (D-149, shop time zone): how many were handed to the
/// provider, and the plan's allowance (null = unlimited, 0 = none).
/// </summary>
public sealed record WhatsAppUsageResponse(DateOnly Month, int Used, int? Allowance);

public sealed record LoyaltyAddOnPriceResponse(Guid Id, int VersionNumber, decimal Amount, string Currency, DateOnly EffectiveFrom, DateTimeOffset CreatedAt);

/// <summary>The add-on's price versions (newest first), the one in force today and the next scheduled one.</summary>
public sealed record LoyaltyAddOnPricesResponse(LoyaltyAddOnPriceResponse? Current, LoyaltyAddOnPriceResponse? Upcoming, IReadOnlyList<LoyaltyAddOnPriceResponse> Prices);

/// <summary>
/// The loyalty add-on and the WhatsApp usage of one shop, for its subscription page (the owner's and the admin's), read
/// through the caller's data scope.
/// </summary>
internal sealed class SubscriptionExtras(TrimmeDbContext db, IWhatsAppUsage usage, TimeProvider clock)
{
    public async Task<LoyaltyAddOnResponse> AddOnAsync(ShopId shopId, DateOnly today, CancellationToken cancellationToken)
    {
        var coverage = await db.Set<SubscriptionCoverage>().AsNoTracking().SingleOrDefaultAsync(c => c.ShopId == shopId, cancellationToken);
        var periods = await db.Set<LoyaltyAddOnPeriod>().AsNoTracking().Where(p => p.ShopId == shopId)
            .OrderByDescending(p => p.StartDate).ThenByDescending(p => p.RecordedAt).ToListAsync(cancellationToken);
        var active = LoyaltyAddOnRules.IsActive(coverage?.LoyaltyAddOnStart, coverage?.LoyaltyAddOnEnd, today);
        return new LoyaltyAddOnResponse(
            active,
            coverage?.LoyaltyAddOnStart,
            coverage?.LoyaltyAddOnEnd,
            active && coverage?.LoyaltyAddOnEnd is { } end ? SubscriptionStatusCalculator.DaysRemaining(end, today) : 0,
            [.. periods.Select(Period)]);
    }

    public async Task<WhatsAppUsageResponse> WhatsAppAsync(ShopSummary shop, ShopSubscription? subscription, DateOnly today, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shop);
        var month = WhatsAppMonths.Of(clock.GetUtcNow(), shop.TimeZone);
        return new WhatsAppUsageResponse(month, await usage.UsedAsync(shop.Id, month, cancellationToken), await AllowanceAsync(db, subscription, today, cancellationToken));
    }

    /// <summary>The allowance of the plan in force (null without a subscription, or for a plan without one: unlimited).</summary>
    public static async Task<int?> AllowanceAsync(TrimmeDbContext db, ShopSubscription? subscription, DateOnly today, CancellationToken cancellationToken)
    {
        if (subscription is null || subscription.Periods.Count == 0)
        {
            return null;
        }

        var planId = subscription.PeriodInForce(today).PlanId;
        return await db.Set<SubscriptionPlan>().AsNoTracking().Where(p => p.Id == planId).Select(p => p.MonthlyWhatsAppMessages).SingleOrDefaultAsync(cancellationToken);
    }

    public static LoyaltyAddOnPeriodResponse Period(LoyaltyAddOnPeriod p) =>
        new(p.Id.Value, p.StartDate, p.EndDate, p.LastDay, p.StoppedOn, p.PriceVersionNumber, p.Amount, p.Currency, p.Notes, p.RecordedAt);

    public static LoyaltyAddOnPriceResponse Price(LoyaltyAddOnPrice p) => new(p.Id.Value, p.VersionNumber, p.Amount, p.Currency, p.EffectiveFrom, p.CreatedAt);
}
