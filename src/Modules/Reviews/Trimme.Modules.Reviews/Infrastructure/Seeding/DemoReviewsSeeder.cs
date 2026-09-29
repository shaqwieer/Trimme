using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trimme.BuildingBlocks.Application.Bookings;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.Modules.Reviews.Application;
using Trimme.Modules.Reviews.Domain;

namespace Trimme.Modules.Reviews.Infrastructure.Seeding;

/// <summary>
/// The demo customers' reviews of their completed visits (spec §20, D-092), with the rating aggregates. Idempotent (one
/// review per booking; the id is derived from the booking's); development only.
/// </summary>
internal sealed class DemoReviewsSeeder : IDevSeeder
{
    /// <summary>After bookings (400).</summary>
    public int Order => 450;

    public string Name => "reviews-demo";

    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<TrimmeDbContext>();
        var bookings = services.GetRequiredService<IBookingReviewSource>();
        using var scope = services.GetRequiredService<ISystemDataScope>().Begin();

        foreach (var visit in DemoVisits.All)
        {
            if (await db.Set<Review>().AnyAsync(r => r.BookingId == visit.BookingId, cancellationToken)
                || await bookings.FindAsync(visit.BookingId, cancellationToken) is not { CompletedAt: { } completedAt } booking)
            {
                continue;
            }

            // Written two hours after the visit, so the demo history reads naturally.
            var writtenAt = completedAt.AddHours(2);
            var review = Review.Create(ReviewIdFor(visit.BookingId), RatingBook.ToReviewed(booking), visit.Rating, visit.Comment, writtenAt).Value;
            db.Add(review);
            await RatingBook.ApplyAsync(db, review, +1, writtenAt, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>A stable id per booking: the booking id with its version nibble kept and the last byte flipped.</summary>
    private static ReviewId ReviewIdFor(Guid bookingId)
    {
        var bytes = bookingId.ToByteArray();
        bytes[15] ^= 0xFF;
        return new ReviewId(new Guid(bytes));
    }
}
