using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Bookings;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Scheduling;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Bookings.Domain;

namespace Trimme.Modules.Bookings.Infrastructure;

/// <summary>
/// The real <see cref="IBookedTimeReader"/> (replaces the Phase 09 stand-in): active bookings (Pending, Confirmed,
/// Arrived) hold the professional's time. It reads through the caller's scope; the availability paths open the right
/// one (public scope for a customer, the tenant for a shop).
/// </summary>
internal sealed class BookedTimeReader(TrimmeDbContext db) : IBookedTimeReader
{
    public async Task<IReadOnlyList<BusyTime>> GetBusyAsync(
        ShopId shopId, IReadOnlyCollection<ProfessionalId> professionalIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var ids = professionalIds.ToArray();
        return await Active(shopId)
            .Where(b => ids.Contains(b.ProfessionalId) && b.StartsAt < to && b.EndsAt > from)
            .Select(b => new BusyTime(b.Id.Value, b.ProfessionalId, b.StartsAt, b.EndsAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BookedAppointment>> GetAppointmentsAsync(
        ShopId shopId, ProfessionalId? professionalId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        await Active(shopId)
            .Where(b => (professionalId == null || b.ProfessionalId == professionalId) && b.StartsAt < to && b.EndsAt > from)
            .OrderBy(b => b.StartsAt)
            .Select(b => new BookedAppointment(b.Id.Value, b.ProfessionalId, b.StartsAt, b.EndsAt, b.CustomerName, b.ItemNameAr, b.ItemNameEn))
            .ToListAsync(cancellationToken);

    private IQueryable<Booking> Active(ShopId shopId)
    {
        var active = BookingRules.Active.ToArray();
        return db.Set<Booking>().AsNoTracking().Where(b => b.ShopId == shopId && active.Contains(b.Status));
    }
}

/// <summary>A service in any booking (as the service or inside a booked package) is in use: archive it, never delete (R-SVC-02).</summary>
internal sealed class BookingServiceUsage(TrimmeDbContext db) : IShopServiceUsage
{
    public async Task<bool> IsInUseAsync(ShopId shopId, Guid serviceId, CancellationToken cancellationToken)
    {
        if (await db.Set<Booking>().AnyAsync(b => b.ShopId == shopId && b.ServiceId == serviceId, cancellationToken))
        {
            return true;
        }

        // Package items are a JSON list on the booking; they reference services the package already keeps in use, but a
        // package may later drop an item, so check the booked snapshots too.
        return await db.Set<Booking>().AnyAsync(b => b.ShopId == shopId && b.PackageId != null && b.PackageItems.Any(i => i.ServiceId == serviceId), cancellationToken);
    }
}

/// <summary>
/// <see cref="IBookingReviewSource"/> for the Reviews module (D-092). Reads through the caller's data scope, so a customer
/// finds only their own bookings (D-085). The completion time is the history entry that moved the booking to Completed.
/// </summary>
internal sealed class BookingReviewSource(TrimmeDbContext db) : IBookingReviewSource
{
    public async Task<ReviewableBooking?> FindAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        var id = new BookingId(bookingId);
        var booking = await db.Set<Booking>().AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (booking is null)
        {
            return null;
        }

        var completedAt = booking.Status == BookingStatus.Completed
            ? booking.History.Where(h => h.ToStatus == BookingStatus.Completed).Select(h => (DateTimeOffset?)h.OccurredAt).LastOrDefault()
            : null;
        return new ReviewableBooking(
            booking.Id.Value, booking.ShopId, booking.CustomerId, booking.CustomerName, booking.ProfessionalId,
            booking.ItemNameAr, booking.ItemNameEn, completedAt);
    }
}
