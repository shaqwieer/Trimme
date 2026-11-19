using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Reporting;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Services.Domain;

namespace Trimme.Modules.Services.Infrastructure;

/// <summary>
/// <see cref="IBookedOfferLookup"/>: the names and shops of booked services and packages, for the overview's most booked
/// list (D-154). Reads through the caller's scope (the overview opens the admin scope, so every shop's rows are read).
/// </summary>
internal sealed class BookedOfferLookup(TrimmeDbContext db) : IBookedOfferLookup
{
    public async Task<IReadOnlyDictionary<Guid, BookedOfferRef>> OffersAsync(
        IReadOnlyCollection<Guid> serviceIds, IReadOnlyCollection<Guid> packageIds, CancellationToken cancellationToken)
    {
        var offers = new Dictionary<Guid, BookedOfferRef>();
        if (serviceIds.Count > 0)
        {
            var ids = serviceIds.Distinct().Select(id => new ShopServiceId(id)).ToArray();
            var services = await db.Set<ShopService>().AsNoTracking().Where(s => ids.Contains(s.Id))
                .Select(s => new { s.Id, s.ShopId, s.NameAr, s.NameEn })
                .ToListAsync(cancellationToken);
            foreach (var s in services)
            {
                offers[s.Id.Value] = new BookedOfferRef(s.Id.Value, s.ShopId, s.NameAr, s.NameEn);
            }
        }

        if (packageIds.Count > 0)
        {
            var ids = packageIds.Distinct().Select(id => new ServicePackageId(id)).ToArray();
            var packages = await db.Set<ServicePackage>().AsNoTracking().Where(p => ids.Contains(p.Id))
                .Select(p => new { p.Id, p.ShopId, p.NameAr, p.NameEn })
                .ToListAsync(cancellationToken);
            foreach (var p in packages)
            {
                offers[p.Id.Value] = new BookedOfferRef(p.Id.Value, p.ShopId, p.NameAr, p.NameEn);
            }
        }

        return offers;
    }
}
