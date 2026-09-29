using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Reporting;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Shops.Domain;

namespace Trimme.Modules.Shops.Infrastructure;

/// <summary><see cref="IShopStatistics"/>: shops per lifecycle status (the shops table is the platform directory).</summary>
internal sealed class ShopStatistics(TrimmeDbContext db) : IShopStatistics
{
    public async Task<ShopCounts> CountsAsync(CancellationToken cancellationToken)
    {
        var counts = await db.Set<Shop>().AsNoTracking()
            .GroupBy(s => s.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        return new ShopCounts(
            counts.Values.Sum(), counts.GetValueOrDefault(ShopStatus.Active), counts.GetValueOrDefault(ShopStatus.Draft), counts.GetValueOrDefault(ShopStatus.Suspended));
    }
}
