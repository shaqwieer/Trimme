using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trimme.BuildingBlocks.Application.Jobs;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Notifications;
using Trimme.BuildingBlocks.Application.Platform;
using Trimme.BuildingBlocks.Application.Privacy;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Privacy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Notifications.Application;
using Trimme.Modules.Notifications.Domain;

namespace Trimme.Modules.Notifications.Jobs;

/// <summary>The booking outbox payload (Bookings' <c>BookingEvent</c>, D-089): ids, times, statuses, channel and actor type.</summary>
internal sealed record BookingOutboxEvent(
    Guid BookingId,
    Guid ShopId,
    Guid ProfessionalId,
    Guid? CustomerId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Status,
    string Channel,
    DateTimeOffset? PreviousStartsAt,
    string ActorType);

/// <summary>
/// Creates a WhatsApp dispatch for one booking, event and audience (D-110), or nothing when the audience does not get
/// the event, has no usable recipient (no customer account, a disabled account, a professional without an enabled number,
/// spec §16), or a dispatch with the same dedupe key exists. It renders the active template version of the recipient's
/// locale (customers: their preferred locale; professionals: the platform default), falling back to Arabic.
/// </summary>
internal sealed partial class DispatchFactory(
    TrimmeDbContext db,
    ICustomerContactReader customers,
    IProfessionalContactReader professionals,
    IPersonalDataProtector protector,
    IOptions<MessageLinkOptions> links,
    TimeProvider clock,
    ILogger<DispatchFactory> logger)
{
    public async Task<WhatsAppDispatch?> CreateForBookingAsync(
        DispatchKind kind,
        string dedupeKey,
        MessageEvent @event,
        MessageAudience audience,
        NotifiableBooking booking,
        ShopSummary shop,
        PlatformSettingsSnapshot platform,
        CancellationToken cancellationToken)
    {
        if (!WhatsAppTemplate.IsSent(@event, audience)
            || db.Set<WhatsAppDispatch>().Local.Any(d => d.DedupeKey == dedupeKey)
            || await db.Set<WhatsAppDispatch>().AnyAsync(d => d.DedupeKey == dedupeKey, cancellationToken))
        {
            return null;
        }

        var recipient = await RecipientAsync(audience, booking, platform, cancellationToken);
        if (recipient is null)
        {
            return null;
        }

        var templates = await db.Set<WhatsAppTemplate>().Include(t => t.Versions)
            .Where(t => t.Event == @event && t.Audience == audience && (t.Locale == recipient.Value.Locale || t.Locale == "ar"))
            .ToListAsync(cancellationToken);
        var template = templates.FirstOrDefault(t => t.Locale == recipient.Value.Locale && t.ActiveVersion is not null)
                       ?? templates.FirstOrDefault(t => t.Locale == "ar" && t.ActiveVersion is not null);
        if (template?.ActiveVersion is not { } version)
        {
            LogNoTemplate(logger, @event, audience, recipient.Value.Locale);
            return null;
        }

        var baseUrl = links.Value.PublicBaseUrl;
        var message = RenderedMessage.From(
            version.Content,
            MessageComposer.Values(booking, shop, audience, template.Locale, platform.ReminderOffsetMinutes, baseUrl),
            MessageComposer.Urls(booking, shop, audience, template.Locale, baseUrl));
        var masked = PhoneNumber.TryParse(recipient.Value.E164, out var phone) ? phone.Masked : "•••";
        var dispatch = WhatsAppDispatch.Create(
            kind, dedupeKey, booking.Id, booking.ShopId, template, version,
            protector.Protect(recipient.Value.E164, NotificationPurposes.DispatchRecipient), masked, recipient.Value.Id, message, clock.GetUtcNow());
        db.Add(dispatch);
        return dispatch;
    }

    private async Task<(string E164, string Locale, Guid Id)?> RecipientAsync(
        MessageAudience audience, NotifiableBooking booking, PlatformSettingsSnapshot platform, CancellationToken cancellationToken)
    {
        if (audience == MessageAudience.Customer)
        {
            return booking.CustomerId is { } customerId
                   && await customers.FindAsync(customerId, cancellationToken) is { IsActive: true, MobileE164: { } mobile } customer
                ? (mobile, customer.PreferredLocale, customerId)
                : null;
        }

        return await professionals.FindAsync(booking.ProfessionalId, cancellationToken) is { IsActive: true, WhatsAppE164: { } number }
            ? (number, platform.DefaultLocale, booking.ProfessionalId.Value)
            : null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No active WhatsApp template for {Event} / {Audience} / {Locale}; nothing sent")]
    private static partial void LogNoTemplate(ILogger logger, MessageEvent @event, MessageAudience audience, string locale);
}

/// <summary>
/// Turns booking outbox events into WhatsApp dispatches, in-app notices and reminder jobs (spec §16, §17; R-NTF-03/04/06/09;
/// D-109…D-112). Runs once per event (the processor's processed-message record) and writes idempotently.
/// <list type="bullet">
/// <item>Customers: confirmed, pending request, rescheduled, cancelled. Professionals: confirmed (their new-booking alert),
/// rescheduled while confirmed, and cancelled only if they had been told about the booking.</item>
/// <item>Messages about an event older than <see cref="MaxMessageAge"/> are not sent (a backlog after downtime, D-111);
/// reminders are still reconciled.</item>
/// <item>Reminders follow the booking's current state: a confirmed booking has one per audience at start − offset, a
/// moved one gets new jobs and the old ones are deleted, a cancelled one has none. A reminder whose time already passed is
/// not scheduled.</item>
/// <item>In-app: the shop hears about what customers and admins did; the customer about what the shop or admins did.</item>
/// </list>
/// Nothing exists for a rolled-back change or seed data: both write no outbox message.
/// </summary>
internal sealed class BookingNotificationsConsumer(
    TrimmeDbContext db,
    IBookingNotificationSource bookings,
    IShopDirectory shops,
    IPlatformSettings settings,
    DispatchFactory factory,
    INotificationCenter center,
    IJobScheduler jobs,
    TimeProvider clock) : IOutboxConsumer
{
    public const string ConsumerName = "notifications.booking";

    /// <summary>The booking history's actor type for a shop user (Bookings' <c>ActorType.ShopUser</c>).</summary>
    private const string ShopActor = "ShopUser";
    public static readonly TimeSpan MaxMessageAge = TimeSpan.FromHours(24);

    public string Name => ConsumerName;

    public bool Handles(string messageType) => messageType is "booking.created" or "booking.rescheduled" or "booking.cancelled" or "booking.status_changed";

    public async Task HandleAsync(OutboxEnvelope message, OutboxConsumerContext context, CancellationToken cancellationToken)
    {
        var change = JsonSerializer.Deserialize<BookingOutboxEvent>(message.Payload, JsonSerializerOptions.Web);
        if (change is null || await bookings.FindAsync(change.BookingId, cancellationToken) is not { } booking
            || await shops.FindAsync(booking.ShopId, cancellationToken) is not { } shop)
        {
            return;
        }

        var platform = await settings.GetAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var dispatches = new List<WhatsAppDispatch>();
        if (now - message.OccurredAt <= MaxMessageAge)
        {
            foreach (var (@event, audience) in Plan(message.Type, change, booking))
            {
                if (audience == MessageAudience.Professional && @event == MessageEvent.BookingCancelled && !await ProfessionalWasToldAsync(booking.Id, cancellationToken))
                {
                    continue;
                }

                if (await factory.CreateForBookingAsync(
                        DispatchKind.Lifecycle, $"{message.Id:N}:{audience}", @event, audience, booking, shop, platform, cancellationToken) is { } dispatch)
                {
                    dispatches.Add(dispatch);
                }
            }

            await NoticesAsync(message, change, booking, shop, cancellationToken);
        }

        var (cancelled, scheduled) = await ReconcileRemindersAsync(booking, platform.ReminderOffsetMinutes, now, cancellationToken);

        context.AfterCommit(async ct =>
        {
            foreach (var jobId in cancelled)
            {
                jobs.Delete(jobId);
            }

            foreach (var dispatch in dispatches)
            {
                dispatch.AssignJob(SendDispatchJob.Enqueue(jobs, dispatch.Id.Value));
            }

            foreach (var reminder in scheduled)
            {
                reminder.AssignJob(BookingReminderJob.Schedule(jobs, reminder.Id.Value, reminder.DueAt));
            }

            await db.SaveChangesAsync(ct);
            await center.PushPendingAsync(ct);
        });
    }

    /// <summary>Which messages an event sends, from what happened (the event and the status it produced).</summary>
    internal static IEnumerable<(MessageEvent Event, MessageAudience Audience)> Plan(string type, BookingOutboxEvent change, NotifiableBooking booking)
    {
        switch (type)
        {
            case "booking.created" when change.Status == "Pending":
                yield return (MessageEvent.BookingPending, MessageAudience.Customer);
                break;
            case "booking.created" when change.Status is "Confirmed" or "Arrived":
            case "booking.status_changed" when change.Status == "Confirmed":
                yield return (MessageEvent.BookingConfirmed, MessageAudience.Customer);
                yield return (MessageEvent.BookingConfirmed, MessageAudience.Professional);
                break;
            case "booking.rescheduled":
                yield return (MessageEvent.BookingRescheduled, MessageAudience.Customer);
                if (booking.IsConfirmed)
                {
                    yield return (MessageEvent.BookingRescheduled, MessageAudience.Professional);
                }

                break;
            case "booking.cancelled":
                yield return (MessageEvent.BookingCancelled, MessageAudience.Customer);
                yield return (MessageEvent.BookingCancelled, MessageAudience.Professional);
                break;
        }
    }

    private Task<bool> ProfessionalWasToldAsync(Guid bookingId, CancellationToken cancellationToken) =>
        db.Set<WhatsAppDispatch>().AnyAsync(
            d => d.BookingId == bookingId && d.Audience == MessageAudience.Professional
                 && (d.Event == MessageEvent.BookingConfirmed || d.Event == MessageEvent.BookingRescheduled),
            cancellationToken);

    private async Task NoticesAsync(OutboxEnvelope message, BookingOutboxEvent change, NotifiableBooking booking, ShopSummary shop, CancellationToken cancellationToken)
    {
        var kind = message.Type switch
        {
            "booking.created" => change.Status == "Pending" ? NoticeKinds.BookingPending : NoticeKinds.BookingCreated,
            "booking.status_changed" when change.Status == "Confirmed" => NoticeKinds.BookingConfirmed,
            "booking.rescheduled" => NoticeKinds.BookingRescheduled,
            "booking.cancelled" => NoticeKinds.BookingCancelled,
            _ => null,
        };
        if (kind is null)
        {
            return;
        }

        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["reference"] = booking.Reference,
            ["itemNameAr"] = booking.ItemNameAr,
            ["itemNameEn"] = booking.ItemNameEn ?? booking.ItemNameAr,
            ["professionalNameAr"] = booking.ProfessionalNameAr,
            ["professionalNameEn"] = booking.ProfessionalNameEn,
            ["startsAt"] = booking.StartsAt.ToString("O", CultureInfo.InvariantCulture),
            ["status"] = change.Status,
            ["actor"] = change.ActorType,
        };
        if (change.PreviousStartsAt is { } previous)
        {
            parameters["previousStartsAt"] = previous.ToString("O", CultureInfo.InvariantCulture);
        }

        var dedupe = $"{message.Id:N}";

        // The shop's own actions are not echoed back to it; walk-ins are the shop's own.
        if (change.ActorType != ShopActor && change.Channel != "WalkIn")
        {
            var forShop = new Dictionary<string, string>(parameters, StringComparer.Ordinal) { ["customerName"] = booking.CustomerName };
            await center.NotifyShopAsync(booking.ShopId, new InAppNotice(kind, dedupe, forShop, booking.Id), cancellationToken);
        }

        // The customer is told what the shop or the platform did (not what they did themselves).
        if (booking.CustomerId is { } customerId && change.ActorType != "Customer" && kind != NoticeKinds.BookingCreated && kind != NoticeKinds.BookingPending)
        {
            var forCustomer = new Dictionary<string, string>(parameters, StringComparer.Ordinal)
            {
                ["shopNameAr"] = shop.NameAr,
                ["shopNameEn"] = string.IsNullOrWhiteSpace(shop.NameEn) ? shop.NameAr : shop.NameEn,
            };
            await center.NotifyUserAsync(customerId, new InAppNotice(kind, dedupe, forCustomer, booking.Id), cancellationToken);
        }
    }

    private async Task<(List<string> Cancelled, List<ReminderSchedule> Scheduled)> ReconcileRemindersAsync(
        NotifiableBooking booking, int offsetMinutes, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await db.Set<ReminderSchedule>()
            .Where(r => r.BookingId == booking.Id && r.Status == ReminderStatus.Scheduled)
            .ToListAsync(cancellationToken);
        var dueAt = booking.StartsAt.AddMinutes(-offsetMinutes);
        var cancelled = new List<string>();
        var wanted = new List<MessageAudience>();
        foreach (var audience in new[] { MessageAudience.Customer, MessageAudience.Professional })
        {
            var want = booking.IsConfirmed && dueAt > now && (audience == MessageAudience.Professional || booking.CustomerId is not null);

            // A kept reminder keeps its due time even if the offset setting changed since (D-111).
            var keep = want ? existing.FirstOrDefault(r => r.Audience == audience && r.StartsAt == booking.StartsAt) : null;
            foreach (var obsolete in existing.Where(r => r.Audience == audience && r != keep))
            {
                obsolete.Cancel(now);
                if (obsolete.JobId is { } jobId)
                {
                    cancelled.Add(jobId);
                }
            }

            if (want && keep is null)
            {
                wanted.Add(audience);
            }
        }

        // Cancellations are saved first: the partial unique index allows one Scheduled reminder per booking and audience.
        await db.SaveChangesAsync(cancellationToken);
        var scheduled = wanted.Select(a => ReminderSchedule.Schedule(booking.Id, a, booking.StartsAt, dueAt, now)).ToList();
        db.AddRange(scheduled);
        return (cancelled, scheduled);
    }
}
