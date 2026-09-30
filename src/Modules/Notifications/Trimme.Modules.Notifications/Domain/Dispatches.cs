using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.Modules.Notifications.Domain;

public readonly record struct DispatchId(Guid Value) : IEntityId<DispatchId>
{
    public static DispatchId From(Guid value) => new(value);
}

public enum DispatchKind
{
    /// <summary>Confirmation, pending request, reschedule or cancellation, from a booking outbox event.</summary>
    Lifecycle,

    /// <summary>The reminder before the start.</summary>
    Reminder,

    /// <summary>An admin's test send to an explicitly typed recipient (R-NTF-08); never linked to a booking.</summary>
    Test,
}

public enum DispatchStatus
{
    /// <summary>Recorded and waiting for (another) attempt.</summary>
    Queued,

    /// <summary>The provider accepted it.</summary>
    Sent,

    /// <summary>The provider reported delivery to the handset.</summary>
    Delivered,

    /// <summary>The recipient read it (Meta status webhook).</summary>
    Read,

    /// <summary>Gave up: a permanent provider error, or the automatic retries ran out. An admin can retry it.</summary>
    Failed,
}

/// <summary>A button as rendered into a dispatch: its label and the URL the platform built.</summary>
public sealed record RenderedButton(string Label, string Url);

/// <summary>
/// One WhatsApp message to one recipient (spec §16, R-NTF-07, D-110). It keeps the template version that rendered it, the
/// recipient encrypted and masked, the rendered text (until the retention period ends; its hash stays), the provider's
/// message id, the status and every attempt's outcome. The <see cref="DedupeKey"/> is unique, so an event or a reminder
/// can produce at most one dispatch per audience however many times it is processed.
/// </summary>
public sealed class WhatsAppDispatch : Entity<DispatchId>
{
    public const int MaxErrorLength = 300;

    /// <summary>Automatic attempts before the dispatch is marked Failed (an admin can still retry it).</summary>
    public const int MaxAutomaticAttempts = 5;

    private WhatsAppDispatch(DispatchId id)
        : base(id)
    {
        DedupeKey = RecipientProtected = RecipientMasked = Locale = ContentHash = string.Empty;
    }

    private WhatsAppDispatch()
    {
        DedupeKey = RecipientProtected = RecipientMasked = Locale = ContentHash = string.Empty;
    }

    public DispatchKind Kind { get; private set; }

    public string DedupeKey { get; private set; }

    public Guid? BookingId { get; private set; }

    /// <summary>For filters only; the dispatch log is a platform (admin) view, never shown to shops.</summary>
    public ShopId? ShopId { get; private set; }

    public MessageEvent Event { get; private set; }

    public MessageAudience Audience { get; private set; }

    public string Locale { get; private set; }

    public WhatsAppTemplateId TemplateId { get; private set; }

    public TemplateVersionId TemplateVersionId { get; private set; }

    public int TemplateVersionNumber { get; private set; }

    /// <summary>The recipient's E.164 number, encrypted (purpose <c>trimme.whatsapp-recipient</c>).</summary>
    public string RecipientProtected { get; private set; }

    public string RecipientMasked { get; private set; }

    /// <summary>The customer's user id or the professional's id (null for a test send); an id, not contact data.</summary>
    public Guid? RecipientId { get; private set; }

    /// <summary>The rendered text; cleared after the retention period (<see cref="ContentHash"/> stays).</summary>
    public string? Body { get; private set; }

    public List<RenderedButton> Buttons { get; private set; } = [];

    /// <summary>The placeholder values in body order, for the provider's template parameters; cleared with the body.</summary>
    public List<string> Parameters { get; private set; } = [];

    public string? ProviderTemplateName { get; private set; }

    /// <summary>SHA-256 of the rendered body and buttons.</summary>
    public string ContentHash { get; private set; }

    public DateTimeOffset? ContentPurgedAt { get; private set; }

    public DispatchStatus Status { get; private set; }

    public int Attempts { get; private set; }

    /// <summary>Stable error code and a short description; never a phone number, token or message text.</summary>
    public string? LastError { get; private set; }

    public string? ProviderMessageId { get; private set; }

    /// <summary>The latest send job (Hangfire id), for the operations dashboard.</summary>
    public string? JobId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? LastAttemptAt { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public DateTimeOffset? DeliveredAt { get; private set; }

    public DateTimeOffset? FailedAt { get; private set; }

    public static WhatsAppDispatch Create(
        DispatchKind kind,
        string dedupeKey,
        Guid? bookingId,
        ShopId? shopId,
        WhatsAppTemplate template,
        WhatsAppTemplateVersion version,
        string recipientProtected,
        string recipientMasked,
        Guid? recipientId,
        RenderedMessage message,
        DateTimeOffset now) =>
        Build(DispatchId.From(Guid.CreateVersion7(now)), kind, dedupeKey, bookingId, shopId, template, version, recipientProtected, recipientMasked, recipientId, message, now);

    /// <summary>A development seed row: already delivered or failed, never sent (spec §20).</summary>
    public static WhatsAppDispatch Seeded(
        DispatchId id, string dedupeKey, Guid? bookingId, ShopId? shopId, WhatsAppTemplate template, WhatsAppTemplateVersion version,
        string recipientProtected, string recipientMasked, Guid? recipientId, RenderedMessage message, DispatchStatus status, string? error, DateTimeOffset at)
    {
        var dispatch = Build(id, DispatchKind.Lifecycle, dedupeKey, bookingId, shopId, template, version, recipientProtected, recipientMasked, recipientId, message, at);
        dispatch.Status = status;
        dispatch.Attempts = 1;
        dispatch.LastAttemptAt = at;
        dispatch.SentAt = status == DispatchStatus.Failed ? null : at;
        dispatch.DeliveredAt = status is DispatchStatus.Delivered or DispatchStatus.Read ? at : null;
        dispatch.FailedAt = status == DispatchStatus.Failed ? at : null;
        dispatch.LastError = error;
        dispatch.ProviderMessageId = status == DispatchStatus.Failed ? null : $"seed-{id.Value:N}";
        return dispatch;
    }

    public bool CanAttempt => Status == DispatchStatus.Queued;

    public void AssignJob(string jobId) => JobId = jobId;

    /// <summary>The provider accepted the message (or the fake reported it delivered).</summary>
    public void RecordAccepted(string providerMessageId, bool delivered, DateTimeOffset now)
    {
        Attempts++;
        LastAttemptAt = now;
        ProviderMessageId = providerMessageId;
        LastError = null;
        SentAt = now;
        Status = delivered ? DispatchStatus.Delivered : DispatchStatus.Sent;
        DeliveredAt = delivered ? now : null;
    }

    /// <summary>
    /// A failed attempt. Transient failures stay Queued until <see cref="MaxAutomaticAttempts"/>; a permanent failure, or
    /// the last automatic attempt, marks the dispatch Failed. Returns whether another automatic attempt should follow.
    /// </summary>
    public bool RecordFailure(string error, bool permanent, DateTimeOffset now)
    {
        Attempts++;
        LastAttemptAt = now;
        LastError = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
        if (permanent || Attempts >= MaxAutomaticAttempts)
        {
            Status = DispatchStatus.Failed;
            FailedAt = now;
            return false;
        }

        return true;
    }

    /// <summary>An admin retry: a Failed dispatch goes back to Queued for one more attempt (audited by the caller).</summary>
    public Result Retry()
    {
        if (Status != DispatchStatus.Failed)
        {
            return DispatchErrors.NotRetryable();
        }

        if (Body is null)
        {
            return DispatchErrors.ContentPurged();
        }

        Status = DispatchStatus.Queued;
        FailedAt = null;
        return Result.Success();
    }

    /// <summary>A status the provider reported later (webhook). Statuses only move forward.</summary>
    public void RecordProviderStatus(DispatchStatus status, string? error, DateTimeOffset now)
    {
        switch (status)
        {
            case DispatchStatus.Delivered when Status is DispatchStatus.Sent or DispatchStatus.Queued:
                Status = DispatchStatus.Delivered;
                DeliveredAt = now;
                break;
            case DispatchStatus.Read when Status is not DispatchStatus.Failed:
                Status = DispatchStatus.Read;
                DeliveredAt ??= now;
                break;
            case DispatchStatus.Failed when Status is DispatchStatus.Sent or DispatchStatus.Queued:
                Status = DispatchStatus.Failed;
                FailedAt = now;
                LastError = error is null ? "provider.failed" : (error.Length > MaxErrorLength ? error[..MaxErrorLength] : error);
                break;
        }
    }

    /// <summary>Clears the rendered text and parameters after the retention period; the hash and the version stay.</summary>
    public void PurgeContent(DateTimeOffset now)
    {
        Body = null;
        Buttons = [];
        Parameters = [];
        ContentPurgedAt = now;
    }

    private static WhatsAppDispatch Build(
        DispatchId id,
        DispatchKind kind,
        string dedupeKey,
        Guid? bookingId,
        ShopId? shopId,
        WhatsAppTemplate template,
        WhatsAppTemplateVersion version,
        string recipientProtected,
        string recipientMasked,
        Guid? recipientId,
        RenderedMessage message,
        DateTimeOffset now) =>
        new(id)
        {
            Kind = kind,
            DedupeKey = dedupeKey,
            BookingId = bookingId,
            ShopId = shopId,
            Event = template.Event,
            Audience = template.Audience,
            Locale = template.Locale,
            TemplateId = template.Id,
            TemplateVersionId = version.Id,
            TemplateVersionNumber = version.Number,
            RecipientProtected = recipientProtected,
            RecipientMasked = recipientMasked,
            RecipientId = recipientId,
            Body = message.Body,
            Buttons = [.. message.Buttons],
            Parameters = [.. message.Parameters],
            ProviderTemplateName = version.ProviderTemplateName,
            ContentHash = message.Hash,
            Status = DispatchStatus.Queued,
            CreatedAt = now,
        };
}

/// <summary>A rendered message: the text, the buttons with their URLs, the parameters in order and the content hash.</summary>
public sealed record RenderedMessage(string Body, IReadOnlyList<RenderedButton> Buttons, IReadOnlyList<string> Parameters)
{
    public string Hash =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Body + "\n" + JsonSerializer.Serialize(Buttons))));

    /// <summary>Renders a version with formatted values; <paramref name="urls"/> maps button targets to URLs.</summary>
    public static RenderedMessage From(TemplateContent content, IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<TemplateButtonTarget, string> urls) =>
        new(
            TemplateRenderer.Render(content.Body, values),
            [.. content.Buttons.Where(b => urls.ContainsKey(b.Target)).Select(b => new RenderedButton(b.Label, urls[b.Target]))],
            [.. Placeholders.Used(content.Body).Select(p => values.GetValueOrDefault(p, string.Empty))]);
}

public static class DispatchErrors
{
    public static Error NotFound() => Error.NotFound("dispatch.not_found", "The dispatch was not found.");

    public static Error NotRetryable() => Error.Conflict("dispatch.not_retryable", "Only a failed dispatch can be retried.");

    public static Error ContentPurged() => Error.Conflict("dispatch.content_purged", "The message text was purged after the retention period; it cannot be sent again.");
}

public readonly record struct ReminderId(Guid Value) : IEntityId<ReminderId>
{
    public static ReminderId From(Guid value) => new(value);
}

public enum ReminderStatus
{
    Scheduled,

    /// <summary>The booking moved or was cancelled; the job was deleted (R-NTF-06).</summary>
    Cancelled,

    Sent,

    /// <summary>The job ran but nothing was sent (the booking changed after scheduling, or no recipient).</summary>
    Skipped,
}

/// <summary>
/// A scheduled reminder job for one booking and one audience (spec §16, R-NTF-06, D-111). It records the booking start it
/// was scheduled for and the Hangfire job id, so a reschedule or a cancellation deletes the job and schedules the right
/// replacement. At most one Scheduled reminder per booking and audience (partial unique index).
/// </summary>
public sealed class ReminderSchedule : Entity<ReminderId>
{
    private ReminderSchedule(ReminderId id)
        : base(id)
    {
    }

    private ReminderSchedule()
    {
    }

    public Guid BookingId { get; private set; }

    public MessageAudience Audience { get; private set; }

    /// <summary>The booking's start when this reminder was scheduled; the job skips if the booking has moved since.</summary>
    public DateTimeOffset StartsAt { get; private set; }

    public DateTimeOffset DueAt { get; private set; }

    public string? JobId { get; private set; }

    public ReminderStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public static ReminderSchedule Schedule(Guid bookingId, MessageAudience audience, DateTimeOffset startsAt, DateTimeOffset dueAt, DateTimeOffset now) =>
        new(ReminderId.From(Guid.CreateVersion7(now)))
        {
            BookingId = bookingId,
            Audience = audience,
            StartsAt = startsAt,
            DueAt = dueAt,
            Status = ReminderStatus.Scheduled,
            CreatedAt = now,
        };

    public void AssignJob(string jobId) => JobId = jobId;

    public void Cancel(DateTimeOffset now)
    {
        Status = ReminderStatus.Cancelled;
        UpdatedAt = now;
    }

    public void Complete(bool sent, DateTimeOffset now)
    {
        Status = sent ? ReminderStatus.Sent : ReminderStatus.Skipped;
        UpdatedAt = now;
    }
}
