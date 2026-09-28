using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Platform;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Subscriptions.Domain;

namespace Trimme.Modules.Subscriptions.Infrastructure;

/// <summary>
/// D-013/D-014 gate for new online bookings and discovery. It reads the shop directory (status and pause) and the
/// platform coverage row (D-078), so it needs no tenant scope and works for anonymous callers. It never touches
/// bookings: expiry and pause block only what is new, and walk-ins do not ask it.
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
            if (summaries.GetValueOrDefault(id) is not { Status: ShopStatus.Active } shop)
            {
                return Blocked("shop.not_active");
            }

            var subscription = platform.ExpiredSubscriptionEnforcement == SubscriptionEnforcement.None
                ? null
                : Subscription(coverage.GetValueOrDefault(id), today, platform.ExpiringSoonThresholdDays);
            if (subscription is not null)
            {
                return subscription;
            }

            // D-013: a paused shop takes no online bookings; whether it stays listed is a platform setting.
            return shop.OnlineBookingPaused
                ? new ShopBookability(false, !platform.HidePausedShopsFromDiscovery, "shop.paused")
                : new ShopBookability(true, true, null);
        });
    }

    /// <summary>The block a subscription status imposes, or <see langword="null"/> when it is in force.</summary>
    private static ShopBookability? Subscription(SubscriptionCoverage? covered, DateOnly today, int threshold) =>
        SubscriptionStatusCalculator.Calculate(covered?.IsSuspended ?? false, covered?.EndDate, today, threshold) switch
        {
            SubscriptionStatus.None => Blocked("subscription.none"),
            SubscriptionStatus.Expired => Blocked("subscription.expired"),
            SubscriptionStatus.Suspended => Blocked("subscription.suspended"),
            _ => null,
        };

    private static ShopBookability Blocked(string reason) => new(false, false, reason);
}
