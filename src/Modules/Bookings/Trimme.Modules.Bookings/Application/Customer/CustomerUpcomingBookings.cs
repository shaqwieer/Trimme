using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Privacy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Bookings.Domain;

namespace Trimme.Modules.Bookings.Application.Customer;

/// <summary>
/// <see cref="ICustomerUpcomingBookings"/> (D-144): the customer's active bookings that have not ended, read in their own
/// request (the customer filter keeps it to their bookings; the explicit id makes that plain).
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
}
