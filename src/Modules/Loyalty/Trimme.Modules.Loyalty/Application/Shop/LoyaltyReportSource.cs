using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Loyalty;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Loyalty.Domain;

namespace Trimme.Modules.Loyalty.Application.Shop;

/// <summary><see cref="ILoyaltyReportSource"/> (D-147), read through the shop's own data scope (another shop's rows are filtered out).</summary>
internal sealed class LoyaltyReportSource(TrimmeDbContext db) : ILoyaltyReportSource
{
    public async Task<LoyaltyActivity> ActivityAsync(ShopId shopId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var active = await db.Set<LoyaltyProgram>().AsNoTracking().AnyAsync(p => p.ShopId == shopId && p.IsActive, cancellationToken);
        var stamps = await db.Set<LoyaltyStamp>().AsNoTracking()
            .Where(s => s.ShopId == shopId && s.EarnedAt >= from && s.EarnedAt < to)
            .Select(s => new LoyaltyStampEntry(s.BookingId, s.EarnedAt))
            .ToListAsync(cancellationToken);
        var redeemed = await db.Set<LoyaltyReward>().AsNoTracking()
            .Where(r => r.ShopId == shopId && r.Status == LoyaltyRewardStatus.Redeemed && r.RedeemedAt >= from && r.RedeemedAt < to)
            .Select(r => new { r.Id, r.BookingId, r.RedeemedAt })
            .ToListAsync(cancellationToken);
        return new LoyaltyActivity(active, stamps, [.. redeemed.Select(r => new LoyaltyRedemptionEntry(r.Id.Value, r.BookingId, r.RedeemedAt!.Value))]);
    }

    public async Task<IReadOnlyDictionary<Guid, LoyaltyRewardTerms>> TermsAsync(IReadOnlyCollection<Guid> rewardIds, CancellationToken cancellationToken)
    {
        if (rewardIds.Count == 0)
        {
            return new Dictionary<Guid, LoyaltyRewardTerms>();
        }

        var ids = rewardIds.Select(id => new LoyaltyRewardId(id)).ToList();
        var rows = await db.Set<LoyaltyReward>().AsNoTracking().Where(r => ids.Contains(r.Id))
            .Select(r => new { r.Id, r.Kind, r.DiscountPercent }).ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.Id.Value, r => new LoyaltyRewardTerms(r.Kind == LoyaltyRewardKind.FreeService, r.DiscountPercent));
    }
}
