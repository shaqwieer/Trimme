using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Notifications;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Subscriptions.Domain;

namespace Trimme.Modules.Subscriptions.Application;

/// <summary>
/// What the shop's plan in force gives it (D-155, the client's «كل محل مربوط بباقة»): loyalty (<c>LoyaltyActive</c>
/// while the subscription is in force), the win-back message and after how many days, and the monthly marketing
/// messages (null unlimited, 0 none). Null on the responses without a subscription.
/// </summary>
public sealed record ShopPlanFeaturesResponse(bool IncludesLoyalty, bool LoyaltyActive, bool WinBackEnabled, int WinBackAfterDays, int? MonthlyMarketingMessages);

/// <summary>
/// The shop's win-back messages in the current window (D-155: «رسايل الدعوة هالشهر: ١٢٣ من ٥٠٠»): the window's first and
/// last day (a month from the day the subscription period started), how many went to the provider, and the plan's limit.
/// </summary>
public sealed record MarketingUsageResponse(DateOnly WindowStart, DateOnly WindowEnd, int Used, int? Allowance);

/// <summary>The plan's features and the marketing usage of one shop, for its subscription page (the owner's and the admin's).</summary>
internal sealed class SubscriptionExtras(TrimmeDbContext db, IMarketingUsage usage)
{
    public async Task<ShopPlanFeaturesResponse?> FeaturesAsync(ShopSubscription? subscription, SubscriptionStatus status, DateOnly today, CancellationToken cancellationToken)
    {
        if (await PlanAsync(db, subscription, today, cancellationToken) is not { } plan)
        {
            return null;
        }

        var inForce = status is not (SubscriptionStatus.None or SubscriptionStatus.Expired or SubscriptionStatus.Suspended);
        return new ShopPlanFeaturesResponse(
            plan.IncludesLoyalty, inForce && plan.IncludesLoyalty, plan.WinBackEnabled, plan.WinBackAfterDays, plan.MonthlyMarketingMessages);
    }

    public async Task<MarketingUsageResponse> MarketingAsync(ShopSummary shop, ShopSubscription? subscription, DateOnly today, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shop);
        var terms = await TermsAsync(db, subscription, today, cancellationToken);
        return new MarketingUsageResponse(terms.WindowStart, terms.WindowEnd, await usage.UsedAsync(shop.Id, terms.WindowStart, cancellationToken), terms.Allowance);
    }

    /// <summary>The plan of the period in force (or the latest one once it lapsed); null without a subscription.</summary>
    public static async Task<SubscriptionPlan?> PlanAsync(TrimmeDbContext db, ShopSubscription? subscription, DateOnly today, CancellationToken cancellationToken)
    {
        if (subscription is null || subscription.Periods.Count == 0)
        {
            return null;
        }

        var planId = subscription.PeriodInForce(today).PlanId;
        return await db.Set<SubscriptionPlan>().AsNoTracking().SingleOrDefaultAsync(p => p.Id == planId, cancellationToken);
    }

    /// <summary>
    /// The shop's marketing terms (D-155): the window from the day its period in force started (the calendar month
    /// without one), the plan's limit, and the win-back message while the subscription is in force.
    /// </summary>
    public static async Task<MarketingTerms> TermsAsync(TrimmeDbContext db, ShopSubscription? subscription, DateOnly today, CancellationToken cancellationToken)
    {
        if (subscription is null || subscription.Periods.Count == 0 || await PlanAsync(db, subscription, today, cancellationToken) is not { } plan)
        {
            var (start, end) = MarketingWindows.Calendar(today);
            return new MarketingTerms(start, end, null, false, PlanLimits.DefaultWinBackAfterDays);
        }

        var (windowStart, windowEnd) = MarketingWindows.Of(subscription.PeriodInForce(today).PeriodStart, today);
        var inForce = !subscription.IsSuspended && today <= subscription.EndDate;
        return new MarketingTerms(windowStart, windowEnd, plan.MonthlyMarketingMessages, inForce && plan.WinBackEnabled, plan.WinBackAfterDays);
    }
}
