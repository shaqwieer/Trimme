using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.BuildingBlocks.Application.Qr;

/// <summary>
/// The first-party attribution cookie (R-QR-02, D-114): HttpOnly, set by the API when a scanned code's landing page records
/// its visit, holding only the visit id. The customer booking endpoint reads it; nothing else does.
/// </summary>
public static class QrAttributionCookie
{
    public const string Name = "trimme-qr";

    /// <summary>The cookie reaches every API path (the booking endpoint) and no page path.</summary>
    public const string Path = "/api/v1";
}

/// <summary>A booking's QR origin: the code that was scanned and the visit it came from.</summary>
public sealed record QrAttribution(Guid LinkId, Guid VisitId);

/// <summary>
/// Decides whether a visit id from the attribution cookie credits a new booking (implemented by the QrAnalytics module):
/// the visit exists, it scanned a code of the booked shop, and it is within the attribution window. Anything else is an
/// ordinary booking, never an error.
/// </summary>
public interface IQrAttributionResolver
{
    Task<QrAttribution?> ResolveAsync(Guid visitId, ShopId shopId, CancellationToken cancellationToken);
}

/// <summary>A booking credited to a QR code, as the QR analytics read it (ids and the time only).</summary>
public sealed record QrAttributedBooking(Guid BookingId, ShopId ShopId, Guid LinkId, Guid? VisitId, DateTimeOffset CreatedAt);

/// <summary>
/// The bookings credited to QR codes (implemented by the Bookings module). It reads as far as the caller may see: the
/// shop's own bookings for a shop user, every shop's inside an admin data scope.
/// </summary>
public interface IQrBookingReader
{
    /// <summary>Bookings created in [<paramref name="from"/>, <paramref name="to"/>), optionally only of these codes.</summary>
    Task<IReadOnlyList<QrAttributedBooking>> ListAsync(
        DateTimeOffset from, DateTimeOffset to, IReadOnlyCollection<Guid>? linkIds, CancellationToken cancellationToken);
}

/// <summary>
/// A barber's own QR code, made automatically (D-155, the client's 7-2-3: «QR للحلاق ينسوى تلقائي»), implemented by the
/// QrAnalytics module. Called when a barber is added (by the owner or the admin) or turned on again, and by the
/// <c>migrate</c> backfill for active barbers without one.
/// </summary>
public interface IProfessionalQrCodes
{
    /// <summary>
    /// Adds a code for the professional to the caller's unit of work when they have none yet (one an admin turned off is
    /// kept, not replaced); true when one was added. The caller saves.
    /// </summary>
    Task<bool> EnsureAsync(ShopId shopId, ProfessionalId professionalId, CancellationToken cancellationToken);
}
