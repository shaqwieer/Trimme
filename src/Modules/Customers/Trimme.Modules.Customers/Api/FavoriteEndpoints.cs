using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Web.Errors;
using Trimme.BuildingBlocks.Web.Security;
using Trimme.Modules.Customers.Application.Customer;

namespace Trimme.Modules.Customers.Api;

/// <summary>The professional's shop (a professional belongs to exactly one shop).</summary>
public sealed record SaveProfessionalFavoriteRequest(Guid ShopId);

internal static class FavoriteEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var favorites = api.MapGroup("/me/favorites").WithTags("Customer: favorites").RequireUserType(UserTypes.Customer);

        favorites.MapGet("/", async (IDispatcher d, CancellationToken ct) => TypedResults.Ok(await d.Send(new ListFavoritesQuery(), ct)))
            .WithName("ListMyFavorites")
            .WithSummary("The customer's saved shops and professionals, most recently saved first; ones discovery no longer shows are left out of the cards.")
            .Produces<FavoritesResponse>();

        favorites.MapPut("/shops/{shopId:guid}", async (Guid shopId, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new SaveShopFavoriteCommand(shopId), ct)).ToHttpResult(TypedResults.NoContent))
            .RequireRateLimiting(RateLimitPolicies.Favorites)
            .WithName("SaveShopFavorite").WithSummary("Saves a shop (idempotent).")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        favorites.MapDelete("/shops/{shopId:guid}", async (Guid shopId, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new RemoveShopFavoriteCommand(shopId), ct)).ToHttpResult(TypedResults.NoContent))
            .RequireRateLimiting(RateLimitPolicies.Favorites)
            .WithName("RemoveShopFavorite").WithSummary("Removes a saved shop (idempotent).")
            .Produces(StatusCodes.Status204NoContent);

        favorites.MapPut("/professionals/{professionalId:guid}", async (Guid professionalId, SaveProfessionalFavoriteRequest request, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new SaveProfessionalFavoriteCommand(professionalId, request.ShopId), ct)).ToHttpResult(TypedResults.NoContent))
            .RequireRateLimiting(RateLimitPolicies.Favorites)
            .WithName("SaveProfessionalFavorite").WithSummary("Saves an active professional of a listed shop (idempotent).")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        favorites.MapDelete("/professionals/{professionalId:guid}", async (Guid professionalId, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new RemoveProfessionalFavoriteCommand(professionalId), ct)).ToHttpResult(TypedResults.NoContent))
            .RequireRateLimiting(RateLimitPolicies.Favorites)
            .WithName("RemoveProfessionalFavorite").WithSummary("Removes a saved professional (idempotent).")
            .Produces(StatusCodes.Status204NoContent);
    }
}
