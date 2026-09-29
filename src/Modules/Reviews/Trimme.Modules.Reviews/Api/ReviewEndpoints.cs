using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Paging;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Web.Caching;
using Trimme.BuildingBlocks.Web.Errors;
using Trimme.Modules.Reviews.Application.Public;

namespace Trimme.Modules.Reviews.Api;

internal static class ReviewEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/public/shops/{slug}/reviews", ListPublic).AllowAnonymous().CachePublicly().WithTags("Public")
            .WithName("ListPublicShopReviews")
            .WithSummary("Published reviews of an active shop, or of one of its professionals, newest first, with the rating summary.")
            .Produces<PublicReviewsResponse>().ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListPublic(
        string slug, Guid? professionalId, int? page, int? pageSize, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        await dispatcher.Send(new ListPublicReviewsQuery(slug, professionalId, new PageRequest(page, pageSize ?? 10)), cancellationToken) is { } reviews
            ? TypedResults.Ok(reviews)
            : Error.NotFound("shop.not_found", "The shop was not found.").ToProblem();
}
