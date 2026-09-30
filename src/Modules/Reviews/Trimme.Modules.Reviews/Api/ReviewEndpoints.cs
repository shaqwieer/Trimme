using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Paging;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Web.Caching;
using Trimme.BuildingBlocks.Web.Errors;
using Trimme.BuildingBlocks.Web.Security;
using Trimme.Modules.Reviews.Application.Admin;
using Trimme.Modules.Reviews.Application.Customer;
using Trimme.Modules.Reviews.Application.Public;
using Trimme.Modules.Reviews.Domain;

namespace Trimme.Modules.Reviews.Api;

/// <summary>Stars 1–5, optional tags from the fixed list, optional comment (≤ 1000 characters).</summary>
public sealed record SubmitReviewRequest(int Rating, IReadOnlyList<ReviewTag>? Tags, string? Comment);

/// <summary>A reason (5–300 characters) for a report or a hide; send the version read.</summary>
public sealed record ModerateReviewRequest(string? Reason, uint Version);

/// <summary>Publishing needs only the version read.</summary>
public sealed record PublishReviewRequest(uint Version);

/// <summary>A message to the review's shop (5–500 characters), shown in its notifications.</summary>
public sealed record ContactShopRequest(string? Message);

internal static class ReviewEndpoints
{
    // Identity owns the permission catalogue; the endpoint matrix test fails if a code is not in it.
    private const string AdminView = "Admin.Reviews.View";
    private const string AdminFlag = "Admin.Reviews.Flag";
    private const string AdminModerate = "Admin.Reviews.Moderate";

    public static void Map(IEndpointRouteBuilder api)
    {
        MapAdmin(api.MapGroup("/admin/reviews").WithTags("Admin: reviews"));

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

    private static void MapAdmin(RouteGroupBuilder group)
    {
        group.MapGet("/", async (ReviewQueue? queue, ReviewFlag? flag, Guid? shopId, int? rating, string? search, int? page, int? pageSize, IDispatcher d, CancellationToken ct) =>
                TypedResults.Ok(await d.Send(new ListAdminReviewsQuery(queue ?? ReviewQueue.NeedsReview, flag, shopId, rating, search, new PageRequest(page, pageSize)), ct)))
            .RequirePermission(AdminView)
            .WithName("AdminListReviews")
            .WithSummary("Reviews for moderation: the queue (published with a report, a low rating or a phone number in the text), published, hidden or all; with counts.")
            .Produces<AdminReviewListResponse>();
        group.MapPost("/{reviewId:guid}/flag", async (Guid reviewId, ModerateReviewRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new ModerateReviewCommand(reviewId, ModerationAction.Flag, r.Reason, r.Version), ct)).ToHttpResult())
            .RequirePermission(AdminFlag)
            .WithName("AdminFlagReview").WithSummary("Reports a published review to the moderators with a reason (it stays published; audited).")
            .Produces<AdminReviewResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPost("/{reviewId:guid}/hide", async (Guid reviewId, ModerateReviewRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new ModerateReviewCommand(reviewId, ModerationAction.Hide, r.Reason, r.Version), ct)).ToHttpResult())
            .RequirePermission(AdminModerate)
            .WithName("AdminHideReview").WithSummary("Hides a review with a reason: it leaves the public pages and the rating totals (audited; never deleted).")
            .Produces<AdminReviewResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPost("/{reviewId:guid}/publish", async (Guid reviewId, PublishReviewRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new ModerateReviewCommand(reviewId, ModerationAction.Publish, null, r.Version), ct)).ToHttpResult())
            .RequirePermission(AdminModerate)
            .WithName("AdminPublishReview").WithSummary("Publishes a hidden review again, or clears a report on a published one (audited).")
            .Produces<AdminReviewResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPost("/{reviewId:guid}/contact-shop", async (Guid reviewId, ContactShopRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new ContactShopAboutReviewCommand(reviewId, r.Message), ct)).ToHttpResult())
            .RequirePermission(AdminModerate)
            .WithName("AdminContactShopAboutReview")
            .WithSummary("Sends the review's shop an in-app message about it (audited without the text).")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
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
