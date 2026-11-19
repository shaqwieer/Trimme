using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Loyalty;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Loyalty.Domain;

namespace Trimme.Modules.Loyalty.Application.Public;

/// <summary>A published shop's loyalty offer (D-149), shown on its page: whether it runs now and its rules. No customer data.</summary>
public sealed record PublicLoyaltyResponse(bool Active, LoyaltyRulesResponse? Rules);

internal sealed record GetPublicLoyaltyQuery(string ShopSlug) : IQuery<PublicLoyaltyResponse?>;

/// <summary>Reads inside the public scope of that one shop (D-066); <see langword="null"/> when the shop is not published.</summary>
internal sealed class GetPublicLoyaltyHandler(TrimmeDbContext db, IPublicDataScope scope, IShopDirectory shops, ILoyaltyAddOnStatus addOn)
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
        var settings = await db.Set<LoyaltySettings>().AsNoTracking().SingleOrDefaultAsync(s => s.ShopId == shop.Id, cancellationToken);
        var active = settings is not null && await addOn.IsActiveAsync(shop.Id, cancellationToken);
        return new PublicLoyaltyResponse(active, active ? LoyaltyMapping.Rules(settings!) : null);
    }
}
