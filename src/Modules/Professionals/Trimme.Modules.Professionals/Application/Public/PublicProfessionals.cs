using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Media;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Professionals.Domain;

namespace Trimme.Modules.Professionals.Application.Public;

/// <summary>A professional on the public shop page. Never carries a phone or WhatsApp number (R-PRO-02).</summary>
public sealed record PublicProfessionalResponse(
    Guid Id,
    string Slug,
    string NameAr,
    string NameEn,
    string? SpecialtyAr,
    string? SpecialtyEn,
    string? BioAr,
    string? BioEn,
    string? AvatarUrl);

internal sealed record ListPublicProfessionalsQuery(string ShopSlug) : IQuery<IReadOnlyList<PublicProfessionalResponse>?>;

/// <summary>
/// Active professionals of an active shop, read inside <see cref="IPublicDataScope"/> bound to that one shop.
/// Returns <see langword="null"/> when the shop is not published.
/// </summary>
internal sealed class ListPublicProfessionalsHandler(TrimmeDbContext db, IPublicDataScope scope, IShopDirectory shops)
    : IQueryHandler<ListPublicProfessionalsQuery, IReadOnlyList<PublicProfessionalResponse>?>
{
    private const int MaxProfessionals = 200;

    public async Task<IReadOnlyList<PublicProfessionalResponse>?> Handle(ListPublicProfessionalsQuery query, CancellationToken cancellationToken)
    {
        ShopSummary? shop;
        using (scope.Begin(shopId: null))
        {
            shop = await shops.FindBySlugAsync(query.ShopSlug, cancellationToken);
        }

        if (shop is not { Status: ShopStatus.Active })
        {
            return null;
        }

        using var _ = scope.Begin(shop.Id);
        var professionals = await db.Set<Professional>().AsNoTracking()
            .Where(p => p.ShopId == shop.Id && p.Status == ProfessionalStatus.Active)
            .OrderBy(p => p.NameAr)
            .Take(MaxProfessionals)
            .ToListAsync(cancellationToken);
        return
        [
            .. professionals.Select(p => new PublicProfessionalResponse(
                p.Id.Value, p.Slug, p.NameAr, p.NameEn, p.SpecialtyAr, p.SpecialtyEn, p.BioAr, p.BioEn, MediaRules.Url(p.AvatarMediaId))),
        ];
    }
}
