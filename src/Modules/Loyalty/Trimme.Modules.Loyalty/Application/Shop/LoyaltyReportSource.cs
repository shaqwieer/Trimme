using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Loyalty;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Loyalty.Domain;

namespace Trimme.Modules.Loyalty.Application.Shop;

/// <summary>
/// <see cref="ILoyaltyReportSource"/> (D-147, D-149), read through the shop's own data scope (another shop's rows are filtered
/// out): one stamp entry per point earned, the rewards used at visits (points rewards and D-132 coupons), and the terms of
/// D-132 coupons (a points reward is already taken off the booking's amount due).
/// </summary>
internal sealed class LoyaltyReportSource(TrimmeDbContext db, ILoyaltyAddOnStatus addOn) : ILoyaltyReportSource
{
    public async Task<LoyaltyActivity> ActivityAsync(ShopId shopId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var earned = await db.Set<LoyaltyEntry>().AsNoTracking()
            .Where(e => e.ShopId == shopId && e.Kind == LoyaltyEntryKind.Earned && e.BookingId != null && e.At >= from && e.At < to)
            .Select(e => new { e.BookingId, e.At, e.Points })
            .ToListAsync(cancellationToken);
        var used = await db.Set<LoyaltyRedemption>().AsNoTracking()
            .Where(r => r.ShopId == shopId && r.Status == LoyaltyRedemptionStatus.Consumed && r.ClosedAt >= from && r.ClosedAt < to)
            .Select(r => new { r.Id, r.BookingId, r.ClosedAt })
            .ToListAsync(cancellationToken);
        var coupons = await db.Set<LoyaltyReward>().AsNoTracking()
            .Where(r => r.ShopId == shopId && r.Status == LoyaltyRewardStatus.Redeemed && r.RedeemedAt >= from && r.RedeemedAt < to)
            .Select(r => new { r.Id, r.BookingId, r.RedeemedAt })
            .ToListAsync(cancellationToken);
        return new LoyaltyActivity(
            await addOn.IsActiveAsync(shopId, cancellationToken),
            [.. earned.SelectMany(e => Enumerable.Repeat(new LoyaltyStampEntry(e.BookingId!.Value, e.At), Math.Max(e.Points, 0)))],
            [
                .. used.Select(r => new LoyaltyRedemptionEntry(r.Id.Value, r.BookingId, r.ClosedAt!.Value)),
                .. coupons.Select(r => new LoyaltyRedemptionEntry(r.Id.Value, r.BookingId, r.RedeemedAt!.Value)),
            ]);
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
