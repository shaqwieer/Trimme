using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Platform;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Subscriptions.Domain;

namespace Trimme.Modules.Subscriptions.Application;

/// <summary>The signed-in shop's own subscription (tenant from claims; null without a tenant → 404).</summary>
internal sealed record GetMySubscriptionQuery : IQuery<ShopSubscriptionResponse?>;

internal sealed class GetMySubscriptionHandler(TrimmeDbContext db, ICurrentTenant tenant, IPlatformSettings settings, TimeProvider clock)
    : IQueryHandler<GetMySubscriptionQuery, ShopSubscriptionResponse?>
{
    public async Task<ShopSubscriptionResponse?> Handle(GetMySubscriptionQuery query, CancellationToken cancellationToken)
    {
        if (tenant.ShopId is not { } shopId)
        {
            return null;
        }

        var platform = await settings.GetAsync(cancellationToken);
        var today = platform.LocalDate(clock.GetUtcNow());
        var subscription = await db.Set<ShopSubscription>().AsNoTracking().Include(s => s.Periods)
            .SingleOrDefaultAsync(s => s.ShopId == shopId, cancellationToken);
        var status = SubscriptionView.Status(subscription, today, platform);
        if (subscription is null)
        {
            return new ShopSubscriptionResponse(
                status, null, null, null, null, 0, 0, platform.ExpiringSoonThresholdDays, SubscriptionView.HiddenBySubscription(status, platform), []);
        }

        var inForce = subscription.PeriodInForce(today);
        var totalDays = subscription.EndDate.DayNumber - inForce.PeriodStart.DayNumber + 1;
        var elapsed = Math.Clamp(today.DayNumber - inForce.PeriodStart.DayNumber + 1, 0, totalDays);
        return new ShopSubscriptionResponse(
            status,
            inForce.PlanNameAr,
            inForce.PlanNameEn,
            inForce.PeriodStart,
            subscription.EndDate,
            SubscriptionStatusCalculator.DaysRemaining(subscription.EndDate, today),
            totalDays <= 0 ? 100 : (int)Math.Round(100.0 * elapsed / totalDays),
            platform.ExpiringSoonThresholdDays,
            SubscriptionView.HiddenBySubscription(status, platform),
            [.. subscription.Periods.OrderByDescending(p => p.PeriodStart)
                .Select(p => new ShopRenewalResponse(p.PeriodStart, p.PeriodEnd, p.PlanNameAr, p.PlanNameEn, p.Amount, p.Currency))]);
    }
}
