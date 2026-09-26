using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Paging;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Web.Errors;
using Trimme.BuildingBlocks.Web.Security;
using Trimme.Modules.Shops.Application;
using Trimme.Modules.Shops.Application.Admin;
using Trimme.Modules.Shops.Domain;

namespace Trimme.Modules.Shops.Api;

public sealed record CreateShopRequest(string Slug, string NameAr, string NameEn, string? TimeZone);

public sealed record ChangeShopStatusRequest(string? Reason);

internal static class ShopEndpoints
{
    // Identity owns the permission catalogue; the codes are duplicated as literals here to keep modules decoupled.
    // The endpoint matrix test fails if a code is not in the catalogue.
    private const string ShopsView = "Admin.Shops.View";
    private const string ShopsCreate = "Admin.Shops.Create";
    private const string ShopsSuspend = "Admin.Shops.Suspend";

    public static void Map(IEndpointRouteBuilder api)
    {
        var admin = api.MapGroup("/admin/shops").WithTags("Admin: shops");

        admin.MapPost("/", Create).RequirePermission(ShopsCreate)
            .WithName("AdminCreateShop").WithSummary("Creates a shop in Draft status.")
            .Produces<AdminShopResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest);
        admin.MapGet("/", List).RequirePermission(ShopsView)
            .WithName("AdminListShops").WithSummary("Shops, newest first, with search and status filter (paged).");
        admin.MapGet("/{shopId:guid}", Get).RequirePermission(ShopsView)
            .WithName("AdminGetShop").WithSummary("One shop.")
            .Produces<AdminShopResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        admin.MapPost("/{shopId:guid}/activate", Activate).RequirePermission(ShopsSuspend)
            .WithName("AdminActivateShop").WithSummary("Activates a draft or suspended shop (audited).")
            .Produces<AdminShopResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        admin.MapPost("/{shopId:guid}/suspend", Suspend).RequirePermission(ShopsSuspend)
            .WithName("AdminSuspendShop").WithSummary("Suspends a shop; its users lose access to shop data immediately (audited).")
            .Produces<AdminShopResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        api.MapGet("/shop/me", MyShop).RequireUserType(UserTypes.ShopUser).WithTags("Shop")
            .WithName("GetMyShop").WithSummary("The signed-in shop user's shop, from the session (never from the request).")
            .Produces<ShopProfileResponse>().ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Create(CreateShopRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(
            new CreateShopCommand(request.Slug ?? string.Empty, request.NameAr ?? string.Empty, request.NameEn ?? string.Empty, request.TimeZone),
            cancellationToken);
        return result.ToHttpResult(shop => TypedResults.Created($"/api/v1/admin/shops/{shop.Id}", shop));
    }

    private static async Task<Ok<PagedResponse<AdminShopResponse>>> List(
        int? page, int? pageSize, string? search, ShopStatus? status, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        TypedResults.Ok(await dispatcher.Send(new ListShopsQuery(new PageRequest(page, pageSize), search, status), cancellationToken));

    private static async Task<IResult> Get(Guid shopId, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        await dispatcher.Send(new GetShopQuery(shopId), cancellationToken) is { } shop
            ? TypedResults.Ok(shop)
            : ShopErrors.NotFound().ToProblem();

    private static async Task<IResult> Activate(Guid shopId, ChangeShopStatusRequest? request, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        (await dispatcher.Send(new ChangeShopStatusCommand(shopId, ShopStatusChange.Activate, request?.Reason), cancellationToken)).ToHttpResult();

    private static async Task<IResult> Suspend(Guid shopId, ChangeShopStatusRequest? request, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        (await dispatcher.Send(new ChangeShopStatusCommand(shopId, ShopStatusChange.Suspend, request?.Reason), cancellationToken)).ToHttpResult();

    private static async Task<IResult> MyShop(ICurrentUser user, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        user.ShopId is { } shopId && await dispatcher.Send(new GetMyShopQuery(new ShopId(shopId)), cancellationToken) is { } shop
            ? TypedResults.Ok(shop)
            : ShopErrors.NotFound().ToProblem();
}
