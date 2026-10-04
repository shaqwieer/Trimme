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
/// published (D-072). A package is online-bookable when every item service is; so are several services booked together.
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
            return new BookableOffer(
                service.Id.Value, IsPackage: false, service.DurationMinutes, service.OnlineBookable, professionals,
                service.NameAr, service.NameEn, service.Price, service.Currency, []);
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
            return new BookableOffer(
                package.Id.Value, IsPackage: true, package.DurationMinutes, items.All(item => services[item].OnlineBookable), eligible,
                package.NameAr, package.NameEn, package.Price, package.Currency,
                [.. items.Select(item => new BookableOfferItem(item.Value, services[item].NameAr, services[item].NameEn))]);
        }

        return null;
    }

    public async Task<BookableOffer?> FindServicesAsync(ShopId shopId, IReadOnlyList<Guid> serviceIds, CancellationToken cancellationToken)
    {
        if (serviceIds.Count <= 1)
        {
            return serviceIds.Count == 1 ? await FindAsync(shopId, serviceIds[0], null, cancellationToken) : null;
        }

        var ids = serviceIds.Select(id => new ShopServiceId(id)).Distinct().ToList();
        var found = await db.Set<ShopService>().AsNoTracking()
            .Where(s => s.ShopId == shopId && ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, cancellationToken);
        if (ids.Count != serviceIds.Count || !ids.All(id => found.TryGetValue(id, out var s) && s.IsPubliclyAvailable))
        {
            return null;
        }

        var services = ids.Select(id => found[id]).ToList();
        var currency = services[0].Currency;
        if (services.Any(s => s.Currency != currency))
        {
            return null;
        }

        // One professional does every service back to back, so they must be assigned to all of them.
        var assignments = await db.Set<ProfessionalServiceAssignment>().AsNoTracking()
            .Where(a => a.ShopId == shopId && ids.Contains(a.ServiceId)).ToListAsync(cancellationToken);
        var eligible = assignments.GroupBy(a => a.ProfessionalId)
            .Where(g => g.Select(a => a.ServiceId).Distinct().Count() == ids.Count)
            .Select(g => g.Key).ToList();
        var nameEn = services.All(s => !string.IsNullOrWhiteSpace(s.NameEn)) ? Joined(services.Select(s => s.NameEn!)) : null;
        return new BookableOffer(
            services[0].Id.Value, IsPackage: false, services.Sum(s => s.DurationMinutes), services.All(s => s.OnlineBookable), eligible,
            Joined(services.Select(s => s.NameAr)), nameEn, services.Sum(s => s.Price), currency,
            [.. services.Select(s => new BookableOfferItem(s.Id.Value, s.NameAr, s.NameEn))]);
    }

    /// <summary>The services' names as one booked item name, shortened to the snapshot's length.</summary>
    private static string Joined(IEnumerable<string> names)
    {
        var joined = string.Join(" + ", names);
        return joined.Length <= CatalogRules.MaxNameLength ? joined : string.Concat(joined.AsSpan(0, CatalogRules.MaxNameLength - 1), "…");
    }
}
