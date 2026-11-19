using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Discovery;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Paging;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Reviews.Domain;

namespace Trimme.Modules.Reviews.Application.Public;

/// <summary>
/// A published review as the public sees it (D-017): the author's first name and surname initial, never the full name
/// or any contact data (DV-S15). <c>ItemNameAr/En</c> is the booked service or package (the design links reviews to a
/// service). <c>Tags</c> is what the customer picked in «ما الذي أعجبك؟» (D-152, the client's 3-6).
/// </summary>
public sealed record PublicReviewResponse(
    Guid Id,
    string AuthorName,
    int Rating,
    string? Comment,
    string ItemNameAr,
    string? ItemNameEn,
    Guid ProfessionalId,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ReviewTag> Tags);

/// <summary>How many of the published reviews picked one «ما الذي أعجبك؟» tag (D-152).</summary>
public sealed record ReviewTagCountResponse(ReviewTag Tag, int Count);

/// <summary>
/// Average and count, and the number of reviews per star (index 0 = one star … 4 = five stars). <c>Tags</c> (D-152, the
/// client's 3-6: «٨ أشادوا بجودة الحلاقة») counts each tag over the same published reviews (the shop's, or the barber's),
/// most picked first; tags nobody picked are left out.
/// </summary>
public sealed record RatingSummaryResponse(decimal Average, int Count, IReadOnlyList<int> Histogram, IReadOnlyList<ReviewTagCountResponse> Tags);

public sealed record PublicReviewsResponse(RatingSummaryResponse Summary, PagedResponse<PublicReviewResponse> Reviews);

internal sealed record ListPublicReviewsQuery(string ShopSlug, Guid? ProfessionalId, PageRequest Page) : IQuery<PublicReviewsResponse?>;

/// <summary>
/// Published reviews of an active shop (or of one of its professionals), newest first, with the matching rating summary.
/// Reads inside the public scope of that one shop (D-066); <see langword="null"/> when the shop is not published.
/// </summary>
internal sealed class ListPublicReviewsHandler(TrimmeDbContext db, IPublicDataScope scope, IShopDirectory shops, IRatingReader ratings)
    : IQueryHandler<ListPublicReviewsQuery, PublicReviewsResponse?>
{
    public async Task<PublicReviewsResponse?> Handle(ListPublicReviewsQuery query, CancellationToken cancellationToken)
    {
        ShopSummary? shop;
        using (scope.Begin(shopId: null))
        {
            shop = await shops.FindBySlugAsync(query.ShopSlug, cancellationToken);
        }

        if (shop is not { Status: ShopStatus.Active })
        {
            return null;
        }

        using var _ = scope.Begin(shop.Id);
        var reviews = db.Set<Review>().AsNoTracking().Where(r => r.ShopId == shop.Id && r.Status == ReviewStatus.Published);
        if (query.ProfessionalId is { } professional)
        {
            var professionalId = new ProfessionalId(professional);
            reviews = reviews.Where(r => r.ProfessionalId == professionalId);
        }

        var total = await reviews.CountAsync(cancellationToken);
        var page = await reviews.OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id)
            .Skip(query.Page.Skip).Take(query.Page.PageSize)
            .Select(r => new PublicReviewResponse(r.Id.Value, r.AuthorName, r.Rating, r.Comment, r.ItemNameAr, r.ItemNameEn, r.ProfessionalId.Value, r.CreatedAt, r.Tags))
            .ToListAsync(cancellationToken);

        // Each tag's count over the same published reviews (D-152), in the database (one unnest, grouped).
        var tags = await reviews.SelectMany(r => r.Tags).GroupBy(t => t).Select(g => new { Tag = g.Key, Count = g.Count() }).ToListAsync(cancellationToken);

        var subjectId = query.ProfessionalId ?? shop.Id.Value;
        var summaries = await ratings.GetAsync(query.ProfessionalId is null ? RatingSubject.Shop : RatingSubject.Professional, [subjectId], cancellationToken);
        var summary = summaries.GetValueOrDefault(subjectId) ?? RatingSummary.Empty;
        return new PublicReviewsResponse(
            new RatingSummaryResponse(
                summary.Average, summary.Count, summary.Histogram,
                [.. tags.Where(t => t.Count > 0).OrderByDescending(t => t.Count).ThenBy(t => t.Tag).Select(t => new ReviewTagCountResponse(t.Tag, t.Count))]),
            new PagedResponse<PublicReviewResponse>(page, query.Page.Page, query.Page.PageSize, total));
    }
}
