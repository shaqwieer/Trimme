using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Services.Domain;

namespace Trimme.Modules.Services.Application.Public;

/// <summary>A published service: active, not archived, not hidden by the platform. <c>NameEn</c> may be null (D-070).</summary>
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
    bool OnlineBookable);

public sealed record PublicPackageItemResponse(Guid ServiceId, string NameAr, string? NameEn);

/// <summary>A published package: itself and every item service are available.</summary>
public sealed record PublicPackageResponse(
    Guid Id,
    string NameAr,
    string? NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    decimal Price,
    string Currency,
    int DurationMinutes,
    IReadOnlyList<PublicPackageItemResponse> Items);

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
        return
        [
            .. services.Select(s => new PublicServiceResponse(
                s.Id.Value, s.NameAr, s.NameEn, s.DescriptionAr, s.DescriptionEn, s.CategoryId?.Value, s.Price, s.Currency, s.DurationMinutes, s.OnlineBookable)),
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

        // A package whose item service is off, archived or hidden is not offered (D-072).
        return
        [
            .. packages
                .Where(p => p.ExpandItems().All(id => services.TryGetValue(id, out var s) && s.IsPubliclyAvailable))
                .Select(p => new PublicPackageResponse(
                    p.Id.Value, p.NameAr, p.NameEn, p.DescriptionAr, p.DescriptionEn, p.Price, p.Currency, p.DurationMinutes,
                    [.. p.ExpandItems().Select(id => new PublicPackageItemResponse(id.Value, services[id].NameAr, services[id].NameEn))])),
        ];
    }
}
