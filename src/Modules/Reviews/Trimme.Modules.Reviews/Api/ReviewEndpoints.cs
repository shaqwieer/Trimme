using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Paging;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Web.Caching;
using Trimme.BuildingBlocks.Web.Errors;
using Trimme.BuildingBlocks.Web.Security;
using Trimme.Modules.Reviews.Application.Customer;
using Trimme.Modules.Reviews.Application.Public;
using Trimme.Modules.Reviews.Domain;

namespace Trimme.Modules.Reviews.Api;

/// <summary>Stars 1–5, optional tags from the fixed list, optional comment (≤ 1000 characters).</summary>
public sealed record SubmitReviewRequest(int Rating, IReadOnlyList<ReviewTag>? Tags, string? Comment);

internal static class ReviewEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/public/shops/{slug}/reviews", ListPublic).AllowAnonymous().CachePublicly().WithTags("Public")
            .WithName("ListPublicShopReviews")
            .WithSummary("Published reviews of an active shop, or of one of its professionals, newest first, with the rating summary.")
            .Produces<PublicReviewsResponse>().ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPost("/me/bookings/{bookingId:guid}/review", Submit)
            .RequireUserType(UserTypes.Customer).RequireRateLimiting(RateLimitPolicies.Review).WithTags("Customer: bookings")
            .WithName("SubmitReview")
            .WithSummary("Reviews the customer's own completed booking once, within the review window; published with the first name and initial only.")
            .Produces<MyReviewResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> Submit(Guid bookingId, SubmitReviewRequest request, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        (await dispatcher.Send(new SubmitReviewCommand(bookingId, request.Rating, request.Tags ?? [], request.Comment), cancellationToken))
            .ToHttpResult(review => TypedResults.Created($"/api/v1/me/bookings/{bookingId}/review", review));

    private static async Task<IResult> ListPublic(
        string slug, Guid? professionalId, int? page, int? pageSize, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        await dispatcher.Send(new ListPublicReviewsQuery(slug, professionalId, new PageRequest(page, pageSize ?? 10)), cancellationToken) is { } reviews
            ? TypedResults.Ok(reviews)
            : Error.NotFound("shop.not_found", "The shop was not found.").ToProblem();
}
