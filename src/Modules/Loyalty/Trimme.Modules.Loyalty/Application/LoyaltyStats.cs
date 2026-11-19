using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Loyalty.Domain;

namespace Trimme.Modules.Loyalty.Application;

/// <summary>Counts for one shop's loyalty, read through the caller's data scope (the admin's or the shop's own).</summary>
internal static class LoyaltyStats
{
    public static async Task<LoyaltyStatsResponse> ForShopAsync(TrimmeDbContext db, ShopId shopId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var settings = await db.Set<LoyaltySettings>().AsNoTracking().SingleOrDefaultAsync(s => s.ShopId == shopId, cancellationToken);
        var accounts = await db.Set<LoyaltyAccount>().AsNoTracking().Where(a => a.ShopId == shopId).ToListAsync(cancellationToken);
        var redemptions = await db.Set<LoyaltyRedemption>().AsNoTracking().Where(r => r.ShopId == shopId)
            .Select(r => new { r.MemberKey, r.Status, r.Points }).ToListAsync(cancellationToken);
        var held = redemptions.Where(r => r.Status == LoyaltyRedemptionStatus.Held).GroupBy(r => r.MemberKey).ToDictionary(g => g.Key, g => g.Sum(r => r.Points), StringComparer.Ordinal);
        var earned = await db.Set<LoyaltyEntry>().AsNoTracking().Where(e => e.ShopId == shopId && e.Kind == LoyaltyEntryKind.Earned)
            .SumAsync(e => (int?)e.Points, cancellationToken) ?? 0;
        var coupons = await db.Set<LoyaltyReward>().AsNoTracking().CountAsync(r => r.ShopId == shopId && r.Status == LoyaltyRewardStatus.Redeemed, cancellationToken);
        var ready = settings is null
            ? 0
            : accounts.Sum(a => LoyaltyBalance.Card(LoyaltyBalance.Available(a, settings, held.GetValueOrDefault(a.MemberKey), now), settings.PointsForReward).RewardsReady);
        return new LoyaltyStatsResponse(
            accounts.Count,
            ready,
            redemptions.Count(r => r.Status == LoyaltyRedemptionStatus.Held),
            redemptions.Count(r => r.Status == LoyaltyRedemptionStatus.Consumed) + coupons,
            earned);
    }
}
