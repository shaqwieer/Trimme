using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Bookings;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Platform;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Reviews.Domain;

namespace Trimme.Modules.Reviews.Application.Customer;

/// <summary>The review the customer just published (shown to them; the public list shows first name + initial only).</summary>
public sealed record MyReviewResponse(Guid Id, Guid BookingId, int Rating, IReadOnlyList<ReviewTag> Tags, string? Comment, DateTimeOffset CreatedAt);

internal sealed record SubmitReviewCommand(Guid BookingId, int Rating, IReadOnlyList<ReviewTag> Tags, string? Comment) : ICommand<Result<MyReviewResponse>>;

/// <summary>
/// A customer reviews one of their own completed bookings, once, within the review window (R-CUS-09, D-017, D-097):
/// <list type="bullet">
/// <item>the booking is read through the customer's own data scope, so another customer's booking is simply not found (404);</item>
/// <item>not Completed → 409 <c>review.booking_not_completed</c>; after <c>ReviewWindowDays</c> → 422 <c>review.window_closed</c>;</item>
/// <item>a second review → 409 <c>review.already_exists</c>, also when two submissions race (the unique booking index decides);</item>
/// <item>the review and both rating aggregates are written in one transaction (R-RVW-01), and the public cache is evicted after
/// the commit.</item>
/// </list>
/// </summary>
internal sealed class SubmitReviewHandler(
    TrimmeDbContext db,
    ICurrentCustomer customer,
    IBookingReviewSource bookings,
    IPlatformSettings settings,
    IPublicContentChangeSink publicContent,
    TimeProvider clock) : ICommandHandler<SubmitReviewCommand, Result<MyReviewResponse>>
{
    public async Task<Result<MyReviewResponse>> Handle(SubmitReviewCommand command, CancellationToken cancellationToken)
    {
        if (customer.CustomerId is not { } customerId
            || await bookings.FindAsync(command.BookingId, cancellationToken) is not { } booking
            || booking.CustomerId != customerId)
        {
            return ReviewErrors.BookingNotFound();
        }

        if (booking.CompletedAt is not { } completedAt)
        {
            return ReviewErrors.NotCompleted();
        }

        var now = clock.GetUtcNow();
        var window = (await settings.GetAsync(cancellationToken)).ReviewWindowDays;
        if (now > completedAt.AddDays(window))
        {
            return ReviewErrors.WindowClosed();
        }

        if (await db.Set<Review>().AnyAsync(r => r.BookingId == command.BookingId, cancellationToken))
        {
            return ReviewErrors.AlreadyExists();
        }

        var created = Review.Create(new ReviewId(Guid.CreateVersion7()), RatingBook.ToReviewed(booking), command.Rating, command.Comment, now, command.Tags);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var review = created.Value;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                db.Add(review);
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (DatabaseErrors.IsUniqueViolation(exception))
            {
                return ReviewErrors.AlreadyExists();
            }

            await RatingBook.ApplyAsync(db, review, +1, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        // The save above already evicted the public pages once, before the commit; evict again now the totals are visible.
        await publicContent.PublicContentChangedAsync(cancellationToken);
        return new MyReviewResponse(review.Id.Value, review.BookingId, review.Rating, review.Tags, review.Comment, review.CreatedAt);
    }
}

/// <summary><see cref="IReviewLookup"/>: the caller's own reviews by booking (the customer filter shows only theirs).</summary>
internal sealed class ReviewLookup(TrimmeDbContext db) : IReviewLookup
{
    public async Task<IReadOnlyDictionary<Guid, int>> RatingsByBookingAsync(IReadOnlyCollection<Guid> bookingIds, CancellationToken cancellationToken)
    {
        if (bookingIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        var ids = bookingIds.Distinct().ToArray();
        return await db.Set<Review>().AsNoTracking()
            .Where(r => ids.Contains(r.BookingId))
            .ToDictionaryAsync(r => r.BookingId, r => r.Rating, cancellationToken);
    }
}
