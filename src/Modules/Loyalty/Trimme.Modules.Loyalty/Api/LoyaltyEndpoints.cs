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
/// A shop's loyalty terms (D-132). <c>RewardTitleAr</c> is what the customer gets («حلاقة مجانية»); a discount needs
/// <c>DiscountPercent</c> 1–100. <c>RewardValidityDays</c> (7–365) limits how long a reward stays usable; null keeps it.
/// Send the <c>Version</c> read, except for the first save.
/// </summary>
public sealed record SaveLoyaltyProgramRequest(
    bool IsActive,
    int RequiredVisits,
    LoyaltyRewardKind RewardKind,
    int? DiscountPercent,
    string? RewardTitleAr,
    string? RewardTitleEn,
    int? RewardValidityDays,
    uint? Version);

internal static class LoyaltyEndpoints
{
    // Identity owns the permission catalogue; the endpoint matrix test fails if a code is not in it.
    private const string AdminView = "Admin.Shops.View";
    private const string AdminManage = "Admin.Loyalty.Manage";
    private const string ShopRead = "Shop.Bookings.Read";

    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/public/shops/{slug}/loyalty", async (string slug, IDispatcher d, CancellationToken ct) =>
                await d.Send(new GetPublicLoyaltyQuery(slug), ct) is { } loyalty
                    ? Results.Ok(loyalty)
                    : LoyaltyErrors.ShopNotFound().ToProblem())
            .AllowAnonymous().CachePublicly().WithTags("Public")
            .WithName("GetPublicShopLoyalty")
            .WithSummary("A published shop's loyalty offer (visits needed and the reward), or a null programme when it runs none.")
            .Produces<PublicLoyaltyResponse>().ProducesProblem(StatusCodes.Status404NotFound);

        api.MapGet("/me/loyalty", async (IDispatcher d, CancellationToken ct) => TypedResults.Ok(await d.Send(new ListMyLoyaltyQuery(), ct)))
            .RequireUserType(UserTypes.Customer).WithTags("Customer: loyalty")
            .WithName("ListMyLoyalty")
            .WithSummary("The customer's loyalty at each shop where they have stamps or rewards: the programme, the stamps on the open card and the usable or held rewards.")
            .Produces<MyLoyaltyResponse>();
        api.MapGet("/me/loyalty/shops/{slug}", async (string slug, IDispatcher d, CancellationToken ct) =>
                await d.Send(new GetMyShopLoyaltyQuery(slug), ct) is { } loyalty
                    ? Results.Ok(loyalty)
                    : LoyaltyErrors.ShopNotFound().ToProblem())
            .RequireUserType(UserTypes.Customer).WithTags("Customer: loyalty")
            .WithName("GetMyShopLoyalty")
            .WithSummary("The customer's loyalty at one published shop (the booking page offers its usable rewards).")
            .Produces<MyShopLoyaltyResponse>().ProducesProblem(StatusCodes.Status404NotFound);

        api.MapGet("/shop/loyalty", async (IDispatcher d, CancellationToken ct) =>
                await d.Send(new GetShopLoyaltyQuery(), ct) is { } loyalty
                    ? Results.Ok(loyalty)
                    : Error.NotFound("shop.not_found", "The shop was not found.").ToProblem())
            .RequirePermission(ShopRead).WithTags("Shop: loyalty")
            .WithName("GetShopLoyalty")
            .WithSummary("The shop's loyalty programme as the platform set it (read-only) and how it is used.")
            .Produces<ShopLoyaltyResponse>().ProducesProblem(StatusCodes.Status404NotFound);

        var admin = api.MapGroup("/admin/shops/{shopId:guid}/loyalty").WithTags("Admin: loyalty");
        admin.MapGet("/", async (Guid shopId, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new GetAdminShopLoyaltyQuery(shopId), ct)).ToHttpResult())
            .RequirePermission(AdminView)
            .WithName("AdminGetShopLoyalty")
            .WithSummary("A shop's loyalty programme (null before it is first set) and its use.")
            .Produces<AdminShopLoyaltyResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        admin.MapPut("/", async (Guid shopId, SaveLoyaltyProgramRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new SaveShopLoyaltyCommand(
                    shopId, new LoyaltyTerms(r.IsActive, r.RequiredVisits, r.RewardKind, r.DiscountPercent, r.RewardTitleAr, r.RewardTitleEn, r.RewardValidityDays),
                    r.Version), ct)).ToHttpResult())
            .RequirePermission(AdminManage)
            .WithName("AdminSaveShopLoyalty")
            .WithSummary("Creates or replaces a shop's loyalty terms, agreed with the shop (versioned; audited).")
            .Produces<AdminShopLoyaltyResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}
