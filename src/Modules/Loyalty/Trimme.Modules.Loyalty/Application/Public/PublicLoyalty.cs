using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Loyalty.Domain;

namespace Trimme.Modules.Loyalty.Application.Public;

/// <summary>A published shop's loyalty offer, shown on its page; <c>Program</c> is null when it has none running.</summary>
public sealed record PublicLoyaltyResponse(LoyaltyProgramResponse? Program);

internal sealed record GetPublicLoyaltyQuery(string ShopSlug) : IQuery<PublicLoyaltyResponse?>;

/// <summary>Reads inside the public scope of that one shop (D-066); <see langword="null"/> when the shop is not published.</summary>
internal sealed class GetPublicLoyaltyHandler(TrimmeDbContext db, IPublicDataScope scope, IShopDirectory shops)
    : IQueryHandler<GetPublicLoyaltyQuery, PublicLoyaltyResponse?>
{
    public async Task<PublicLoyaltyResponse?> Handle(GetPublicLoyaltyQuery query, CancellationToken cancellationToken)
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
        var program = await db.Set<LoyaltyProgram>().AsNoTracking().SingleOrDefaultAsync(p => p.ShopId == shop.Id && p.IsActive, cancellationToken);
        return new PublicLoyaltyResponse(program is null ? null : LoyaltyMapping.Program(program));
    }
}
