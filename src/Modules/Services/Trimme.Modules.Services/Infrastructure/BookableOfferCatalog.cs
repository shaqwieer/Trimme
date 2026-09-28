using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Scheduling;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Services.Application;
using Trimme.Modules.Services.Domain;

namespace Trimme.Modules.Services.Infrastructure;

/// <summary>
/// <see cref="IBookableOfferCatalog"/> for the availability engine (D-082). Reads through the caller's data scope.
/// Only published items qualify: active, not archived and not hidden; a package also needs every item service to be
/// published (D-072). A package is online-bookable when every item service is.
/// </summary>
internal sealed class BookableOfferCatalog(TrimmeDbContext db) : IBookableOfferCatalog
{
    public async Task<BookableOffer?> FindAsync(ShopId shopId, Guid? serviceId, Guid? packageId, CancellationToken cancellationToken)
    {
        if (serviceId is { } rawServiceId && packageId is null)
        {
            var id = new ShopServiceId(rawServiceId);
            var service = await db.Set<ShopService>().AsNoTracking().SingleOrDefaultAsync(s => s.Id == id && s.ShopId == shopId, cancellationToken);
            if (service is not { IsPubliclyAvailable: true })
            {
                return null;
            }

            var professionals = await db.Set<ProfessionalServiceAssignment>().AsNoTracking()
                .Where(a => a.ShopId == shopId && a.ServiceId == id).Select(a => a.ProfessionalId).ToListAsync(cancellationToken);
            return new BookableOffer(service.Id.Value, IsPackage: false, service.DurationMinutes, service.OnlineBookable, professionals);
        }

        if (packageId is { } rawPackageId && serviceId is null)
        {
            var id = new ServicePackageId(rawPackageId);
            var package = await db.Set<ServicePackage>().AsNoTracking().Include(p => p.Items)
                .SingleOrDefaultAsync(p => p.Id == id && p.ShopId == shopId, cancellationToken);
            if (package is not { IsActive: true, IsArchived: false, Moderation: ModerationState.Visible })
            {
                return null;
            }

            var items = package.ExpandItems();
            var services = await CatalogMapping.ServicesOfAsync(db, [package], cancellationToken);
            if (!items.All(item => services.TryGetValue(item, out var s) && s.IsPubliclyAvailable))
            {
                return null;
            }

            // One professional does the whole package, so they must be assigned to every item service.
            var assignments = await db.Set<ProfessionalServiceAssignment>().AsNoTracking()
                .Where(a => a.ShopId == shopId && items.Contains(a.ServiceId)).ToListAsync(cancellationToken);
            var eligible = assignments.GroupBy(a => a.ProfessionalId)
                .Where(g => g.Select(a => a.ServiceId).Distinct().Count() == items.Count)
                .Select(g => g.Key).ToList();
            return new BookableOffer(package.Id.Value, IsPackage: true, package.DurationMinutes, items.All(item => services[item].OnlineBookable), eligible);
        }

        return null;
    }
}
