using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Media;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Shops.Domain;

namespace Trimme.Modules.Shops.Application.Public;

/// <summary>Where a customer finds the shop: the confirmed point and the address, without who confirmed it.</summary>
public sealed record PublicShopLocationResponse(
    double Latitude,
    double Longitude,
    string? AddressLine,
    string? District,
    string? City,
    string? FormattedAddress);

/// <summary>
/// A published shop's basic profile (<c>GET /public/shops/{slug}</c>). Only active shops are published. The phone is the
/// shop's own business number; professional and customer numbers are never part of a public contract (R-PRO-02).
/// </summary>
public sealed record PublicShopResponse(
    Guid Id,
    string Slug,
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    ShopCategory Category,
    string? PublicPhone,
    IReadOnlyList<ShopAmenity> Amenities,
    bool IsVerified,
    string? LogoUrl,
    string? CoverUrl,
    IReadOnlyList<string> GalleryUrls,
    PublicShopLocationResponse? Location);

internal sealed record GetPublicShopQuery(string Slug) : IQuery<PublicShopResponse?>;

/// <summary>
/// Public read inside <see cref="IPublicDataScope"/>, so a signed-in shop user sees another shop's public page exactly
/// as an anonymous visitor does, and nothing unpublished.
/// </summary>
internal sealed class GetPublicShopHandler(TrimmeDbContext db, IPublicDataScope scope) : IQueryHandler<GetPublicShopQuery, PublicShopResponse?>
{
    public async Task<PublicShopResponse?> Handle(GetPublicShopQuery query, CancellationToken cancellationToken)
    {
        var slug = query.Slug.Trim().ToLowerInvariant();
        using var _ = scope.Begin(shopId: null);
        var shop = await db.Set<Shop>().AsNoTracking()
            .SingleOrDefaultAsync(s => s.Slug == slug && s.Status == ShopStatus.Active, cancellationToken);
        if (shop is null)
        {
            return null;
        }

        return new PublicShopResponse(
            shop.Id.Value,
            shop.Slug,
            shop.NameAr,
            shop.NameEn,
            shop.DescriptionAr,
            shop.DescriptionEn,
            shop.Category,
            shop.PublicPhone,
            shop.Amenities,
            shop.IsVerified,
            MediaRules.Url(shop.LogoMediaId),
            MediaRules.Url(shop.CoverMediaId),
            [.. ShopInputMapping.Gallery(shop).Select(image => image.Url)],
            shop.Location is { } location
                ? new PublicShopLocationResponse(location.Latitude, location.Longitude, location.AddressLine, location.District, location.City, location.FormattedAddress)
                : null);
    }
}
