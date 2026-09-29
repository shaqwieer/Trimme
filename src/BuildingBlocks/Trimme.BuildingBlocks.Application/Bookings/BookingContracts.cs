using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.BuildingBlocks.Application.Bookings;

/// <summary>
/// A booking as the Reviews module needs it (implemented by the Bookings module, D-092): who booked, which professional,
/// the booked item's snapshot name and whether it was completed. It reads through the caller's data scope (a customer
/// sees only their own bookings, D-085). No contact data by construction.
/// </summary>
public interface IBookingReviewSource
{
    Task<ReviewableBooking?> FindAsync(Guid bookingId, CancellationToken cancellationToken);
}

/// <summary>
/// A booking with the customer name snapshotted on it; <c>CompletedAt</c> is when the shop marked it Completed (null when
/// it is not Completed).
/// </summary>
public sealed record ReviewableBooking(
    Guid BookingId,
    ShopId ShopId,
    Guid? CustomerId,
    string CustomerName,
    ProfessionalId ProfessionalId,
    string ItemNameAr,
    string? ItemNameEn,
    DateTimeOffset? CompletedAt);

/// <summary>
/// The customer's own submitted reviews, by booking (implemented by the Reviews module, D-097). It reads through the caller's
/// data scope, so a customer sees only their own. Used to show "rated ★ n" and hide the review action.
/// </summary>
public interface IReviewLookup
{
    Task<IReadOnlyDictionary<Guid, int>> RatingsByBookingAsync(IReadOnlyCollection<Guid> bookingIds, CancellationToken cancellationToken);
}
