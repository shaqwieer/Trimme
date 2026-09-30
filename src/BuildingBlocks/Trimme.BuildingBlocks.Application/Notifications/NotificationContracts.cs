using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.BuildingBlocks.Application.Notifications;

// Ports the Notifications module uses to build messages from other modules' data (Phase 15, D-108…D-112). The two
// contact readers return phone numbers, so an architecture rule limits them to background jobs (*.Jobs namespaces):
// no endpoint can read a customer's or a professional's number through them.

/// <summary>A booking as notifications need it (implemented by the Bookings module); reads through the caller's scope.</summary>
public interface IBookingNotificationSource
{
    Task<NotifiableBooking?> FindAsync(Guid bookingId, CancellationToken cancellationToken);
}

/// <summary>
/// The booking snapshot a message is rendered from. No contact data by construction: recipients are resolved separately
/// through <see cref="ICustomerContactReader"/> and <see cref="IProfessionalContactReader"/>. <c>Status</c> is the booking
/// status name (<c>Pending</c>, <c>Confirmed</c>, <c>Arrived</c>, <c>Completed</c>, <c>CancelledByCustomer</c>,
/// <c>CancelledByShop</c>, <c>NoShow</c>); <c>Channel</c> is <c>Online</c> or <c>WalkIn</c>.
/// </summary>
public sealed record NotifiableBooking(
    Guid Id,
    ShopId ShopId,
    Guid? CustomerId,
    string CustomerName,
    string Reference,
    ProfessionalId ProfessionalId,
    string ProfessionalNameAr,
    string ProfessionalNameEn,
    string ItemNameAr,
    string? ItemNameEn,
    decimal Price,
    string Currency,
    int DurationMinutes,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Status,
    string Channel)
{
    /// <summary>Pending, Confirmed or Arrived: the booking still holds its time.</summary>
    public bool IsActive => Status is "Pending" or "Confirmed" or "Arrived";

    /// <summary>Confirmed or Arrived: the professional has been told and reminders apply.</summary>
    public bool IsConfirmed => Status is "Confirmed" or "Arrived";

    public bool IsCancelled => Status is "CancelledByCustomer" or "CancelledByShop";
}

/// <summary>
/// A customer's WhatsApp recipient (implemented by the Identity module, which holds the encrypted mobile). Only the
/// notification jobs may use it (architecture rule).
/// </summary>
public interface ICustomerContactReader
{
    Task<CustomerContact?> FindAsync(Guid customerId, CancellationToken cancellationToken);
}

/// <summary>
/// Whether a number belongs to a registered customer (implemented by the Identity module through the keyed lookup hash).
/// The admin test send refuses such a number, so a production customer never receives a test by accident (R-NTF-08).
/// It answers yes or no only.
/// </summary>
public interface ICustomerNumberCheck
{
    Task<bool> IsCustomerNumberAsync(string phoneE164, CancellationToken cancellationToken);
}

/// <summary>
/// A customer's recipient data: the verified mobile in E.164 (never logged, never returned by an endpoint) and the
/// preferred locale (<c>ar</c> or <c>en</c>).
/// </summary>
public sealed record CustomerContact(Guid Id, string? DisplayName, string? MobileE164, string PreferredLocale, bool IsActive);

/// <summary>
/// A professional's WhatsApp recipient (implemented by the Professionals module). Reads through the caller's scope
/// (the system scope in jobs). Only the notification jobs may use it (architecture rule).
/// </summary>
public interface IProfessionalContactReader
{
    Task<ProfessionalContactCard?> FindAsync(ProfessionalId professionalId, CancellationToken cancellationToken);

    /// <summary>Records what the provider reported for the professional's number (delivered: verified; failed: failed).</summary>
    Task RecordDeliveryAsync(ProfessionalId professionalId, bool delivered, CancellationToken cancellationToken);
}

/// <summary>
/// A professional's recipient data. <c>WhatsAppE164</c> is set only when the professional can receive notifications (a
/// number is set and notifications are on, spec §16); otherwise it is null.
/// </summary>
public sealed record ProfessionalContactCard(ProfessionalId Id, ShopId ShopId, string NameAr, string NameEn, bool IsActive, string? WhatsAppE164);

/// <summary>Platform admins for notification fan-out (implemented by the Identity module). Ids only.</summary>
public interface IStaffDirectory
{
    /// <summary>Enabled platform admins whose roles grant <paramref name="permission"/>.</summary>
    Task<IReadOnlyList<Guid>> ActiveAdminsWithPermissionAsync(string permission, CancellationToken cancellationToken);
}

/// <summary>
/// In-app notifications (spec §17, R-NTF-10; implemented by the Notifications module). The notify methods add rows to
/// the caller's unit of work, skipping a notice whose dedupe key the recipient already has; the caller saves, then calls
/// <see cref="PushPendingAsync"/> after the commit to tell connected clients. Notices carry a kind and parameters that the
/// web app renders in the reader's language: no text is composed on the server, and never a phone number.
/// </summary>
public interface INotificationCenter
{
    Task NotifyShopAsync(ShopId shopId, InAppNotice notice, CancellationToken cancellationToken);

    Task NotifyUserAsync(Guid userId, InAppNotice notice, CancellationToken cancellationToken);

    /// <summary>Notifies every enabled admin who holds <paramref name="permission"/>.</summary>
    Task NotifyAdminsAsync(string permission, InAppNotice notice, CancellationToken cancellationToken);

    /// <summary>Signals the recipients of the notices added so far (call after the commit; best effort).</summary>
    Task PushPendingAsync(CancellationToken cancellationToken);
}

/// <param name="Kind">Stable kind, for example <c>booking.created</c> or <c>subscription.expiring</c>.</param>
/// <param name="DedupeKey">Unique per recipient; a second notice with the same key is ignored.</param>
/// <param name="Parameters">Values the web message uses (names, times as ISO 8601, counts). Never contact data.</param>
/// <param name="BookingId">The booking it is about, if any (the web links to it).</param>
public sealed record InAppNotice(string Kind, string DedupeKey, IReadOnlyDictionary<string, string> Parameters, Guid? BookingId = null);

/// <summary>Who has new in-app notifications (a user, or every user of a shop).</summary>
public sealed record NotificationAudience(Guid? UserId, ShopId? ShopId);

/// <summary>Tells connected clients that an audience has new notifications (implemented over SignalR by the host).</summary>
public interface INotificationsPush
{
    Task PushAsync(IReadOnlyCollection<NotificationAudience> audiences, CancellationToken cancellationToken);
}

/// <summary>
/// Sends a sign-in code through the WhatsApp authentication template (implemented by the Notifications module; used by
/// the Identity module's WhatsApp OTP sender). The code is never stored or logged.
/// </summary>
public interface IWhatsAppAuthenticationSender
{
    Task<bool> TrySendCodeAsync(string phoneE164, string code, string locale, CancellationToken cancellationToken);
}
