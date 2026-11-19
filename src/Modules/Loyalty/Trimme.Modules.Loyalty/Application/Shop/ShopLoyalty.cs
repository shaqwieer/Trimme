using Trimme.BuildingBlocks.Application.Loyalty;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Privacy;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Privacy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Loyalty.Domain;

namespace Trimme.Modules.Loyalty.Application.Shop;

/// <summary>
/// The shop's own loyalty (D-149): whether the paid add-on is in force, the rules and their version, and how they are used.
/// A barber reads the rules but not the figures (D-147: «ما يشوف إحصائيات»): <c>Stats</c> is null for them.
/// </summary>
public sealed record ShopLoyaltyResponse(bool AddOnActive, LoyaltyRulesResponse? Rules, uint? Version, LoyaltyStatsResponse? Stats);

/// <summary>
/// Whether the number typed for a walk-in or phone booking has a reward ready at the shop, and what it is (D-149). Nothing
/// else about the customer: no points, no history, no name.
/// </summary>
public sealed record LoyaltyLookupResponse(bool RewardReady, LoyaltyRewardType? RewardType, Guid? RewardServiceId, decimal? RewardAmount, string? TitleAr, string? TitleEn);

internal sealed record GetShopLoyaltyQuery : IQuery<ShopLoyaltyResponse?>;

internal sealed record SaveShopLoyaltyRulesCommand(LoyaltyRules Rules, uint? Version) : ICommand<Result<ShopLoyaltyResponse>>;

internal sealed record LookupLoyaltyRewardQuery(string Phone) : IQuery<Result<LoyaltyLookupResponse>>
{
    public override string ToString() => nameof(LookupLoyaltyRewardQuery);
}

/// <summary>The shop comes from the caller's claims (tenant filter); <see langword="null"/> for a suspended shop.</summary>
internal sealed class GetShopLoyaltyHandler(TrimmeDbContext db, ICurrentTenant tenant, ICurrentUser user, LoyaltyRulesStore rules, TimeProvider clock)
    : IQueryHandler<GetShopLoyaltyQuery, ShopLoyaltyResponse?>
{
    public async Task<ShopLoyaltyResponse?> Handle(GetShopLoyaltyQuery query, CancellationToken cancellationToken)
    {
        if (tenant.ShopId is not { } shopId)
        {
            return null;
        }

        var editor = await rules.GetAsync(shopId, cancellationToken);
        return new ShopLoyaltyResponse(
            editor.AddOnActive, editor.Rules, editor.Version,
            user.ProfessionalId is null ? await LoyaltyStats.ForShopAsync(db, shopId, clock.GetUtcNow(), cancellationToken) : null);
    }
}

/// <summary>The owner sets the shop's loyalty rules (<c>Shop.Profile.Edit</c>; barbers cannot). Audited.</summary>
internal sealed class SaveShopLoyaltyRulesHandler(TrimmeDbContext db, ICurrentTenant tenant, LoyaltyRulesStore rules, TimeProvider clock)
    : ICommandHandler<SaveShopLoyaltyRulesCommand, Result<ShopLoyaltyResponse>>
{
    public async Task<Result<ShopLoyaltyResponse>> Handle(SaveShopLoyaltyRulesCommand command, CancellationToken cancellationToken)
    {
        if (tenant.ShopId is not { } shopId)
        {
            return LoyaltyErrors.ShopNotFound();
        }

        var saved = await rules.SaveAsync(shopId, command.Rules, command.Version, "shop", cancellationToken);
        return saved.IsFailure
            ? saved.Error
            : new ShopLoyaltyResponse(saved.Value.AddOnActive, saved.Value.Rules, saved.Value.Version, await LoyaltyStats.ForShopAsync(db, shopId, clock.GetUtcNow(), cancellationToken));
    }
}

/// <summary>
/// A barber checks the typed number before a walk-in or phone booking (D-149): only whether a reward is ready and what it
/// is. The number is hashed and never stored, logged or answered.
/// </summary>
internal sealed class LookupLoyaltyRewardHandler(ICurrentTenant tenant, LoyaltyLedger ledger, ILoyaltyAddOnStatus addOn, IPersonalDataProtector protector, TimeProvider clock)
    : IQueryHandler<LookupLoyaltyRewardQuery, Result<LoyaltyLookupResponse>>
{
    private static readonly LoyaltyLookupResponse None = new(false, null, null, null, null, null);

    public async Task<Result<LoyaltyLookupResponse>> Handle(LookupLoyaltyRewardQuery query, CancellationToken cancellationToken)
    {
        if (tenant.ShopId is not { } shopId)
        {
            return LoyaltyErrors.ShopNotFound();
        }

        if (!PhoneNumber.TryParseMobile(query.Phone, out var phone))
        {
            return Error.Validation("validation.failed", "The mobile number is invalid.",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["phone"] = ["validation.phone_invalid"] });
        }

        if (!await addOn.IsActiveAsync(shopId, cancellationToken) || await ledger.SettingsAsync(shopId, cancellationToken) is not { } settings)
        {
            return None;
        }

        var memberKey = LoyaltyMembers.KeyOf(protector, phone.E164);
        var account = await ledger.AccountAsync(shopId, memberKey, track: false, cancellationToken);
        var held = await ledger.HeldAsync(shopId, memberKey, null, cancellationToken);
        if (LoyaltyBalance.Available(account, settings, held, clock.GetUtcNow()) < settings.PointsForReward)
        {
            return None;
        }

        var (titleAr, titleEn) = settings.RewardTitle();
        return new LoyaltyLookupResponse(true, settings.RewardType, settings.RewardServiceId, settings.RewardAmount, titleAr, titleEn);
    }
}
