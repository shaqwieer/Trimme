using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Jobs;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Notifications;
using Trimme.BuildingBlocks.Application.Platform;
using Trimme.BuildingBlocks.Application.Privacy;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Privacy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Notifications.Application;
using Trimme.Modules.Notifications.Domain;

namespace Trimme.Modules.Notifications.Jobs;

/// <summary>
/// The booking outbox payload (Bookings' <c>BookingEvent</c>, D-089): ids, times, statuses, channel and actor type; the
/// previous barber when the change gave the booking to another one, and the acting account (D-145).
/// </summary>
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
    string ActorType,
    Guid? PreviousProfessionalId = null,
    Guid? ActorId = null)
{
    /// <summary>The barber the booking left, when this change gave it to another one (D-145).</summary>
    public Guid? LeftProfessionalId => PreviousProfessionalId is { } previous && previous != ProfessionalId ? previous : null;
}

/// <summary>
/// Creates a WhatsApp dispatch for one booking, event and audience (D-110), or nothing when the audience does not get
/// the event, has no usable recipient (no customer account, a disabled account, a professional without an enabled number,
/// spec §16), or a dispatch with the same dedupe key exists. It renders the active template version of the recipient's
/// locale (customers: their preferred locale; walk-ins and professionals: the platform default), falling back to Arabic.
/// A walk-in whose mobile the shop took (D-134) is told that it is booked, moved or cancelled (D-145): it has no account,
/// so no reminder, no offer to change it after a change of barber, and its manage button opens the shop's page.
/// </summary>
internal sealed partial class DispatchFactory(
    TrimmeDbContext db,
    ICustomerContactReader customers,
    IWalkInContactReader walkIns,
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

        // The platform turned this message off for the shop (D-142). Read when the message is due, so switching it back on
        // also brings back reminders that were already scheduled.
        if (await db.Set<ShopMessageOptOut>().AnyAsync(
                o => o.ShopId == booking.ShopId && o.Audience == audience && o.Event == @event, cancellationToken))
        {
            LogTurnedOff(logger, @event, audience);
            return null;
        }

        var recipient = await RecipientAsync(@event, audience, booking, platform, cancellationToken);
        if (recipient is null)
        {
            return null;
        }

        // A barber's message names them as they are named now (the barber a booking left is no longer its professional).
        if (recipient.Value.Professional is { } card)
        {
            booking = booking with { ProfessionalNameAr = card.NameAr, ProfessionalNameEn = card.NameEn };
        }

        // The recipient's language first; a language without templates (Turkish, D-132) falls back to English, then Arabic.
        string[] locales = recipient.Value.Locale is "ar" or "en" ? [recipient.Value.Locale, "ar"] : [recipient.Value.Locale, "en", "ar"];
        var templates = await db.Set<WhatsAppTemplate>().Include(t => t.Versions)
            .Where(t => t.Event == @event && t.Audience == audience && locales.Contains(t.Locale))
            .ToListAsync(cancellationToken);
        var template = locales
            .Select(locale => templates.FirstOrDefault(t => t.Locale == locale && t.ActiveVersion is not null))
            .FirstOrDefault(t => t is not null);
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

    private async Task<Recipient?> RecipientAsync(
        MessageEvent @event, MessageAudience audience, NotifiableBooking booking, PlatformSettingsSnapshot platform, CancellationToken cancellationToken)
    {
        if (audience == MessageAudience.Customer)
        {
            if (booking.CustomerId is { } customerId)
            {
                return await customers.FindAsync(customerId, cancellationToken) is { IsActive: true, MobileE164: { } mobile } customer
                    ? new Recipient(mobile, customer.PreferredLocale, customerId, null)
                    : null;
            }

            return booking.Channel == WalkInChannel
                   && @event is MessageEvent.BookingConfirmed or MessageEvent.BookingRescheduled or MessageEvent.BookingCancelled
                   && await walkIns.FindMobileAsync(booking.Id, cancellationToken) is { } walkIn
                ? new Recipient(walkIn, platform.DefaultLocale, null, null)
                : null;
        }

        return await professionals.FindAsync(booking.ProfessionalId, cancellationToken) is { IsActive: true, WhatsAppE164: { } number } card
            ? new Recipient(number, platform.DefaultLocale, booking.ProfessionalId.Value, card)
            : null;
    }

    /// <summary>Bookings' <c>BookingChannel.WalkIn</c>.</summary>
    private const string WalkInChannel = "WalkIn";

    /// <summary>Where a message goes; <c>Id</c> is the customer's or professional's id (none for a walk-in).</summary>
    private readonly record struct Recipient(string E164, string Locale, Guid? Id, ProfessionalContactCard? Professional);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No active WhatsApp template for {Event} / {Audience} / {Locale}; nothing sent")]
    private static partial void LogNoTemplate(ILogger logger, MessageEvent @event, MessageAudience audience, string locale);

    [LoggerMessage(Level = LogLevel.Information, Message = "WhatsApp {Event} / {Audience} is turned off for the booking's shop; nothing sent")]
    private static partial void LogTurnedOff(ILogger logger, MessageEvent @event, MessageAudience audience);
}

/// <summary>
/// Turns booking outbox events into WhatsApp dispatches, in-app notices and reminder jobs (spec §16, §17; R-NTF-03/04/06/09;
/// D-109…D-112). Runs once per event (the processor's processed-message record) and writes idempotently.
/// <list type="bullet">
/// <item>Customers: confirmed, pending request, rescheduled, cancelled, given to another barber (D-145). Professionals:
/// confirmed (their new-booking alert), rescheduled while confirmed, and cancelled only if they had been told about the
/// booking. When a change gives the booking to another barber, the barber it left is told it is cancelled (if they had been
/// told about it) and the barber it went to that it is confirmed (if it is).</item>
/// <item>Messages about an event older than <see cref="MaxMessageAge"/> are not sent (a backlog after downtime, D-111);
/// reminders are still reconciled.</item>
/// <item>Reminders follow the booking's current state: a confirmed booking has one per audience at start − offset, a
/// moved one gets new jobs and the old ones are deleted, a cancelled one has none. A reminder whose time already passed is
/// not scheduled.</item>
/// <item>In-app: the shop hears about what customers and admins did; the customer about what the shop or admins did; a
/// barber with a login (D-145) about their own bookings, the one it left included, unless they made the change.</item>
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
    IStaffDirectory staff,
    IProfessionalDirectory professionals,
    IJobScheduler jobs,
    TimeProvider clock) : IOutboxConsumer
{
    public const string ConsumerName = "notifications.booking";

    /// <summary>The booking history's actor type for a shop user (Bookings' <c>ActorType.ShopUser</c>).</summary>
    private const string ShopActor = "ShopUser";
    public static readonly TimeSpan MaxMessageAge = TimeSpan.FromHours(24);

    public string Name => ConsumerName;

    public bool Handles(string messageType) =>
        messageType is "booking.created" or "booking.rescheduled" or "booking.cancelled" or "booking.status_changed" or "booking.professional_changed";

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
            foreach (var (@event, audience, previous) in Plan(message.Type, change, booking))
            {
                var about = previous ? AsTheBarberItLeftHadIt(change, booking) : booking;
                if (audience == MessageAudience.Professional && @event == MessageEvent.BookingCancelled
                    && !await ProfessionalWasToldAsync(booking.Id, about.ProfessionalId, cancellationToken))
                {
                    continue;
                }

                var key = previous ? $"{message.Id:N}:{audience}:previous" : $"{message.Id:N}:{audience}";
                if (await factory.CreateForBookingAsync(DispatchKind.Lifecycle, key, @event, audience, about, shop, platform, cancellationToken) is { } dispatch)
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

    /// <summary>
    /// Which messages an event sends, from what happened (the event and the status it produced). <c>Previous</c> marks the
    /// message for the barber the booking left (D-145); the others go to the booking's barber.
    /// </summary>
    internal static IEnumerable<(MessageEvent Event, MessageAudience Audience, bool Previous)> Plan(string type, BookingOutboxEvent change, NotifiableBooking booking)
    {
        switch (type)
        {
            case "booking.created" when change.Status == "Pending":
                yield return (MessageEvent.BookingPending, MessageAudience.Customer, false);
                break;
            case "booking.created" when change.Status is "Confirmed" or "Arrived":
            case "booking.status_changed" when change.Status == "Confirmed":
                yield return (MessageEvent.BookingConfirmed, MessageAudience.Customer, false);
                yield return (MessageEvent.BookingConfirmed, MessageAudience.Professional, false);
                break;
            case "booking.rescheduled":
            case "booking.professional_changed":
                yield return (type == "booking.rescheduled" ? MessageEvent.BookingRescheduled : MessageEvent.BookingProfessionalChanged, MessageAudience.Customer, false);
                if (change.LeftProfessionalId is not null)
                {
                    // Off the day of the barber it left; new to the barber it went to.
                    yield return (MessageEvent.BookingCancelled, MessageAudience.Professional, true);
                    if (booking.IsConfirmed)
                    {
                        yield return (MessageEvent.BookingConfirmed, MessageAudience.Professional, false);
                    }
                }
                else if (booking.IsConfirmed)
                {
                    yield return (MessageEvent.BookingRescheduled, MessageAudience.Professional, false);
                }

                break;
            case "booking.cancelled":
                yield return (MessageEvent.BookingCancelled, MessageAudience.Customer, false);
                yield return (MessageEvent.BookingCancelled, MessageAudience.Professional, false);
                break;
        }
    }

    /// <summary>The booking as the barber it left had it: theirs, at the time it had then (D-145).</summary>
    internal static NotifiableBooking AsTheBarberItLeftHadIt(BookingOutboxEvent change, NotifiableBooking booking)
    {
        var startsAt = change.PreviousStartsAt ?? change.StartsAt;
        return booking with
        {
            ProfessionalId = new ProfessionalId(change.LeftProfessionalId ?? change.ProfessionalId),
            StartsAt = startsAt,
            EndsAt = startsAt.AddMinutes(booking.DurationMinutes),
        };
    }

    /// <summary>Whether this barber got a message announcing the booking (so a cancellation means something to them).</summary>
    private Task<bool> ProfessionalWasToldAsync(Guid bookingId, ProfessionalId professionalId, CancellationToken cancellationToken) =>
        db.Set<WhatsAppDispatch>().AnyAsync(
            d => d.BookingId == bookingId && d.Audience == MessageAudience.Professional && d.RecipientId == professionalId.Value
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
            "booking.professional_changed" => NoticeKinds.BookingProfessionalChanged,
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

        var left = change.LeftProfessionalId is { } leftId ? new ProfessionalId(leftId) : (ProfessionalId?)null;
        if (left is { } leftProfessional && await professionals.FindAsync(leftProfessional, cancellationToken) is { } before)
        {
            parameters["previousProfessionalNameAr"] = before.NameAr;
            parameters["previousProfessionalNameEn"] = before.NameEn;
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

        // A barber with a login hears about their own bookings, and the one it left about the move (D-145), unless they
        // made the change themselves. A change of barber is the same notice for both.
        var forBarber = new Dictionary<string, string>(parameters, StringComparer.Ordinal) { ["customerName"] = booking.CustomerName };
        var barberKind = left is null ? kind : NoticeKinds.BookingProfessionalChanged;
        foreach (var professional in left is { } l ? new[] { new ProfessionalId(change.ProfessionalId), l } : [new ProfessionalId(change.ProfessionalId)])
        {
            if (await staff.BarberAccountAsync(professional, cancellationToken) is { } barber && barber != change.ActorId)
            {
                await center.NotifyUserAsync(barber, new InAppNotice(barberKind, dedupe, forBarber, booking.Id), cancellationToken);
            }
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
