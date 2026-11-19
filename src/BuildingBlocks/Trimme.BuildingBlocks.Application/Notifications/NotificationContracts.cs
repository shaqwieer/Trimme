using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.BuildingBlocks.Application.Notifications;

// Ports the Notifications module uses to build messages from other modules' data (Phase 15, D-108…D-112). The three
// contact readers return phone numbers, so an architecture rule limits them to background jobs (*.Jobs namespaces):
// no endpoint can read a customer's, a walk-in's or a professional's number through them.

/// <summary>A booking as notifications need it (implemented by the Bookings module); reads through the caller's scope.</summary>
public interface IBookingNotificationSource
{
    Task<NotifiableBooking?> FindAsync(Guid bookingId, CancellationToken cancellationToken);
}

/// <summary>
/// The booking snapshot a message is rendered from. No contact data by construction: recipients are resolved separately
/// through <see cref="ICustomerContactReader"/> and <see cref="IProfessionalContactReader"/>. <c>Status</c> is the booking
/// status name (<c>Pending</c>, <c>Confirmed</c>, <c>Arrived</c>, <c>Completed</c>, <c>CancelledByCustomer</c>,
/// <c>CancelledByShop</c>, <c>NoShow</c>); <c>Channel</c> is <c>Online</c> or <c>WalkIn</c>. <c>AmountDue</c> is what the
/// customer pays at the shop, net of a loyalty reward (D-152: the message's amount, not the booked price).
/// <c>CancellationReason</c> and <c>CancellationReasonCode</c> (D-155): what the shop gave when it cancelled, the typed
/// text and the ready reason (<c>StaffUnwell</c>, <c>Emergency</c>, <c>ShopClosed</c>, <c>Other</c>), for the shop's
/// cancellation message.
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
    decimal AmountDue,
    string Currency,
    int DurationMinutes,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Status,
    string Channel,
    string? CancellationReason = null,
    string? CancellationReasonCode = null)
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
/// preferred locale (<c>ar</c> or <c>en</c>). <c>WinBackMessages</c> (D-155): whether they still want the shops'
/// «ندعوك للعودة» messages (on by default; the customer turns it off on «حسابي»).
/// </summary>
public sealed record CustomerContact(Guid Id, string? DisplayName, string? MobileE164, string PreferredLocale, bool IsActive, bool WinBackMessages = true);

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

/// <summary>
/// The mobile number the shop took for a walk-in (D-134), for its WhatsApp confirmation (D-145; implemented by the
/// Bookings module, which holds it encrypted). Null when none was given or it was erased. Only the notification jobs may
/// use it (architecture rule); the number never enters the outbox, a notice or a log.
/// </summary>
public interface IWalkInContactReader
{
    Task<string?> FindMobileAsync(Guid bookingId, CancellationToken cancellationToken);
}

/// <summary>Accounts for notification fan-out (implemented by the Identity module). Ids only.</summary>
public interface IStaffDirectory
{
    /// <summary>Enabled platform admins whose roles grant <paramref name="permission"/>.</summary>
    Task<IReadOnlyList<Guid>> ActiveAdminsWithPermissionAsync(string permission, CancellationToken cancellationToken);

    /// <summary>The enabled login of a barber (D-145), if the professional has one; their in-app notices go to it.</summary>
    Task<Guid?> BarberAccountAsync(ProfessionalId professionalId, CancellationToken cancellationToken);

    /// <summary>
    /// Whether the account is a barber's login (any barber of any shop): the owner hears what a barber did, also on a
    /// colleague's booking (D-147), but not their own actions.
    /// </summary>
    Task<bool> IsBarberAsync(Guid userId, CancellationToken cancellationToken);
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

/// <summary>
/// A shop's marketing terms (implemented by the Subscriptions module, D-155), from the plan of the subscription period in
/// force, read through the caller's data scope (the jobs' system scope, or the shop's own):
/// <list type="bullet">
/// <item><c>WindowStart</c>..<c>WindowEnd</c> (inclusive): the month the limit counts, running from the day of the period's
/// start (the client's «العدّاد يرجع صفر كل شهر، من تاريخ اشتراك المحل»); the calendar month without a subscription.</item>
/// <item><c>Allowance</c>: the plan's monthly marketing messages; null is unlimited, 0 none.</item>
/// <item><c>WinBackEnabled</c> and <c>WinBackAfterDays</c>: whether the plan includes «رسالة الدعوة للعودة» and after how
/// many days without a visit. Off without a subscription in force (expired or suspended).</item>
/// </list>
/// </summary>
public sealed record MarketingTerms(DateOnly WindowStart, DateOnly WindowEnd, int? Allowance, bool WinBackEnabled, int WinBackAfterDays);

public interface IMarketingPlans
{
    Task<MarketingTerms> ForShopAsync(ShopId shopId, CancellationToken cancellationToken);

    /// <summary>The shops whose plan in force today includes the win-back message, each with its number of days.</summary>
    Task<IReadOnlyList<WinBackShop>> WinBackShopsAsync(CancellationToken cancellationToken);
}

public sealed record WinBackShop(ShopId ShopId, int AfterDays);

/// <summary>
/// How many marketing messages (D-155: the win-back message only) a shop handed to the provider in a window (implemented
/// by the Notifications module), through the caller's data scope (the shop's own, or an admin's).
/// </summary>
public interface IMarketingUsage
{
    Task<int> UsedAsync(ShopId shopId, DateOnly windowStart, CancellationToken cancellationToken);
}

/// <summary>The monthly window a marketing limit counts (D-155): from the anchor day, month after month.</summary>
public static class MarketingWindows
{
    /// <summary>The window containing <paramref name="today"/>, counted in whole months from <paramref name="anchor"/>.</summary>
    public static (DateOnly Start, DateOnly End) Of(DateOnly anchor, DateOnly today)
    {
        var months = ((today.Year - anchor.Year) * 12) + today.Month - anchor.Month;
        if (anchor.AddMonths(months) > today)
        {
            months--;
        }

        months = Math.Max(0, months);
        return (anchor.AddMonths(months), anchor.AddMonths(months + 1).AddDays(-1));
    }

    /// <summary>The calendar month of <paramref name="today"/> (a shop without a subscription).</summary>
    public static (DateOnly Start, DateOnly End) Calendar(DateOnly today)
    {
        var start = new DateOnly(today.Year, today.Month, 1);
        return (start, start.AddMonths(1).AddDays(-1));
    }
}

/// <summary>
/// The customers a shop may invite back (implemented by the Bookings module, D-155), read through the caller's scope (the
/// job's system scope): account customers whose latest completed online visit at the shop started between
/// <c>from</c> and <c>to</c>, and who have no open booking at any shop. Ids only.
/// </summary>
public interface IWinBackCandidates
{
    Task<IReadOnlyList<WinBackCandidate>> ListAsync(ShopId shopId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

/// <summary>A customer to invite back, and their last completed booking at the shop (the message is about it).</summary>
public sealed record WinBackCandidate(Guid CustomerId, Guid LastBookingId);
