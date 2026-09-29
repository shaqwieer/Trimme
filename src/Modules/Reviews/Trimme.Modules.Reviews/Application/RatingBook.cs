using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Bookings;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Reviews.Domain;

namespace Trimme.Modules.Reviews.Application;

/// <summary>
/// Keeps the rating aggregates in step with published reviews (R-RVW-01, D-092/D-097). Each change is one atomic
/// `INSERT … ON CONFLICT DO UPDATE` that adds to the counters in SQL, so concurrent reviews of the same shop or
/// professional never lose an update and the first two reviews never race on creating the row. Call it inside the
/// review's transaction, so a review and its totals commit or roll back together.
/// </summary>
internal static class RatingBook
{
    public static async Task ApplyAsync(TrimmeDbContext db, Review review, int sign, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await UpsertAsync(db, nameof(RatingSubjectKind.Shop), review.ShopId.Value, review, sign, now, cancellationToken);
        await UpsertAsync(db, nameof(RatingSubjectKind.Professional), review.ProfessionalId.Value, review, sign, now, cancellationToken);
    }

    public static ReviewedBooking ToReviewed(ReviewableBooking booking) =>
        new(booking.BookingId, booking.ShopId, booking.CustomerId, booking.CustomerName, booking.ProfessionalId,
            booking.ItemNameAr, booking.ItemNameEn, booking.CompletedAt);

    private static Task<int> UpsertAsync(
        TrimmeDbContext db, string subject, Guid subjectId, Review review, int sign, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var shopId = review.ShopId.Value;
        var sum = sign * review.Rating;
        int Star(int stars) => review.Rating == stars ? sign : 0;
        return db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO reviews.rating_aggregates AS a
                (subject, subject_id, shop_id, count, sum, stars1, stars2, stars3, stars4, stars5, updated_at)
            VALUES ({subject}, {subjectId}, {shopId}, {sign}, {sum}, {Star(1)}, {Star(2)}, {Star(3)}, {Star(4)}, {Star(5)}, {now})
            ON CONFLICT (subject, subject_id) DO UPDATE SET
                count = a.count + EXCLUDED.count,
                sum = a.sum + EXCLUDED.sum,
                stars1 = a.stars1 + EXCLUDED.stars1,
                stars2 = a.stars2 + EXCLUDED.stars2,
                stars3 = a.stars3 + EXCLUDED.stars3,
                stars4 = a.stars4 + EXCLUDED.stars4,
                stars5 = a.stars5 + EXCLUDED.stars5,
                updated_at = EXCLUDED.updated_at
            """,
            cancellationToken);
    }
}
