using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Web.Caching;
using Trimme.BuildingBlocks.Web.Errors;
using Trimme.BuildingBlocks.Web.Security;
using Trimme.Modules.Loyalty.Application;
using Trimme.Modules.Loyalty.Application.Admin;
using Trimme.Modules.Loyalty.Application.Customer;
using Trimme.Modules.Loyalty.Application.Public;
using Trimme.Modules.Loyalty.Application.Shop;
using Trimme.Modules.Loyalty.Domain;

namespace Trimme.Modules.Loyalty.Api;

/// <summary>
/// A shop's loyalty rules (D-149): <c>FirstVisitPoints</c> and <c>VisitPoints</c> (1–100), <c>PointsForReward</c> (2–1,000);
/// the reward: <c>FreeService</c> with <c>RewardServiceId</c> (one of the shop's services) or <c>AmountOff</c> with
/// <c>RewardAmount</c> (5–1,000 SAR); without visits: <c>None</c>, <c>Wipe</c> or <c>DecayPerPeriod</c>, with
/// <c>InactivityDays</c> (7–730). Send the <c>Version</c> read, except for the first save.
/// </summary>
public sealed record SaveLoyaltyRulesRequest(
    int FirstVisitPoints,
    int VisitPoints,
    int PointsForReward,
    LoyaltyRewardType RewardType,
    Guid? RewardServiceId,
    decimal? RewardAmount,
    LoyaltyInactivityRule InactivityRule,
    int? InactivityDays,
    uint? Version)
{
    internal LoyaltyRules Rules => new(FirstVisitPoints, VisitPoints, PointsForReward, RewardType, RewardServiceId, RewardAmount, InactivityRule, InactivityDays);
}

/// <summary>The mobile typed for a walk-in or phone booking (sent in the body, so it never appears in a URL or a log).</summary>
public sealed record LoyaltyLookupRequest(string Phone)
{
    public override string ToString() => nameof(LoyaltyLookupRequest);
}

internal static class LoyaltyEndpoints
{
    // Identity owns the permission catalogue; the endpoint matrix test fails if a code is not in it.
    private const string AdminView = "Admin.Shops.View";
    private const string AdminManage = "Admin.Loyalty.Manage";
    private const string ShopRead = "Shop.Bookings.Read";

    // The owner's shop settings permission (barbers do not hold it), as for the booking settings (D-149).
    private const string ShopManage = "Shop.Profile.Edit";
    private const string ShopWalkIn = "Shop.Bookings.CreateWalkIn";

    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/public/shops/{slug}/loyalty", async (string slug, IDispatcher d, CancellationToken ct) =>
                await d.Send(new GetPublicLoyaltyQuery(slug), ct) is { } loyalty
                    ? Results.Ok(loyalty)
                    : LoyaltyErrors.ShopNotFound().ToProblem())
            .AllowAnonymous().CachePublicly().WithTags("Public")
            .WithName("GetPublicShopLoyalty")
            .WithSummary("Whether a published shop runs loyalty now (its plan includes it and its subscription is in force, D-155) and its rules (D-149). No customer data.")
            .Produces<PublicLoyaltyResponse>().ProducesProblem(StatusCodes.Status404NotFound);

        api.MapGet("/me/loyalty", async (IDispatcher d, CancellationToken ct) => TypedResults.Ok(await d.Send(new ListMyLoyaltyQuery(), ct)))
            .RequireUserType(UserTypes.Customer).WithTags("Customer: loyalty")
            .WithName("ListMyLoyalty")
            .WithSummary("The customer's cards across shops, by their account's mobile (D-149): points, rewards ready, the card being filled, history; and D-132 coupons.")
            .Produces<MyLoyaltyResponse>();
        api.MapGet("/me/loyalty/shops/{slug}", async (string slug, IDispatcher d, CancellationToken ct) =>
                await d.Send(new GetMyShopLoyaltyQuery(slug), ct) is { } loyalty
                    ? Results.Ok(loyalty)
                    : LoyaltyErrors.ShopNotFound().ToProblem())
            .RequireUserType(UserTypes.Customer).WithTags("Customer: loyalty")
            .WithName("GetMyShopLoyalty")
            .WithSummary("The customer's card at one published shop (the booking page offers «استخدم مكافأتي» when a reward is ready).")
            .Produces<MyShopLoyaltyResponse>().ProducesProblem(StatusCodes.Status404NotFound);

        api.MapGet("/shop/loyalty", async (IDispatcher d, CancellationToken ct) =>
                await d.Send(new GetShopLoyaltyQuery(), ct) is { } loyalty
                    ? Results.Ok(loyalty)
                    : Error.NotFound("shop.not_found", "The shop was not found.").ToProblem())
            .RequirePermission(ShopRead).WithTags("Shop: loyalty")
            .WithName("GetShopLoyalty")
            .WithSummary("The shop's loyalty: whether its plan includes it now (D-155), the rules and (for the owner) how they are used.")
            .Produces<ShopLoyaltyResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        api.MapPut("/shop/loyalty", async (SaveLoyaltyRulesRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new SaveShopLoyaltyRulesCommand(r.Rules, r.Version), ct)).ToHttpResult())
            .RequirePermission(ShopManage).WithTags("Shop: loyalty")
            .WithName("SaveShopLoyaltyRules")
            .WithSummary("The owner sets the loyalty rules: points per visit, the reward and what happens without visits (D-149; versioned; audited).")
            .Produces<ShopLoyaltyResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict);
        api.MapPost("/shop/loyalty/lookup", async (LoyaltyLookupRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new LookupLoyaltyRewardQuery(r.Phone ?? string.Empty), ct)).ToHttpResult())
            .RequirePermission(ShopWalkIn).WithTags("Shop: loyalty")
            .WithName("LookupLoyaltyReward")
            .WithSummary("For a walk-in or phone booking: whether the typed number has a reward ready here, and what it is. Nothing else.")
            .Produces<LoyaltyLookupResponse>().ProducesProblem(StatusCodes.Status400BadRequest);

        var admin = api.MapGroup("/admin/shops/{shopId:guid}/loyalty").WithTags("Admin: loyalty");
        admin.MapGet("/", async (Guid shopId, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new GetAdminShopLoyaltyQuery(shopId), ct)).ToHttpResult())
            .RequirePermission(AdminView)
            .WithName("AdminGetShopLoyalty")
            .WithSummary("A shop's loyalty: whether its plan includes it now (D-155), the rules (null before they are set) and their use.")
            .Produces<AdminShopLoyaltyResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        admin.MapPut("/", async (Guid shopId, SaveLoyaltyRulesRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new SaveAdminShopLoyaltyCommand(shopId, r.Rules, r.Version), ct)).ToHttpResult())
            .RequirePermission(AdminManage)
            .WithName("AdminSaveShopLoyalty")
            .WithSummary("Sets a shop's loyalty rules on its behalf (D-149; versioned; audited).")
            .Produces<AdminShopLoyaltyResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}
