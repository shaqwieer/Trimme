using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Loyalty.Domain;

namespace Trimme.Modules.Loyalty.Application.Shop;

/// <summary>The shop's own programme (read-only: the platform sets it, D-132) and how it is used; null programme when none.</summary>
public sealed record ShopLoyaltyResponse(LoyaltyProgramResponse? Program, LoyaltyStatsResponse Stats);

internal sealed record GetShopLoyaltyQuery : IQuery<ShopLoyaltyResponse?>;

/// <summary>The shop comes from the caller's claims (tenant filter); <see langword="null"/> for a suspended shop.</summary>
internal sealed class GetShopLoyaltyHandler(TrimmeDbContext db, ICurrentTenant tenant, TimeProvider clock) : IQueryHandler<GetShopLoyaltyQuery, ShopLoyaltyResponse?>
{
    public async Task<ShopLoyaltyResponse?> Handle(GetShopLoyaltyQuery query, CancellationToken cancellationToken)
    {
        if (tenant.ShopId is not { } shopId)
        {
            return null;
        }

        var program = await db.Set<LoyaltyProgram>().AsNoTracking().SingleOrDefaultAsync(p => p.ShopId == shopId, cancellationToken);
        return new ShopLoyaltyResponse(
            program is null ? null : LoyaltyMapping.Program(program), await LoyaltyStats.ForShopAsync(db, shopId, clock.GetUtcNow(), cancellationToken));
    }
}
