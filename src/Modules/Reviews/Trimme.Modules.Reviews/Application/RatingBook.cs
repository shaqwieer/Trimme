using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Bookings;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Reviews.Domain;

namespace Trimme.Modules.Reviews.Application;

/// <summary>
/// Keeps the rating aggregates in step with published reviews (R-RVW-01, D-092/D-097). Each change is one atomic
/// `INSERT … ON CONFLICT DO UPDATE` that adds to the counters in SQL, so concurrent reviews of the same shop or
/// professional never lose an update and the first two reviews never race on creating the row. Call it inside the
/// review's transaction, so a review and its totals commit or roll back together. A moderator's hide subtracts with a
/// plain UPDATE of the existing row, and a publish adds it back through the same upsert (D-102).
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

        // Taking a review off (a moderator hides it, D-102) only ever lowers an existing row. It cannot go through the
        // upsert: PostgreSQL checks the CHECK constraints on the proposed insert row (a count of −1) before ON CONFLICT.
        if (sign < 0)
        {
            return db.Database.ExecuteSqlAsync(
                $"""
                UPDATE reviews.rating_aggregates SET
                    count = count + {sign},
                    sum = sum + {sum},
                    stars1 = stars1 + {Star(1)},
                    stars2 = stars2 + {Star(2)},
                    stars3 = stars3 + {Star(3)},
                    stars4 = stars4 + {Star(4)},
                    stars5 = stars5 + {Star(5)},
                    updated_at = {now}
                WHERE subject = {subject} AND subject_id = {subjectId}
                """,
                cancellationToken);
        }

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
