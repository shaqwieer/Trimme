using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Reporting;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Services.Domain;

namespace Trimme.Modules.Services.Infrastructure;

/// <summary>
/// <see cref="IServiceCategoryLookup"/>: the platform category of shop services, for reporting by category (services
/// are shop-owned, so the overview never groups by a global service, DV-S02). Reads through the caller's scope.
/// </summary>
internal sealed class ServiceCategoryLookup(TrimmeDbContext db) : IServiceCategoryLookup
{
    public async Task<IReadOnlyDictionary<Guid, ServiceCategoryRef>> CategoriesOfAsync(IReadOnlyCollection<Guid> serviceIds, CancellationToken cancellationToken)
    {
        if (serviceIds.Count == 0)
        {
            return new Dictionary<Guid, ServiceCategoryRef>();
        }

        var ids = serviceIds.Distinct().Select(id => new ShopServiceId(id)).ToArray();
        var rows = await (from service in db.Set<ShopService>().AsNoTracking()
                          join category in db.Set<ServiceCategory>().AsNoTracking() on service.CategoryId equals (ServiceCategoryId?)category.Id
                          where ids.Contains(service.Id)
                          select new { service.Id, CategoryId = category.Id, category.NameAr, category.NameEn })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.Id.Value, r => new ServiceCategoryRef(r.CategoryId.Value, r.NameAr, r.NameEn));
    }
}
