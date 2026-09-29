using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Services.Domain;

namespace Trimme.Modules.Services.Application.Public;

/// <summary>
/// A published service: active, not archived, not hidden by the platform. <c>NameEn</c> may be null (D-070).
/// <c>ProfessionalIds</c> are the professionals assigned to it (the booking wizard shows the active ones among them).
/// </summary>
public sealed record PublicServiceResponse(
    Guid Id,
    string NameAr,
    string? NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    Guid? CategoryId,
    decimal Price,
    string Currency,
    int DurationMinutes,
    bool OnlineBookable,
    IReadOnlyList<Guid> ProfessionalIds);

public sealed record PublicPackageItemResponse(Guid ServiceId, string NameAr, string? NameEn);

/// <summary>
/// A published package: itself and every item service are available. <c>ProfessionalIds</c> are the professionals
/// assigned to every one of its services (who can perform the whole package).
/// </summary>
public sealed record PublicPackageResponse(
    Guid Id,
    string NameAr,
    string? NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    decimal Price,
    string Currency,
    int DurationMinutes,
    IReadOnlyList<PublicPackageItemResponse> Items,
    IReadOnlyList<Guid> ProfessionalIds);

internal sealed record ListPublicCategoriesQuery : IQuery<IReadOnlyList<ServiceCategoryResponse>>;

internal sealed record ListPublicServicesQuery(string ShopSlug) : IQuery<IReadOnlyList<PublicServiceResponse>?>;

internal sealed record ListPublicPackagesQuery(string ShopSlug) : IQuery<IReadOnlyList<PublicPackageResponse>?>;

internal sealed class ListPublicCategoriesHandler(TrimmeDbContext db) : IQueryHandler<ListPublicCategoriesQuery, IReadOnlyList<ServiceCategoryResponse>>
{
    public async Task<IReadOnlyList<ServiceCategoryResponse>> Handle(ListPublicCategoriesQuery query, CancellationToken cancellationToken)
    {
        var categories = await db.Set<ServiceCategory>().AsNoTracking()
            .Where(c => c.IsActive).OrderBy(c => c.DisplayOrder).ThenBy(c => c.NameAr).ToListAsync(cancellationToken);
        return [.. categories.Select(CatalogMapping.ToResponse)];
    }
}

/// <summary>Resolves an active shop by slug inside the public scope (D-066), whoever the caller is.</summary>
internal static class PublishedShop
{
    public static async Task<ShopId?> FindAsync(IPublicDataScope scope, IShopDirectory shops, string slug, CancellationToken cancellationToken)
    {
        using (scope.Begin(shopId: null))
        {
            var shop = await shops.FindBySlugAsync(slug, cancellationToken);
            return shop is { Status: ShopStatus.Active } ? shop.Id : null;
        }
    }

    /// <summary>The shop's service assignments, by service (read inside the shop's public scope).</summary>
    public static async Task<ILookup<ShopServiceId, ProfessionalId>> AssignmentsAsync(TrimmeDbContext db, ShopId shopId, CancellationToken cancellationToken) =>
        (await db.Set<ProfessionalServiceAssignment>().AsNoTracking().Where(a => a.ShopId == shopId).ToListAsync(cancellationToken))
            .ToLookup(a => a.ServiceId, a => a.ProfessionalId);

    /// <summary>Professionals assigned to every one of the services, in id order.</summary>
    public static IReadOnlyList<Guid> Eligible(ILookup<ShopServiceId, ProfessionalId> assigned, IReadOnlyCollection<ShopServiceId> services)
    {
        IEnumerable<ProfessionalId>? eligible = null;
        foreach (var service in services.Distinct())
        {
            eligible = eligible is null ? assigned[service] : eligible.Intersect(assigned[service]);
        }

        return [.. (eligible ?? []).Distinct().Select(p => p.Value).Order()];
    }
}

internal sealed class ListPublicServicesHandler(TrimmeDbContext db, IPublicDataScope scope, IShopDirectory shops)
    : IQueryHandler<ListPublicServicesQuery, IReadOnlyList<PublicServiceResponse>?>
{
    public async Task<IReadOnlyList<PublicServiceResponse>?> Handle(ListPublicServicesQuery query, CancellationToken cancellationToken)
    {
        if (await PublishedShop.FindAsync(scope, shops, query.ShopSlug, cancellationToken) is not { } shopId)
        {
            return null;
        }

        using var _ = scope.Begin(shopId);
        var services = await db.Set<ShopService>().AsNoTracking()
            .Where(s => s.ShopId == shopId && s.IsActive && !s.IsArchived && s.Moderation == ModerationState.Visible)
            .OrderBy(s => s.DisplayOrder).ToListAsync(cancellationToken);
        var assigned = await PublishedShop.AssignmentsAsync(db, shopId, cancellationToken);
        return
        [
            .. services.Select(s => new PublicServiceResponse(
                s.Id.Value, s.NameAr, s.NameEn, s.DescriptionAr, s.DescriptionEn, s.CategoryId?.Value, s.Price, s.Currency, s.DurationMinutes, s.OnlineBookable,
                PublishedShop.Eligible(assigned, [s.Id]))),
        ];
    }
}

internal sealed class ListPublicPackagesHandler(TrimmeDbContext db, IPublicDataScope scope, IShopDirectory shops)
    : IQueryHandler<ListPublicPackagesQuery, IReadOnlyList<PublicPackageResponse>?>
{
    public async Task<IReadOnlyList<PublicPackageResponse>?> Handle(ListPublicPackagesQuery query, CancellationToken cancellationToken)
    {
        if (await PublishedShop.FindAsync(scope, shops, query.ShopSlug, cancellationToken) is not { } shopId)
        {
            return null;
        }

        using var _ = scope.Begin(shopId);
        var packages = await db.Set<ServicePackage>().AsNoTracking().Include(p => p.Items)
            .Where(p => p.ShopId == shopId && p.IsActive && !p.IsArchived && p.Moderation == ModerationState.Visible)
            .OrderBy(p => p.DisplayOrder).ToListAsync(cancellationToken);
        var services = await CatalogMapping.ServicesOfAsync(db, packages, cancellationToken);
        var assigned = await PublishedShop.AssignmentsAsync(db, shopId, cancellationToken);

        // A package whose item service is off, archived or hidden is not offered (D-072).
        return
        [
            .. packages
                .Where(p => p.ExpandItems().All(id => services.TryGetValue(id, out var s) && s.IsPubliclyAvailable))
                .Select(p => new PublicPackageResponse(
                    p.Id.Value, p.NameAr, p.NameEn, p.DescriptionAr, p.DescriptionEn, p.Price, p.Currency, p.DurationMinutes,
                    [.. p.ExpandItems().Select(id => new PublicPackageItemResponse(id.Value, services[id].NameAr, services[id].NameEn))],
                    PublishedShop.Eligible(assigned, [.. p.ExpandItems()]))),
        ];
    }
}
