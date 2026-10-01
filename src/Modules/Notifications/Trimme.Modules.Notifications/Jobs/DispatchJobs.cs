using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trimme.BuildingBlocks.Application.Jobs;
using Trimme.BuildingBlocks.Application.Notifications;
using Trimme.BuildingBlocks.Application.Platform;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Notifications.Application;
using Trimme.Modules.Notifications.Domain;

namespace Trimme.Modules.Notifications.Jobs;

/// <summary>Retention of rendered message text (D-110). Bound from <c>Notifications</c>.</summary>
public sealed class NotificationsOptions
{
    public const string SectionName = "Notifications";

    /// <summary>After this many days a dispatch keeps only its content hash and template version (default 90).</summary>
    public int ContentRetentionDays { get; set; } = 90;
}

/// <summary>
/// Sends one queued dispatch (D-110). A transient failure schedules the next attempt with backoff (30 s, 2 min, 10 min,
/// 30 min) until <see cref="WhatsAppDispatch.MaxAutomaticAttempts"/>; a permanent failure or the last attempt marks it
/// Failed and tells the admins who watch WhatsApp. For a professional, the outcome also sets the number's verification
/// state. Idempotent: a dispatch that is not queued is left alone. Job arguments are ids only.
/// </summary>
internal sealed class SendDispatchJob(
    TrimmeDbContext db,
    DispatchSender sender,
    IJobScheduler jobs,
    INotificationCenter center,
    IProfessionalContactReader professionals,
    ISystemDataScope scope,
    TimeProvider clock)
{
    public const string WatchPermission = "Admin.WhatsApp.View";

    public static string Enqueue(IJobScheduler jobs, Guid dispatchId) =>
        jobs.Enqueue<SendDispatchJob>(job => job.RunAsync(dispatchId, CancellationToken.None));

    public async Task RunAsync(Guid dispatchId, CancellationToken cancellationToken)
    {
        var id = DispatchId.From(dispatchId);
        if (await db.Set<WhatsAppDispatch>().SingleOrDefaultAsync(d => d.Id == id, cancellationToken) is not { CanAttempt: true } dispatch)
        {
            return;
        }

        var retry = await sender.AttemptAsync(dispatch, cancellationToken);
        if (retry)
        {
            dispatch.AssignJob(jobs.Schedule<SendDispatchJob>(
                job => job.RunAsync(dispatchId, CancellationToken.None), clock.GetUtcNow() + DispatchSender.Backoff(dispatch.Attempts)));
        }

        using (scope.Begin())
        {
            if (dispatch is { Audience: MessageAudience.Professional, RecipientId: { } professionalId, Kind: not DispatchKind.Test }
                && dispatch.Status is DispatchStatus.Delivered or DispatchStatus.Failed)
            {
                await professionals.RecordDeliveryAsync(new ProfessionalId(professionalId), dispatch.Status == DispatchStatus.Delivered, cancellationToken);
            }

            if (dispatch.Status == DispatchStatus.Failed && dispatch.Kind != DispatchKind.Test)
            {
                await center.NotifyAdminsAsync(WatchPermission, new InAppNotice(
                    NoticeKinds.DispatchFailed,
                    $"dispatch:{dispatchId:N}:{dispatch.Attempts.ToString(CultureInfo.InvariantCulture)}",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["dispatchId"] = dispatchId.ToString(),
                        ["event"] = dispatch.Event.ToString(),
                        ["audience"] = dispatch.Audience.ToString(),
                        ["error"] = dispatch.LastError ?? string.Empty,
                    },
                    dispatch.BookingId), cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        await center.PushPendingAsync(cancellationToken);
    }
}

/// <summary>
/// The reminder job of one booking and audience, scheduled at start − offset (spec §16, R-NTF-06, D-111). It re-reads the
/// booking and sends only if it is still confirmed, still at the start it was scheduled for, and not started; otherwise
/// it records that it skipped. At most one dispatch per reminder (dedupe key from the reminder id).
/// </summary>
internal sealed class BookingReminderJob(
    TrimmeDbContext db,
    IBookingNotificationSource bookings,
    IShopDirectory shops,
    IPlatformSettings settings,
    DispatchFactory factory,
    IJobScheduler jobs,
    ISystemDataScope scope,
    TimeProvider clock)
{
    public static string Schedule(IJobScheduler jobs, Guid reminderId, DateTimeOffset dueAt) =>
        jobs.Schedule<BookingReminderJob>(job => job.RunAsync(reminderId, CancellationToken.None), dueAt);

    public async Task RunAsync(Guid reminderId, CancellationToken cancellationToken)
    {
        WhatsAppDispatch? dispatch;
        using (scope.Begin())
        {
            var id = ReminderId.From(reminderId);
            if (await db.Set<ReminderSchedule>().SingleOrDefaultAsync(r => r.Id == id, cancellationToken) is not { Status: ReminderStatus.Scheduled } reminder)
            {
                return;
            }

            var now = clock.GetUtcNow();
            var booking = await bookings.FindAsync(reminder.BookingId, cancellationToken);
            var shop = booking is null ? null : await shops.FindAsync(booking.ShopId, cancellationToken);
            if (booking is not { IsConfirmed: true } || shop is null || booking.StartsAt != reminder.StartsAt || booking.StartsAt <= now)
            {
                reminder.Complete(sent: false, now);
                await db.SaveChangesAsync(cancellationToken);
                return;
            }

            dispatch = await factory.CreateForBookingAsync(
                DispatchKind.Reminder, $"reminder:{reminderId:N}", MessageEvent.BookingReminder, reminder.Audience, booking, shop,
                await settings.GetAsync(cancellationToken), cancellationToken);
            reminder.Complete(sent: dispatch is not null, now);
            await db.SaveChangesAsync(cancellationToken);
        }

        if (dispatch is not null)
        {
            dispatch.AssignJob(SendDispatchJob.Enqueue(jobs, dispatch.Id.Value));
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}

/// <summary>
/// Every five minutes (D-110, D-111): re-queues work whose follow-up was lost between a commit and the job enqueue — a
/// Scheduled reminder without a job, a queued dispatch never attempted — so a crash at the wrong moment cannot drop a
/// message.
/// </summary>
internal sealed partial class NotificationSweepJob(TrimmeDbContext db, IJobScheduler jobs, TimeProvider clock, ILogger<NotificationSweepJob> logger) : IRecurringJob
{
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(2);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var before = now - Grace;
        var reminders = await db.Set<ReminderSchedule>()
            .Where(r => r.Status == ReminderStatus.Scheduled && r.JobId == null && r.CreatedAt < before)
            .OrderBy(r => r.CreatedAt).Take(200).ToListAsync(cancellationToken);
        foreach (var reminder in reminders)
        {
            reminder.AssignJob(BookingReminderJob.Schedule(jobs, reminder.Id.Value, reminder.DueAt > now ? reminder.DueAt : now));
        }

        var dispatches = await db.Set<WhatsAppDispatch>()
            .Where(d => d.Status == DispatchStatus.Queued && d.JobId == null && d.LastAttemptAt == null && d.CreatedAt < before)
            .OrderBy(d => d.CreatedAt).Take(200).ToListAsync(cancellationToken);
        foreach (var dispatch in dispatches)
        {
            dispatch.AssignJob(SendDispatchJob.Enqueue(jobs, dispatch.Id.Value));
        }

        await db.SaveChangesAsync(cancellationToken);
        if (reminders.Count + dispatches.Count > 0)
        {
            LogRequeued(logger, reminders.Count, dispatches.Count);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification sweep re-queued {Reminders} reminders and {Dispatches} dispatches")]
    private static partial void LogRequeued(ILogger logger, int reminders, int dispatches);
}

/// <summary>
/// Daily (D-110): clears the rendered text and parameters of dispatches older than the retention period. The content hash
/// and the template version stay, so history still shows which version rendered each message.
/// </summary>
internal sealed partial class DispatchRetentionJob(
    TrimmeDbContext db, IOptions<NotificationsOptions> options, TimeProvider clock, ILogger<DispatchRetentionJob> logger) : IRecurringJob
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var before = now.AddDays(-Math.Max(1, options.Value.ContentRetentionDays));
        var purged = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var batch = await db.Set<WhatsAppDispatch>()
                .Where(d => d.CreatedAt < before && d.ContentPurgedAt == null && d.Status != DispatchStatus.Queued)
                .OrderBy(d => d.CreatedAt).Take(500).ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var dispatch in batch)
            {
                dispatch.PurgeContent(now);
            }

            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
            purged += batch.Count;
        }

        LogPurged(logger, purged);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Dispatch retention cleared the text of {Count} dispatches")]
    private static partial void LogPurged(ILogger logger, int count);
}

/// <summary>Records a provider-reported delivery outcome on the professional's number (enqueued by the status webhook).</summary>
internal sealed class ProfessionalDeliveryJob(TrimmeDbContext db, IProfessionalContactReader professionals, ISystemDataScope scope)
{
    public static string Enqueue(IJobScheduler jobs, Guid dispatchId) =>
        jobs.Enqueue<ProfessionalDeliveryJob>(job => job.RunAsync(dispatchId, CancellationToken.None));

    public async Task RunAsync(Guid dispatchId, CancellationToken cancellationToken)
    {
        var id = DispatchId.From(dispatchId);
        if (await db.Set<WhatsAppDispatch>().AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, cancellationToken) is not
            { Audience: MessageAudience.Professional, RecipientId: { } professionalId } dispatch
            || dispatch.Status is not (DispatchStatus.Delivered or DispatchStatus.Read or DispatchStatus.Failed))
        {
            return;
        }

        using (scope.Begin())
        {
            await professionals.RecordDeliveryAsync(new ProfessionalId(professionalId), dispatch.Status != DispatchStatus.Failed, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
