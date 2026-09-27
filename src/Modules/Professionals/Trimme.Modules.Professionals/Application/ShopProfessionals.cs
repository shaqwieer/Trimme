using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Media;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Professionals.Domain;

namespace Trimme.Modules.Professionals.Application;

/// <summary>
/// A professional as the shop sees it (read-only: the shop cannot create, edit or move professionals, spec §7).
/// Shop-facing contract: no contact data of any kind.
/// </summary>
public sealed record ShopProfessionalResponse(
    Guid Id,
    string Slug,
    string NameAr,
    string NameEn,
    string? SpecialtyAr,
    string? SpecialtyEn,
    string? AvatarUrl,
    ProfessionalStatus Status);

internal sealed record ListShopProfessionalsQuery : IQuery<IReadOnlyList<ShopProfessionalResponse>>;

/// <summary>The tenant filter scopes the query to the caller's shop; a suspended shop (no tenant) gets an empty list.</summary>
internal sealed class ListShopProfessionalsHandler(TrimmeDbContext db) : IQueryHandler<ListShopProfessionalsQuery, IReadOnlyList<ShopProfessionalResponse>>
{
    /// <summary>A shop has a handful of professionals; the cap only bounds the query.</summary>
    private const int MaxProfessionals = 200;

    public async Task<IReadOnlyList<ShopProfessionalResponse>> Handle(ListShopProfessionalsQuery query, CancellationToken cancellationToken)
    {
        var professionals = await db.Set<Professional>().AsNoTracking()
            .OrderBy(p => p.Status).ThenBy(p => p.NameAr)
            .Take(MaxProfessionals)
            .ToListAsync(cancellationToken);
        return
        [
            .. professionals.Select(p => new ShopProfessionalResponse(
                p.Id.Value, p.Slug, p.NameAr, p.NameEn, p.SpecialtyAr, p.SpecialtyEn, MediaRules.Url(p.AvatarMediaId), p.Status)),
        ];
    }
}
