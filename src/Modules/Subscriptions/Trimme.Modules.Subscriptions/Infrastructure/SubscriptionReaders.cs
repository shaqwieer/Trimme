using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Loyalty;
using Trimme.BuildingBlocks.Application.Notifications;
using Trimme.BuildingBlocks.Application.Platform;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Subscriptions.Application;
using Trimme.Modules.Subscriptions.Domain;

namespace Trimme.Modules.Subscriptions.Infrastructure;

/// <summary>
/// <see cref="ILoyaltyPlanStatus"/> (D-155) from the coverage read model, which any caller may read (D-078), and the
/// plans: loyalty is on while the subscription is in force and the plan of the period covering today includes it, on the
/// platform calendar like the subscription (D-077).
/// </summary>
internal sealed class LoyaltyPlanStatus(TrimmeDbContext db, IPlatformSettings settings, TimeProvider clock) : ILoyaltyPlanStatus
{
    public async Task<bool> IsActiveAsync(ShopId shopId, CancellationToken cancellationToken) =>
        (await ActiveManyAsync([shopId], cancellationToken)).GetValueOrDefault(shopId);

    public async Task<IReadOnlyDictionary<ShopId, bool>> ActiveManyAsync(IReadOnlyCollection<ShopId> shopIds, CancellationToken cancellationToken)
    {
        var ids = shopIds.Distinct().ToArray();
        var today = (await settings.GetAsync(cancellationToken)).LocalDate(clock.GetUtcNow());
        var coverage = await db.Set<SubscriptionCoverage>().AsNoTracking().Where(c => ids.Contains(c.ShopId)).ToListAsync(cancellationToken);
        var withLoyalty = await LoyaltyPlansAsync(cancellationToken);
        return ids.ToDictionary(
            id => id,
            id => coverage.FirstOrDefault(c => c.ShopId == id)?.PlanInForce(today) is { } plan && withLoyalty.Contains(plan));
    }

    public async Task<IReadOnlyList<ShopId>> ShopsWithLoyaltyAsync(int limit, CancellationToken cancellationToken)
    {
        var withLoyalty = await LoyaltyPlansAsync(cancellationToken);
        if (withLoyalty.Count == 0)
        {
            return [];
        }

        var coverage = await db.Set<SubscriptionCoverage>().AsNoTracking().OrderByDescending(c => c.EndDate).ToListAsync(cancellationToken);
        return [.. coverage.Where(c => c.Periods.Any(p => withLoyalty.Contains(p.PlanId))).Select(c => c.ShopId).Take(limit)];
    }

    private async Task<HashSet<Guid>> LoyaltyPlansAsync(CancellationToken cancellationToken) =>
        [.. await db.Set<SubscriptionPlan>().AsNoTracking().Where(p => p.IncludesLoyalty).Select(p => p.Id.Value).ToListAsync(cancellationToken)];
}

/// <summary>
/// <see cref="IMarketingPlans"/> (D-155): the plan in force of a shop, read through the caller's scope (the jobs' system
/// scope).
/// </summary>
internal sealed class MarketingPlans(TrimmeDbContext db, IPlatformSettings settings, TimeProvider clock) : IMarketingPlans
{
    public async Task<MarketingTerms> ForShopAsync(ShopId shopId, CancellationToken cancellationToken)
    {
        var subscription = await db.Set<ShopSubscription>().AsNoTracking().Include(s => s.Periods).SingleOrDefaultAsync(s => s.ShopId == shopId, cancellationToken);
        var today = (await settings.GetAsync(cancellationToken)).LocalDate(clock.GetUtcNow());
        return await SubscriptionExtras.TermsAsync(db, subscription, today, cancellationToken);
    }

    public async Task<IReadOnlyList<WinBackShop>> WinBackShopsAsync(CancellationToken cancellationToken)
    {
        var plans = await db.Set<SubscriptionPlan>().AsNoTracking().Where(p => p.WinBackEnabled)
            .Select(p => new { Id = p.Id.Value, p.WinBackAfterDays }).ToListAsync(cancellationToken);
        if (plans.Count == 0)
        {
            return [];
        }

        var today = (await settings.GetAsync(cancellationToken)).LocalDate(clock.GetUtcNow());
        var coverage = await db.Set<SubscriptionCoverage>().AsNoTracking().Where(c => !c.IsSuspended && c.EndDate >= today).ToListAsync(cancellationToken);
        return
        [
            .. coverage
                .Select(c => (c.ShopId, Plan: c.PlanInForce(today) is { } planId ? plans.FirstOrDefault(p => p.Id == planId) : null))
                .Where(x => x.Plan is not null)
                .Select(x => new WinBackShop(x.ShopId, x.Plan!.WinBackAfterDays)),
        ];
    }
}
