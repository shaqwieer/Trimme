using Trimme.BuildingBlocks.Application.Notifications;
using Trimme.BuildingBlocks.Application.Privacy;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.Modules.Notifications.Domain;

namespace Trimme.Modules.Notifications.Application;

/// <summary>
/// A WhatsApp provider (spec §16, R-NTF-01, D-110): the development fake, or the Meta Cloud API in production. It never
/// logs the recipient, the access token or the message text.
/// </summary>
public interface IWhatsAppProvider
{
    /// <summary><c>Fake</c>, <c>Meta</c> or <c>None</c>.</summary>
    string Name { get; }

    Task<WhatsAppSendResult> SendAsync(WhatsAppOutgoing message, CancellationToken cancellationToken);

    /// <summary>Sends a sign-in code through the approved authentication template; the code is never stored or logged.</summary>
    Task<WhatsAppSendResult> SendAuthenticationCodeAsync(string toE164, string code, string locale, CancellationToken cancellationToken);
}

/// <summary>A rendered message for one recipient (the parameters are the placeholder values in body order).</summary>
public sealed record WhatsAppOutgoing(
    string ToE164, string Locale, string? TemplateName, string Body, IReadOnlyList<string> Parameters, IReadOnlyList<RenderedButton> Buttons);

/// <summary>
/// The provider's answer. Accepted: its message id, and whether it already reports delivery (the fake does). Refused: a
/// stable error code and whether retrying can help (<c>Permanent</c> = no, for example an invalid number or template).
/// </summary>
public sealed record WhatsAppSendResult(bool Accepted, string? ProviderMessageId, bool Delivered, bool Permanent, string? Error)
{
    public static WhatsAppSendResult Ok(string providerMessageId, bool delivered = false) => new(true, providerMessageId, delivered, false, null);

    public static WhatsAppSendResult Transient(string error) => new(false, null, false, false, error);

    public static WhatsAppSendResult Refused(string error) => new(false, null, false, true, error);
}

/// <summary>Where the web app lives, for manage-booking and shop links in messages. Bound from <c>Web</c>.</summary>
public sealed class MessageLinkOptions
{
    public const string SectionName = "Web";

    public string PublicBaseUrl { get; set; } = "http://localhost:3000";
}

public static class NotificationPurposes
{
    /// <summary>Data Protection purpose of the recipient number stored on a dispatch.</summary>
    public const string DispatchRecipient = "trimme.whatsapp-recipient";
}

/// <summary>
/// Builds the placeholder values of a message in the reader's language (D-040, D-109): names in that language (Arabic as
/// the fallback), date and time in the shop's time zone, the reminder offset, the duration, the amount, the address, the
/// booking reference and, for customers only, the manage-booking link. No phone number is ever a value.
/// </summary>
public static class MessageComposer
{
    public static IReadOnlyDictionary<string, string> Values(
        NotifiableBooking booking, ShopSummary shop, MessageAudience audience, string locale, int reminderMinutes, string publicBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(booking);
        ArgumentNullException.ThrowIfNull(shop);
        var arabic = MessageFormat.IsArabic(locale);
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Placeholders.CustomerName] = booking.CustomerName,
            [Placeholders.ProfessionalName] = arabic || string.IsNullOrWhiteSpace(booking.ProfessionalNameEn) ? booking.ProfessionalNameAr : booking.ProfessionalNameEn,
            [Placeholders.ShopName] = arabic || string.IsNullOrWhiteSpace(shop.NameEn) ? shop.NameAr : shop.NameEn,
            [Placeholders.ServiceName] = arabic || string.IsNullOrWhiteSpace(booking.ItemNameEn) ? booking.ItemNameAr : booking.ItemNameEn,
            [Placeholders.BookingDate] = MessageFormat.Date(booking.StartsAt, shop.TimeZone, locale),
            [Placeholders.BookingTime] = MessageFormat.Time(booking.StartsAt, shop.TimeZone, locale),
            [Placeholders.TimeRemaining] = MessageFormat.Minutes(reminderMinutes, locale),
            [Placeholders.Duration] = MessageFormat.Minutes(booking.DurationMinutes, locale),
            [Placeholders.Amount] = MessageFormat.Amount(booking.Price, booking.Currency, locale),
            [Placeholders.Address] = shop.Address ?? string.Empty,
            [Placeholders.BookingReference] = booking.Reference,
        };
        if (audience == MessageAudience.Customer)
        {
            values[Placeholders.ManageUrl] = ManageUrl(publicBaseUrl, locale, booking.Id);
        }

        return values;
    }

    public static IReadOnlyDictionary<TemplateButtonTarget, string> Urls(NotifiableBooking booking, ShopSummary shop, MessageAudience audience, string locale, string publicBaseUrl)
    {
        var urls = new Dictionary<TemplateButtonTarget, string> { [TemplateButtonTarget.ShopPage] = ShopUrl(publicBaseUrl, locale, shop.Slug) };
        if (audience == MessageAudience.Customer)
        {
            urls[TemplateButtonTarget.ManageBooking] = ManageUrl(publicBaseUrl, locale, booking.Id);
        }

        return urls;
    }

    private static string ManageUrl(string baseUrl, string locale, Guid bookingId) => $"{baseUrl.TrimEnd('/')}/{locale}/account/bookings/{bookingId}";

    private static string ShopUrl(string baseUrl, string locale, string slug) => $"{baseUrl.TrimEnd('/')}/{locale}/shops/{slug}";
}

/// <summary>
/// One attempt to send a queued dispatch (D-110): decrypts the recipient only for the provider call and records the
/// outcome. Returns whether another automatic attempt should follow (a transient failure before the limit).
/// </summary>
internal sealed class DispatchSender(IWhatsAppProvider provider, IPersonalDataProtector protector, TimeProvider clock)
{
    public async Task<bool> AttemptAsync(WhatsAppDispatch dispatch, CancellationToken cancellationToken)
    {
        if (!dispatch.CanAttempt || dispatch.Body is null)
        {
            return false;
        }

        var to = protector.Unprotect(dispatch.RecipientProtected, NotificationPurposes.DispatchRecipient);
        WhatsAppSendResult result;
        try
        {
            result = await provider.SendAsync(
                new WhatsAppOutgoing(to, dispatch.Locale, dispatch.ProviderTemplateName, dispatch.Body, dispatch.Parameters, dispatch.Buttons), cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            result = WhatsAppSendResult.Transient($"provider.unreachable: {exception.GetType().Name}");
        }

        if (result.Accepted)
        {
            dispatch.RecordAccepted(result.ProviderMessageId ?? string.Empty, result.Delivered, clock.GetUtcNow());
            return false;
        }

        return dispatch.RecordFailure(result.Error ?? "provider.failed", result.Permanent, clock.GetUtcNow());
    }

    /// <summary>Backoff between automatic attempts: 30 s, 2 min, 10 min, 30 min.</summary>
    public static TimeSpan Backoff(int attempts) => attempts switch
    {
        <= 1 => TimeSpan.FromSeconds(30),
        2 => TimeSpan.FromMinutes(2),
        3 => TimeSpan.FromMinutes(10),
        _ => TimeSpan.FromMinutes(30),
    };
}
