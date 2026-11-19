using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Loyalty.Domain;

namespace Trimme.Modules.Loyalty.Application;

/// <summary>
/// Shared reads and the per-member lock of the points ledger (D-149), through the caller's data scope. Holds and the
/// consumer's changes for one member at one shop run one at a time: they take the same transaction lock first.
/// </summary>
internal sealed class LoyaltyLedger(TrimmeDbContext db)
{
    public Task LockAsync(ShopId shopId, string memberKey, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"trimme.loyalty:" + shopId.Value.ToString("N") + ":" + memberKey}, 0))", cancellationToken);

    public Task<LoyaltySettings?> SettingsAsync(ShopId shopId, CancellationToken cancellationToken) =>
        db.Set<LoyaltySettings>().AsNoTracking().SingleOrDefaultAsync(s => s.ShopId == shopId, cancellationToken);

    public Task<LoyaltyAccount?> AccountAsync(ShopId shopId, string memberKey, bool track, CancellationToken cancellationToken) =>
        (track ? db.Set<LoyaltyAccount>() : db.Set<LoyaltyAccount>().AsNoTracking())
            .SingleOrDefaultAsync(a => a.ShopId == shopId && a.MemberKey == memberKey, cancellationToken);

    /// <summary>The points held for upcoming bookings of the member at the shop, but the one of <paramref name="exceptBooking"/>.</summary>
    public async Task<int> HeldAsync(ShopId shopId, string memberKey, Guid? exceptBooking, CancellationToken cancellationToken) =>
        await db.Set<LoyaltyRedemption>().AsNoTracking()
            .Where(r => r.ShopId == shopId && r.MemberKey == memberKey && r.Status == LoyaltyRedemptionStatus.Held && r.BookingId != exceptBooking)
            .SumAsync(r => (int?)r.Points, cancellationToken) ?? 0;
}
