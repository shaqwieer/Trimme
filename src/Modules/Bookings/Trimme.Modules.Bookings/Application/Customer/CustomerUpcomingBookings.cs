using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Privacy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Bookings.Domain;

namespace Trimme.Modules.Bookings.Application.Customer;

/// <summary>
/// <see cref="ICustomerUpcomingBookings"/> (D-144): the customer's active bookings that have not ended, read in their own
/// request (the customer filter keeps it to their bookings; the explicit id makes that plain).
/// <para>
/// D-155 («حجز واحد فعّال بس للزبون، في كل المحلات»): <see cref="NextAsync"/> is the booking that stops a new one. An open
/// booking (Confirmed or Arrived; Pending is gone) at any shop whose booked end has not passed. A booking the barber did
/// not close after its end (red) does not count, nor do the shop's own walk-in and phone bookings (they carry no customer
/// account).
/// </para>
/// </summary>
internal sealed class CustomerUpcomingBookings(TrimmeDbContext db, TimeProvider clock) : ICustomerUpcomingBookings
{
    public Task<int> CountAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var active = BookingRules.Active.ToArray();
        return db.Set<Booking>().AsNoTracking()
            .CountAsync(b => b.CustomerId == customerId && active.Contains(b.Status) && b.EndsAt >= now, cancellationToken);
    }

    /// <summary>The customer's open online booking that has not ended yet, soonest first; null when they have none.</summary>
    public Task<Booking?> NextAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var active = BookingRules.Active.ToArray();
        return db.Set<Booking>().AsNoTracking()
            .Where(b => b.CustomerId == customerId && b.Channel == BookingChannel.Online && active.Contains(b.Status) && b.EndsAt > now)
            .OrderBy(b => b.StartsAt).ThenBy(b => b.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Serializes this customer's bookings (D-155): two requests at once, at the same shop or two, check one after the other,
    /// so only one of them can pass <see cref="NextAsync"/>. Held until the transaction ends.
    /// </summary>
    public Task LockAsync(Guid customerId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"trimme.customer-booking:" + customerId.ToString("N")}, 0))", cancellationToken);

    public static Error ActiveBookingExists(Booking booking) =>
        Error.Conflict("booking.active_booking_exists", "You already have an upcoming booking: cancel it to book a new one.")
            .WithDetail("bookingId", booking.Id.Value.ToString());
}
