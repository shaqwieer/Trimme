using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Results;

namespace Trimme.Modules.Notifications.Domain;

public readonly record struct WhatsAppTemplateId(Guid Value) : IEntityId<WhatsAppTemplateId>
{
    public static WhatsAppTemplateId From(Guid value) => new(value);
}

public readonly record struct TemplateVersionId(Guid Value) : IEntityId<TemplateVersionId>
{
    public static TemplateVersionId From(Guid value) => new(value);
}

/// <summary>Who a message is for (spec §16): customers and professionals have independent templates.</summary>
public enum MessageAudience
{
    Customer,
    Professional,
}

/// <summary>
/// The booking lifecycle events that send a WhatsApp message (spec §16, D-109). Customers get all five; professionals get
/// confirmed (their new-booking alert), rescheduled, cancelled and the reminder, never the pending request.
/// </summary>
public enum MessageEvent
{
    /// <summary>A booking is confirmed: created Confirmed (or a walk-in), or a Pending request confirmed by the shop.</summary>
    BookingConfirmed,

    /// <summary>A request is waiting for the shop's confirmation (D-006). Customers only.</summary>
    BookingPending,

    BookingRescheduled,

    BookingCancelled,

    /// <summary><c>ReminderOffsetMinutes</c> (default 30) before the start, for both audiences.</summary>
    BookingReminder,
}

public enum TemplateVersionStatus
{
    Draft,
    Active,
    Archived,
}

/// <summary>What a template button opens (the URL is built by the platform, never typed by the admin).</summary>
public enum TemplateButtonTarget
{
    /// <summary>The customer's booking page. Customer templates only.</summary>
    ManageBooking,

    /// <summary>The shop's public page (address, map and directions).</summary>
    ShopPage,
}

public sealed record TemplateButton(string Label, TemplateButtonTarget Target);

/// <summary>
/// One message slot: an event, an audience and a locale (spec §16, D-109). Its wording lives in versions: an admin edits a
/// draft, validates and previews it, and activates it. The active version renders every new dispatch; a dispatch keeps
/// the version it used, so a later edit never rewrites history (R-NTF-07).
/// </summary>
public sealed class WhatsAppTemplate : AggregateRoot<WhatsAppTemplateId>, IConcurrencyVersioned
{
    private readonly List<WhatsAppTemplateVersion> _versions = [];

    private WhatsAppTemplate(WhatsAppTemplateId id, MessageEvent @event, MessageAudience audience, string locale, DateTimeOffset now)
        : base(id)
    {
        Event = @event;
        Audience = audience;
        Locale = locale;
        CreatedAt = now;
    }

    private WhatsAppTemplate()
    {
        Locale = string.Empty;
    }

    public MessageEvent Event { get; private set; }

    public MessageAudience Audience { get; private set; }

    /// <summary><c>ar</c> or <c>en</c>.</summary>
    public string Locale { get; private set; }

    public TemplateVersionId? ActiveVersionId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public IReadOnlyList<WhatsAppTemplateVersion> Versions => _versions;

    public WhatsAppTemplateVersion? ActiveVersion => _versions.FirstOrDefault(v => v.Id == ActiveVersionId);

    public WhatsAppTemplateVersion? Draft => _versions.FirstOrDefault(v => v.Status == TemplateVersionStatus.Draft);

    public static readonly IReadOnlyList<string> Locales = ["ar", "en"];

    /// <summary>Whether this event is sent to this audience at all (professionals never get the pending request).</summary>
    public static bool IsSent(MessageEvent @event, MessageAudience audience) =>
        !(audience == MessageAudience.Professional && @event == MessageEvent.BookingPending);

    /// <summary>A new slot whose first version is active at once (the <c>migrate</c> defaults, D-109).</summary>
    public static WhatsAppTemplate CreateWithActiveVersion(
        WhatsAppTemplateId id, TemplateVersionId versionId, MessageEvent @event, MessageAudience audience, string locale, TemplateContent content, DateTimeOffset now)
    {
        var template = new WhatsAppTemplate(id, @event, audience, locale, now);
        var version = new WhatsAppTemplateVersion(versionId, id, 1, content, createdBy: null, now);
        version.MarkActive(actor: null, now);
        template._versions.Add(version);
        template.ActiveVersionId = versionId;
        return template;
    }

    /// <summary>Creates the draft, or replaces the text of the existing draft. Active and archived versions never change.</summary>
    public Result<WhatsAppTemplateVersion> SaveDraft(TemplateContent content, Guid actor, DateTimeOffset now)
    {
        var issues = TemplateValidator.Validate(Audience, content);
        if (issues.Count > 0)
        {
            return TemplateErrors.Invalid(issues);
        }

        UpdatedAt = now;
        if (Draft is { } draft)
        {
            draft.Replace(content, actor, now);
            return draft;
        }

        var version = new WhatsAppTemplateVersion(TemplateVersionId.From(Guid.CreateVersion7(now)), Id, NextNumber(), content, actor, now);
        _versions.Add(version);
        return version;
    }

    /// <summary>Makes a draft the active version and archives the previous one (audited by the caller).</summary>
    public Result<WhatsAppTemplateVersion> Activate(TemplateVersionId versionId, Guid actor, DateTimeOffset now)
    {
        var version = _versions.FirstOrDefault(v => v.Id == versionId);
        if (version is null)
        {
            return TemplateErrors.VersionNotFound();
        }

        if (version.Status != TemplateVersionStatus.Draft)
        {
            return TemplateErrors.NotADraft();
        }

        // Validated again: the whitelist may have narrowed since the draft was saved.
        var issues = TemplateValidator.Validate(Audience, version.Content);
        if (issues.Count > 0)
        {
            return TemplateErrors.Invalid(issues);
        }

        ActiveVersion?.Archive();
        version.MarkActive(actor, now);
        ActiveVersionId = version.Id;
        UpdatedAt = now;
        return version;
    }

    /// <summary>Starts a draft from an earlier version's text (to restore wording); still needs activation.</summary>
    public Result<WhatsAppTemplateVersion> DraftFrom(TemplateVersionId versionId, Guid actor, DateTimeOffset now) =>
        _versions.FirstOrDefault(v => v.Id == versionId) is { } source
            ? SaveDraft(source.Content, actor, now)
            : TemplateErrors.VersionNotFound();

    private int NextNumber() => _versions.Count == 0 ? 1 : _versions.Max(v => v.Number) + 1;
}

/// <summary>
/// The editable part of a version: the body with placeholders, up to two buttons, and the name of the approved Meta
/// template this wording corresponds to (production sends the template by name with the placeholder values in order;
/// optional in development).
/// </summary>
public sealed record TemplateContent(string Body, IReadOnlyList<TemplateButton> Buttons, string? ProviderTemplateName)
{
    public static TemplateContent Create(string? body, IReadOnlyList<TemplateButton>? buttons, string? providerTemplateName) =>
        new(
            (body ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Trim(),
            [.. (buttons ?? []).Select(b => b with { Label = b.Label.Trim() })],
            string.IsNullOrWhiteSpace(providerTemplateName) ? null : providerTemplateName.Trim());
}

/// <summary>One immutable wording of a template once it leaves the draft state.</summary>
public sealed class WhatsAppTemplateVersion : Entity<TemplateVersionId>
{
    internal WhatsAppTemplateVersion(TemplateVersionId id, WhatsAppTemplateId templateId, int number, TemplateContent content, Guid? createdBy, DateTimeOffset now)
        : base(id)
    {
        TemplateId = templateId;
        Number = number;
        Body = content.Body;
        Buttons = Copy(content.Buttons);
        ProviderTemplateName = content.ProviderTemplateName;
        Status = TemplateVersionStatus.Draft;
        CreatedBy = createdBy;
        CreatedAt = now;
    }

    private WhatsAppTemplateVersion()
    {
        Body = string.Empty;
    }

    public WhatsAppTemplateId TemplateId { get; private set; }

    /// <summary>1, 2, 3… per template.</summary>
    public int Number { get; private set; }

    public string Body { get; private set; }

    public List<TemplateButton> Buttons { get; private set; } = [];

    public string? ProviderTemplateName { get; private set; }

    public TemplateVersionStatus Status { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public DateTimeOffset? ActivatedAt { get; private set; }

    public Guid? ActivatedBy { get; private set; }

    public TemplateContent Content => new(Body, Buttons, ProviderTemplateName);

    internal void Replace(TemplateContent content, Guid actor, DateTimeOffset now)
    {
        Body = content.Body;
        Buttons = Copy(content.Buttons);
        ProviderTemplateName = content.ProviderTemplateName;
        CreatedBy = actor;
        UpdatedAt = now;
    }

    internal void MarkActive(Guid? actor, DateTimeOffset now)
    {
        Status = TemplateVersionStatus.Active;
        ActivatedAt = now;
        ActivatedBy = actor;
    }

    internal void Archive() => Status = TemplateVersionStatus.Archived;

    /// <summary>
    /// Buttons are owned values stored with each version: a version always takes its own copies, so a restored draft or a
    /// shared default never takes the rows of another version.
    /// </summary>
    private static List<TemplateButton> Copy(IEnumerable<TemplateButton> buttons) => [.. buttons.Select(b => b with { })];
}

public static class TemplateErrors
{
    public static Error NotFound() => Error.NotFound("template.not_found", "The template was not found.");

    public static Error VersionNotFound() => Error.NotFound("template.version_not_found", "The template version was not found.");

    public static Error NotADraft() => Error.Conflict("template.not_a_draft", "Only a draft can be activated; active and archived versions never change.");

    public static Error NoActiveVersion() => Error.Conflict("template.no_active_version", "The template has no active version.");

    /// <summary>400 with the issue codes under <c>body</c> or <c>buttons</c>, for example <c>template.unknown_placeholder</c>.</summary>
    public static Error Invalid(IReadOnlyList<TemplateIssue> issues) =>
        Error.Validation(
            "validation.failed",
            "The template is invalid.",
            issues.GroupBy(i => i.Field).ToDictionary(g => g.Key, g => g.Select(i => i.Code).Distinct().ToArray(), StringComparer.Ordinal));
}
