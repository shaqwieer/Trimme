using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Auditing;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Paging;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Reviews.Domain;

namespace Trimme.Modules.Reviews.Application.Admin;

// Reviews moderation (a-reviews, R-AD-07, D-102). Post-moderation (D-017): reviews publish at once; staff report them
// (Admin.Reviews.Flag), moderators hide or publish them (Admin.Reviews.Moderate) with the rating totals moving in the
// same transaction. Every action is audited; the review is never deleted.

/// <summary>Which reviews the list shows: the moderation queue (published with any flag), all published, or hidden.</summary>
public enum ReviewQueue
{
    NeedsReview,
    Published,
    Hidden,
    All,
}

/// <summary>A review as moderators see it: the public name and text, the shop and professional, the flags and the moderation state.</summary>
public sealed record AdminReviewResponse(
    Guid Id,
    Guid BookingId,
    Guid ShopId,
    string ShopNameAr,
    string ShopNameEn,
    Guid ProfessionalId,
    string ProfessionalNameAr,
    string ProfessionalNameEn,
    Guid? CustomerId,
    string AuthorName,
    int Rating,
    IReadOnlyList<ReviewTag> Tags,
    string? Comment,
    string ItemNameAr,
    string? ItemNameEn,
    ReviewStatus Status,
    IReadOnlyList<ReviewFlag> Flags,
    string? FlagReason,
    DateTimeOffset? FlaggedAt,
    string? ModerationReason,
    DateTimeOffset? ModeratedAt,
    DateTimeOffset CreatedAt,
    uint Version);

public sealed record AdminReviewCounts(int NeedsReview, int Published, int Hidden, int All);

public sealed record AdminReviewListResponse(IReadOnlyList<AdminReviewResponse> Items, int Page, int PageSize, int Total, AdminReviewCounts Counts);

internal sealed record ListAdminReviewsQuery(ReviewQueue Queue, ReviewFlag? Flag, Guid? ShopId, int? Rating, string? Search, PageRequest Page)
    : IQuery<AdminReviewListResponse>;

internal enum ModerationAction
{
    Flag,
    Hide,
    Publish,
}

internal sealed record ModerateReviewCommand(Guid ReviewId, ModerationAction Action, string? Reason, uint Version) : ICommand<Result<AdminReviewResponse>>;

internal sealed class AdminReviewMapper(IShopDirectory shops, IProfessionalDirectory professionals)
{
    public async Task<IReadOnlyList<AdminReviewResponse>> MapAsync(IReadOnlyList<Review> reviews, CancellationToken cancellationToken)
    {
        var names = await shops.FindManyAsync([.. reviews.Select(r => r.ShopId).Distinct()], cancellationToken);
        var staff = await professionals.FindManyAsync([.. reviews.Select(r => r.ProfessionalId).Distinct()], cancellationToken);

        return
        [
            .. reviews.Select(r =>
            {
                var shop = names.GetValueOrDefault(r.ShopId);
                var professional = staff.GetValueOrDefault(r.ProfessionalId);
                return new AdminReviewResponse(
                    r.Id.Value, r.BookingId, r.ShopId.Value, shop?.NameAr ?? string.Empty, shop?.NameEn ?? string.Empty, r.ProfessionalId.Value,
                    professional?.NameAr ?? string.Empty, professional?.NameEn ?? string.Empty, r.CustomerId, r.AuthorName, r.Rating, r.Tags, r.Comment,
                    r.ItemNameAr, r.ItemNameEn, r.Status, r.Flags, r.FlagReason, r.FlaggedAt, r.ModerationReason, r.ModeratedAt, r.CreatedAt, r.Version);
            }),
        ];
    }
}

internal sealed class ListAdminReviewsHandler(TrimmeDbContext db, IAdminDataScope scope, AdminReviewMapper mapper)
    : IQueryHandler<ListAdminReviewsQuery, AdminReviewListResponse>
{
    public async Task<AdminReviewListResponse> Handle(ListAdminReviewsQuery query, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var reviews = db.Set<Review>().AsNoTracking();
        if (query.ShopId is { } shop)
        {
            var shopId = new ShopId(shop);
            reviews = reviews.Where(r => r.ShopId == shopId);
        }

        if (query.Rating is { } rating)
        {
            reviews = reviews.Where(r => r.Rating == rating);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{query.Search.Trim()}%";
            reviews = reviews.Where(r => EF.Functions.ILike(r.AuthorName, pattern) || (r.Comment != null && EF.Functions.ILike(r.Comment, pattern)));
        }

        reviews = query.Flag switch
        {
            ReviewFlag.Reported => reviews.Where(r => r.FlagReason != null),
            ReviewFlag.LowRating => reviews.Where(r => r.Rating <= ReviewModeration.LowRatingMax),
            ReviewFlag.ContainsPhone => reviews.Where(r => r.Comment != null && Regex.IsMatch(r.Comment, ReviewModeration.PhonePattern)),
            _ => reviews,
        };

        var counts = new AdminReviewCounts(
            await NeedsReview(reviews).CountAsync(cancellationToken),
            await reviews.CountAsync(r => r.Status == ReviewStatus.Published, cancellationToken),
            await reviews.CountAsync(r => r.Status == ReviewStatus.Hidden, cancellationToken),
            await reviews.CountAsync(cancellationToken));

        var listed = query.Queue switch
        {
            ReviewQueue.NeedsReview => NeedsReview(reviews),
            ReviewQueue.Published => reviews.Where(r => r.Status == ReviewStatus.Published),
            ReviewQueue.Hidden => reviews.Where(r => r.Status == ReviewStatus.Hidden),
            _ => reviews,
        };
        var total = await listed.CountAsync(cancellationToken);
        var page = await listed.OrderByDescending(r => r.FlagReason != null).ThenByDescending(r => r.CreatedAt).ThenBy(r => r.Id)
            .Skip(query.Page.Skip).Take(query.Page.PageSize).ToListAsync(cancellationToken);
        return new AdminReviewListResponse(await mapper.MapAsync(page, cancellationToken), query.Page.Page, query.Page.PageSize, total, counts);
    }

    /// <summary>Published reviews with any flag: reported, low rating, or a phone number in the text.</summary>
    private static IQueryable<Review> NeedsReview(IQueryable<Review> reviews) =>
        reviews.Where(r => r.Status == ReviewStatus.Published
                           && (r.FlagReason != null || r.Rating <= ReviewModeration.LowRatingMax
                               || (r.Comment != null && Regex.IsMatch(r.Comment, ReviewModeration.PhonePattern))));
}

/// <summary>
/// Report, hide or publish one review. Hide and publish move the shop's and the professional's rating totals by one in
/// the same transaction as the versioned save, so two moderators acting at once change the totals once (the second gets
/// 409). The public pages are evicted after the commit.
/// </summary>
internal sealed class ModerateReviewHandler(
    TrimmeDbContext db, IAdminDataScope scope, IAuditLog audit, AdminReviewMapper mapper, IPublicContentChangeSink publicContent, TimeProvider clock)
    : ICommandHandler<ModerateReviewCommand, Result<AdminReviewResponse>>
{
    public async Task<Result<AdminReviewResponse>> Handle(ModerateReviewCommand command, CancellationToken cancellationToken)
    {
        AdminReviewResponse response;
        using (scope.Begin())
        {
            var id = new ReviewId(command.ReviewId);
            if (await db.Set<Review>().SingleOrDefaultAsync(r => r.Id == id, cancellationToken) is not { } review)
            {
                return ReviewErrors.NotFound();
            }

            db.Entry(review).Property(r => r.Version).OriginalValue = command.Version;
            var now = clock.GetUtcNow();
            var wasPublished = review.Status == ReviewStatus.Published;
            var result = command.Action switch
            {
                ModerationAction.Flag => review.Flag(command.Reason, now),
                ModerationAction.Hide => review.Hide(command.Reason, now),
                _ => review.Publish(now),
            };
            if (result.IsFailure)
            {
                return result.Error;
            }

            var sign = (wasPublished, review.Status) switch
            {
                (true, ReviewStatus.Hidden) => -1,
                (false, ReviewStatus.Published) => +1,
                _ => 0,
            };
            audit.Record(new AuditRecord(
                command.Action switch
                {
                    ModerationAction.Flag => "review.flagged",
                    ModerationAction.Hide => "review.hidden",
                    _ => sign == +1 ? "review.published" : "review.report_cleared",
                },
                nameof(Review), review.Id.ToString(), review.ShopId, $"{review.Rating}★ review of booking {review.BookingId}",
                command.Action == ModerationAction.Publish ? null : command.Reason?.Trim()));

            await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
            {
                await db.SaveChangesAsync(cancellationToken);
                if (sign != 0)
                {
                    await RatingBook.ApplyAsync(db, review, sign, now, cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
            }

            response = (await mapper.MapAsync([review], cancellationToken))[0];
        }

        await publicContent.PublicContentChangedAsync(cancellationToken);
        return response;
    }
}
