using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Platform;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Subscriptions.Domain;

namespace Trimme.Modules.Subscriptions.Infrastructure;

/// <summary>
/// D-014 gate for new online bookings and discovery. It reads the shop directory and the platform coverage row
/// (D-078), so it needs no tenant scope and works for anonymous callers. It never touches bookings: expiry blocks only
/// what is new, and walk-ins do not ask it.
/// </summary>
internal sealed class ShopBookabilityService(TrimmeDbContext db, IShopDirectory shops, IPlatformSettings settings, TimeProvider clock) : IShopBookability
{
    public async Task<ShopBookability> GetAsync(ShopId shopId, CancellationToken cancellationToken) =>
        (await GetManyAsync([shopId], cancellationToken))[shopId];

    public async Task<IReadOnlyDictionary<ShopId, ShopBookability>> GetManyAsync(IReadOnlyCollection<ShopId> shopIds, CancellationToken cancellationToken)
    {
        var ids = shopIds.Distinct().ToArray();
        var summaries = await shops.FindManyAsync(ids, cancellationToken);
        var coverage = await db.Set<SubscriptionCoverage>().AsNoTracking().Where(c => ids.Contains(c.ShopId))
            .ToDictionaryAsync(c => c.ShopId, cancellationToken);
        var platform = await settings.GetAsync(cancellationToken);
        var today = platform.LocalDate(clock.GetUtcNow());

        return ids.ToDictionary(id => id, id =>
        {
            if (summaries.GetValueOrDefault(id) is not { Status: ShopStatus.Active })
            {
                return Blocked("shop.not_active");
            }

            if (platform.ExpiredSubscriptionEnforcement == SubscriptionEnforcement.None)
            {
                return new ShopBookability(true, true, null);
            }

            var covered = coverage.GetValueOrDefault(id);
            return SubscriptionStatusCalculator.Calculate(covered?.IsSuspended ?? false, covered?.EndDate, today, platform.ExpiringSoonThresholdDays) switch
            {
                SubscriptionStatus.None => Blocked("subscription.none"),
                SubscriptionStatus.Expired => Blocked("subscription.expired"),
                SubscriptionStatus.Suspended => Blocked("subscription.suspended"),
                _ => new ShopBookability(true, true, null),
            };
        });
    }

    private static ShopBookability Blocked(string reason) => new(false, false, reason);
}
