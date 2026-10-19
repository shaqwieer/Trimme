using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Loyalty.Domain;

namespace Trimme.Modules.Loyalty.Application;

/// <summary>Counts for one shop's programme, read through the caller's data scope (the admin's or the shop's own).</summary>
internal static class LoyaltyStats
{
    public static async Task<LoyaltyStatsResponse> ForShopAsync(TrimmeDbContext db, ShopId shopId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var stamps = db.Set<LoyaltyStamp>().AsNoTracking().Where(s => s.ShopId == shopId);
        var members = await stamps.Select(s => s.CustomerId).Distinct().CountAsync(cancellationToken);
        var open = await stamps.CountAsync(s => s.RewardId == null, cancellationToken);
        var rewards = await db.Set<LoyaltyReward>().AsNoTracking().Where(r => r.ShopId == shopId)
            .Select(r => new { r.Status, r.ExpiresAt })
            .ToListAsync(cancellationToken);
        return new LoyaltyStatsResponse(
            members,
            open,
            rewards.Count,
            rewards.Count(r => r.Status == LoyaltyRewardStatus.Held || (r.Status == LoyaltyRewardStatus.Available && (r.ExpiresAt is null || now < r.ExpiresAt))),
            rewards.Count(r => r.Status == LoyaltyRewardStatus.Redeemed));
    }
}
