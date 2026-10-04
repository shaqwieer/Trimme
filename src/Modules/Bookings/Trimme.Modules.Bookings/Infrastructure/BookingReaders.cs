using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Bookings;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Notifications;
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

    public async Task<IReadOnlyList<BusyTime>> GetBusyAsync(
        IReadOnlyCollection<ShopId> shopIds, IReadOnlyCollection<ProfessionalId> professionalIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var shops = shopIds.ToArray();
        var ids = professionalIds.ToArray();
        var active = BookingRules.Active.ToArray();
        return await db.Set<Booking>().AsNoTracking()
            .Where(b => shops.Contains(b.ShopId) && active.Contains(b.Status) && ids.Contains(b.ProfessionalId) && b.StartsAt < to && b.EndsAt > from)
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

/// <summary>
/// A service in any booking (as the service, inside a booked package or among services booked together) is in use:
/// archive it, never delete (R-SVC-02).
/// </summary>
internal sealed class BookingServiceUsage(TrimmeDbContext db) : IShopServiceUsage
{
    public async Task<bool> IsInUseAsync(ShopId shopId, Guid serviceId, CancellationToken cancellationToken)
    {
        if (await db.Set<Booking>().AnyAsync(b => b.ShopId == shopId && b.ServiceId == serviceId, cancellationToken))
        {
            return true;
        }

        // Items are a JSON list on the booking: a package's services (which the package already keeps in use, but it may
        // later drop one) or the services booked together.
        return await db.Set<Booking>().AnyAsync(b => b.ShopId == shopId && b.PackageItems.Any(i => i.ServiceId == serviceId), cancellationToken);
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

/// <summary>
/// <see cref="IBookingNotificationSource"/>: the booking snapshot messages are rendered from (names, item, price, times,
/// status). Reads through the caller's scope (the system scope in notification jobs). No contact data.
/// </summary>
internal sealed class BookingNotificationSource(TrimmeDbContext db) : IBookingNotificationSource
{
    public async Task<NotifiableBooking?> FindAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        var id = new BookingId(bookingId);
        var b = await db.Set<Booking>().AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, cancellationToken);
        return b is null
            ? null
            : new NotifiableBooking(
                b.Id.Value, b.ShopId, b.CustomerId, b.CustomerName, b.Reference, b.ProfessionalId, b.ProfessionalNameAr, b.ProfessionalNameEn,
                b.ItemNameAr, b.ItemNameEn, b.Price, b.Currency, b.DurationMinutes, b.StartsAt, b.EndsAt, b.Status.ToString(), b.Channel.ToString());
    }
}

/// <summary>
/// <see cref="BuildingBlocks.Application.Qr.IQrBookingReader"/> (D-114): bookings credited to a QR scan, through the caller's scope (the shop's own for a
/// shop user; every shop's inside an admin data scope). Ids and the creation time only.
/// </summary>
internal sealed class QrBookingReader(TrimmeDbContext db) : BuildingBlocks.Application.Qr.IQrBookingReader
{
    public async Task<IReadOnlyList<BuildingBlocks.Application.Qr.QrAttributedBooking>> ListAsync(
        DateTimeOffset from, DateTimeOffset to, IReadOnlyCollection<Guid>? linkIds, CancellationToken cancellationToken)
    {
        var bookings = db.Set<Booking>().AsNoTracking().Where(b => b.QrLinkId != null && b.CreatedAt >= from && b.CreatedAt < to);
        if (linkIds is not null)
        {
            var ids = linkIds.Select(id => (QrCodeLinkId?)new QrCodeLinkId(id)).ToArray();
            bookings = bookings.Where(b => ids.Contains(b.QrLinkId));
        }

        return await bookings
            .Select(b => new BuildingBlocks.Application.Qr.QrAttributedBooking(b.Id.Value, b.ShopId, b.QrLinkId!.Value.Value, b.QrVisitId, b.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
