using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Shops.Domain;

namespace Trimme.Modules.Shops.Application;

/// <summary>
/// The signed-in shop user's own shop (<c>GET /api/v1/shop/me</c>). Shop-facing contract: it must never carry customer
/// contact data (R-NEG-04). The status is reported even while suspended, so the dashboard can explain it.
/// </summary>
/// <summary>
/// The signed-in shop user's shop for the dashboard's frame. <c>ShortNameAr/En</c> (D-152, the client's 3-8) is the
/// shop's short name for the welcome line («أهلاً محمد، نورتنا» beside «الأصالة»); null shows the full name.
/// </summary>
public sealed record ShopProfileResponse(
    Guid Id, string Slug, string NameAr, string NameEn, string Status, string TimeZone, string? ShortNameAr = null, string? ShortNameEn = null);

/// <summary>The shop comes from the caller's claims (resolved by the endpoint), never from the request.</summary>
internal sealed record GetMyShopQuery(ShopId ShopId) : IQuery<ShopProfileResponse?>;

internal sealed class GetMyShopHandler(TrimmeDbContext db) : IQueryHandler<GetMyShopQuery, ShopProfileResponse?>
{
    public async Task<ShopProfileResponse?> Handle(GetMyShopQuery query, CancellationToken cancellationToken) =>
        await db.Set<Shop>().AsNoTracking()
            .Where(s => s.Id == query.ShopId)
            .Select(s => new ShopProfileResponse(s.Id.Value, s.Slug, s.NameAr, s.NameEn, s.Status.ToString(), s.TimeZone, s.ShortNameAr, s.ShortNameEn))
            .SingleOrDefaultAsync(cancellationToken);
}
