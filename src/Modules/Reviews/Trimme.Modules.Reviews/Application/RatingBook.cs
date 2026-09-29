using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Bookings;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Reviews.Domain;

namespace Trimme.Modules.Reviews.Application;

/// <summary>
/// Keeps the rating aggregates in step with published reviews (R-RVW-01, D-092): every change is applied to the shop's and
/// the professional's row in the caller's unit of work, so a review and its totals commit or roll back together.
/// </summary>
internal static class RatingBook
{
    public static async Task ApplyAsync(TrimmeDbContext db, Review review, int sign, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var shop = await GetOrAddAsync(db, RatingSubjectKind.Shop, review.ShopId.Value, review, cancellationToken);
        var professional = await GetOrAddAsync(db, RatingSubjectKind.Professional, review.ProfessionalId.Value, review, cancellationToken);
        shop.Apply(review.Rating, sign, now);
        professional.Apply(review.Rating, sign, now);
    }

    public static ReviewedBooking ToReviewed(ReviewableBooking booking) =>
        new(booking.BookingId, booking.ShopId, booking.CustomerId, booking.CustomerName, booking.ProfessionalId,
            booking.ItemNameAr, booking.ItemNameEn, booking.CompletedAt);

    private static async Task<RatingAggregate> GetOrAddAsync(
        TrimmeDbContext db, RatingSubjectKind subject, Guid subjectId, Review review, CancellationToken cancellationToken)
    {
        var local = db.Set<RatingAggregate>().Local.SingleOrDefault(a => a.Subject == subject && a.SubjectId == subjectId);
        if (local is not null)
        {
            return local;
        }

        var row = await db.Set<RatingAggregate>().SingleOrDefaultAsync(a => a.Subject == subject && a.SubjectId == subjectId, cancellationToken);
        if (row is null)
        {
            row = RatingAggregate.For(subject, subjectId, review.ShopId);
            db.Add(row);
        }

        return row;
    }
}
