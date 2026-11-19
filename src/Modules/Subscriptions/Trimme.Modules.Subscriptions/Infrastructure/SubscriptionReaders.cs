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
/// <see cref="ILoyaltyAddOnStatus"/> (D-149) from the coverage read model, which any caller may read (D-078): the add-on is
/// in force from its first to its last day, on the platform calendar like the subscription (D-077).
/// </summary>
internal sealed class LoyaltyAddOnStatus(TrimmeDbContext db, IPlatformSettings settings, TimeProvider clock) : ILoyaltyAddOnStatus
{
    public async Task<bool> IsActiveAsync(ShopId shopId, CancellationToken cancellationToken) =>
        (await ActiveManyAsync([shopId], cancellationToken)).GetValueOrDefault(shopId);

    public async Task<IReadOnlyDictionary<ShopId, bool>> ActiveManyAsync(IReadOnlyCollection<ShopId> shopIds, CancellationToken cancellationToken)
    {
        var ids = shopIds.Distinct().ToArray();
        var today = (await settings.GetAsync(cancellationToken)).LocalDate(clock.GetUtcNow());
        var rows = await db.Set<SubscriptionCoverage>().AsNoTracking()
            .Where(c => ids.Contains(c.ShopId))
            .Select(c => new { c.ShopId, c.LoyaltyAddOnStart, c.LoyaltyAddOnEnd })
            .ToListAsync(cancellationToken);
        return ids.ToDictionary(id => id, id => rows.FirstOrDefault(r => r.ShopId == id) is { } r && LoyaltyAddOnRules.IsActive(r.LoyaltyAddOnStart, r.LoyaltyAddOnEnd, today));
    }

    public async Task<IReadOnlyList<ShopId>> ShopsWithAddOnAsync(int limit, CancellationToken cancellationToken) =>
        await db.Set<SubscriptionCoverage>().AsNoTracking()
            .Where(c => c.LoyaltyAddOnStart != null)
            .OrderByDescending(c => c.LoyaltyAddOnEnd)
            .Select(c => c.ShopId)
            .Take(limit)
            .ToListAsync(cancellationToken);
}

/// <summary>
/// <see cref="IWhatsAppAllowance"/> (D-149): the allowance of the plan in force for the shop, read through the caller's
/// scope (the dispatch job's system scope).
/// </summary>
internal sealed class WhatsAppAllowance(TrimmeDbContext db, IPlatformSettings settings, TimeProvider clock) : IWhatsAppAllowance
{
    public async Task<int?> MonthlyCustomerMessagesAsync(ShopId shopId, CancellationToken cancellationToken)
    {
        var subscription = await db.Set<ShopSubscription>().AsNoTracking().Include(s => s.Periods).SingleOrDefaultAsync(s => s.ShopId == shopId, cancellationToken);
        var today = (await settings.GetAsync(cancellationToken)).LocalDate(clock.GetUtcNow());
        return await SubscriptionExtras.AllowanceAsync(db, subscription, today, cancellationToken);
    }
}
