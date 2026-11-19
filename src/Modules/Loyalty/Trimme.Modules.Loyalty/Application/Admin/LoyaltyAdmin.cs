using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Loyalty.Domain;

namespace Trimme.Modules.Loyalty.Application.Admin;

/// <summary>
/// A shop's loyalty as the admin sees it (D-149): whether its plan includes loyalty now (D-155; before, a paid add-on switched on or off on the shop's
/// subscription tab), the owner's rules (null before the first save; send <c>Version</c> back) and their use.
/// </summary>
public sealed record AdminShopLoyaltyResponse(bool Included, LoyaltyRulesResponse? Rules, uint? Version, LoyaltyStatsResponse Stats);

internal sealed record GetAdminShopLoyaltyQuery(Guid ShopId) : IQuery<Result<AdminShopLoyaltyResponse>>;

internal sealed record SaveAdminShopLoyaltyCommand(Guid ShopId, LoyaltyRules Rules, uint? Version) : ICommand<Result<AdminShopLoyaltyResponse>>;

internal sealed class GetAdminShopLoyaltyHandler(TrimmeDbContext db, IAdminDataScope scope, IShopDirectory shops, LoyaltyRulesStore rules, TimeProvider clock)
    : IQueryHandler<GetAdminShopLoyaltyQuery, Result<AdminShopLoyaltyResponse>>
{
    public async Task<Result<AdminShopLoyaltyResponse>> Handle(GetAdminShopLoyaltyQuery query, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var shopId = new ShopId(query.ShopId);
        if (await shops.FindAsync(shopId, cancellationToken) is null)
        {
            return LoyaltyErrors.ShopNotFound();
        }

        var editor = await rules.GetAsync(shopId, cancellationToken);
        return new AdminShopLoyaltyResponse(editor.Included, editor.Rules, editor.Version, await LoyaltyStats.ForShopAsync(db, shopId, clock.GetUtcNow(), cancellationToken));
    }
}

/// <summary>The admin edits the shop's rules too (<c>Admin.Loyalty.Manage</c>). Versioned and audited.</summary>
internal sealed class SaveAdminShopLoyaltyHandler(TrimmeDbContext db, IAdminDataScope scope, IShopDirectory shops, LoyaltyRulesStore rules, TimeProvider clock)
    : ICommandHandler<SaveAdminShopLoyaltyCommand, Result<AdminShopLoyaltyResponse>>
{
    public async Task<Result<AdminShopLoyaltyResponse>> Handle(SaveAdminShopLoyaltyCommand command, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var shopId = new ShopId(command.ShopId);
        if (await shops.FindAsync(shopId, cancellationToken) is null)
        {
            return LoyaltyErrors.ShopNotFound();
        }

        var saved = await rules.SaveAsync(shopId, command.Rules, command.Version, "admin", cancellationToken);
        return saved.IsFailure
            ? saved.Error
            : new AdminShopLoyaltyResponse(saved.Value.Included, saved.Value.Rules, saved.Value.Version, await LoyaltyStats.ForShopAsync(db, shopId, clock.GetUtcNow(), cancellationToken));
    }
}
