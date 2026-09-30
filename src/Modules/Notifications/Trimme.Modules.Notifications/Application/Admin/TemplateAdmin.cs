using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Auditing;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Notifications;
using Trimme.BuildingBlocks.Application.Platform;
using Trimme.BuildingBlocks.Application.Privacy;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Domain.Privacy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Notifications.Domain;
using Microsoft.Extensions.Options;

namespace Trimme.Modules.Notifications.Application.Admin;

// WhatsApp template administration (DV-A12, R-NTF-02, R-NTF-08, D-109): list the slots, edit a draft, validate and preview
// with sample data, activate (audited), restore an older wording into the draft, and a test send to an explicitly typed
// recipient that is never a registered customer.

public sealed record TemplateSummaryResponse(
    Guid Id,
    MessageEvent Event,
    MessageAudience Audience,
    string Locale,
    int? ActiveVersionNumber,
    string? ActiveBody,
    bool HasDraft,
    DateTimeOffset? UpdatedAt);

public sealed record TemplateListResponse(IReadOnlyList<TemplateSummaryResponse> Items);

public sealed record TemplateVersionResponse(
    Guid Id,
    int Number,
    string Body,
    IReadOnlyList<TemplateButton> Buttons,
    string? ProviderTemplateName,
    TemplateVersionStatus Status,
    DateTimeOffset CreatedAt,
    string? CreatedByName,
    DateTimeOffset? ActivatedAt,
    string? ActivatedByName);

/// <summary>A template slot with its whole version history (newest first) and the placeholders its audience may use.</summary>
public sealed record TemplateDetailResponse(
    Guid Id,
    MessageEvent Event,
    MessageAudience Audience,
    string Locale,
    IReadOnlyList<string> AllowedPlaceholders,
    Guid? ActiveVersionId,
    IReadOnlyList<TemplateVersionResponse> Versions,
    uint Version);

public sealed record TemplateIssueResponse(string Field, string Code, string? Placeholder);

/// <summary>The rendered sample (no real booking or customer), the issues found, and the placeholders the text uses.</summary>
public sealed record TemplatePreviewResponse(
    bool Valid, IReadOnlyList<TemplateIssueResponse> Issues, string Body, IReadOnlyList<RenderedButton> Buttons, IReadOnlyList<string> UsedPlaceholders);

internal sealed record ListTemplatesQuery : IQuery<TemplateListResponse>;

internal sealed record GetTemplateQuery(Guid TemplateId) : IQuery<Result<TemplateDetailResponse>>;

internal sealed record SaveTemplateDraftCommand(Guid TemplateId, string? Body, IReadOnlyList<TemplateButton>? Buttons, string? ProviderTemplateName, uint Version)
    : ICommand<Result<TemplateDetailResponse>>;

internal sealed record RestoreTemplateVersionCommand(Guid TemplateId, Guid VersionId, uint Version) : ICommand<Result<TemplateDetailResponse>>;

internal sealed record ActivateTemplateVersionCommand(Guid TemplateId, Guid VersionId, uint Version) : ICommand<Result<TemplateDetailResponse>>;

internal sealed record PreviewTemplateCommand(Guid TemplateId, string? Body, IReadOnlyList<TemplateButton>? Buttons) : ICommand<Result<TemplatePreviewResponse>>;

internal sealed record TestSendTemplateCommand(Guid TemplateId, Guid? VersionId, string? Recipient, bool ConfirmTestRecipient) : ICommand<Result<DispatchResponse>>;

internal static class TestSendErrors
{
    public static Error RecipientInvalid() =>
        Error.Validation("validation.failed", "Enter the test recipient's mobile number.",
            new Dictionary<string, string[]>(StringComparer.Ordinal) { ["recipient"] = ["validation.phone_invalid"] });

    public static Error ConfirmationRequired() =>
        Error.Validation("validation.failed", "Confirm that the number is a test recipient.",
            new Dictionary<string, string[]>(StringComparer.Ordinal) { ["confirmTestRecipient"] = ["validation.required"] });

    public static Error RecipientIsCustomer() =>
        Error.Conflict("whatsapp.test_recipient_is_customer", "This number belongs to a registered customer; use a dedicated test number.");
}

internal sealed class TemplateMapper(IUserNameLookup names)
{
    public async Task<TemplateDetailResponse> MapAsync(WhatsAppTemplate template, CancellationToken cancellationToken)
    {
        var people = await names.FindAsync(
            [.. template.Versions.SelectMany(v => new[] { v.CreatedBy, v.ActivatedBy }).OfType<Guid>().Distinct()], cancellationToken);
        string? Name(Guid? id) => id is { } key && people.TryGetValue(key, out var entry) ? entry.DisplayName : null;

        return new TemplateDetailResponse(
            template.Id.Value, template.Event, template.Audience, template.Locale, Placeholders.For(template.Audience), template.ActiveVersionId?.Value,
            [
                .. template.Versions.OrderByDescending(v => v.Number).Select(v => new TemplateVersionResponse(
                    v.Id.Value, v.Number, v.Body, v.Buttons, v.ProviderTemplateName, v.Status, v.CreatedAt, Name(v.CreatedBy), v.ActivatedAt, Name(v.ActivatedBy))),
            ],
            template.Version);
    }
}

internal static class TemplateQueries
{
    public static Task<WhatsAppTemplate?> FindAsync(TrimmeDbContext db, Guid templateId, CancellationToken cancellationToken)
    {
        var id = WhatsAppTemplateId.From(templateId);
        return db.Set<WhatsAppTemplate>().Include(t => t.Versions).SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
    }
}

internal sealed class ListTemplatesHandler(TrimmeDbContext db) : IQueryHandler<ListTemplatesQuery, TemplateListResponse>
{
    public async Task<TemplateListResponse> Handle(ListTemplatesQuery query, CancellationToken cancellationToken)
    {
        var templates = await db.Set<WhatsAppTemplate>().AsNoTracking().Include(t => t.Versions).ToListAsync(cancellationToken);
        return new TemplateListResponse(
        [
            .. templates.OrderBy(t => t.Event).ThenBy(t => t.Audience).ThenBy(t => t.Locale, StringComparer.Ordinal).Select(t => new TemplateSummaryResponse(
                t.Id.Value, t.Event, t.Audience, t.Locale, t.ActiveVersion?.Number, t.ActiveVersion?.Body, t.Draft is not null, t.UpdatedAt ?? t.CreatedAt)),
        ]);
    }
}

internal sealed class GetTemplateHandler(TrimmeDbContext db, TemplateMapper mapper) : IQueryHandler<GetTemplateQuery, Result<TemplateDetailResponse>>
{
    public async Task<Result<TemplateDetailResponse>> Handle(GetTemplateQuery query, CancellationToken cancellationToken) =>
        await TemplateQueries.FindAsync(db, query.TemplateId, cancellationToken) is { } template
            ? await mapper.MapAsync(template, cancellationToken)
            : TemplateErrors.NotFound();
}

internal sealed class SaveTemplateDraftHandler(TrimmeDbContext db, ICurrentUser user, TimeProvider clock, TemplateMapper mapper)
    : ICommandHandler<SaveTemplateDraftCommand, Result<TemplateDetailResponse>>
{
    public async Task<Result<TemplateDetailResponse>> Handle(SaveTemplateDraftCommand command, CancellationToken cancellationToken)
    {
        if (await TemplateQueries.FindAsync(db, command.TemplateId, cancellationToken) is not { } template)
        {
            return TemplateErrors.NotFound();
        }

        db.Entry(template).Property(t => t.Version).OriginalValue = command.Version;
        var saved = template.SaveDraft(TemplateContent.Create(command.Body, command.Buttons, command.ProviderTemplateName), user.UserId!.Value, clock.GetUtcNow());
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await mapper.MapAsync(template, cancellationToken);
    }
}

internal sealed class RestoreTemplateVersionHandler(TrimmeDbContext db, ICurrentUser user, TimeProvider clock, TemplateMapper mapper)
    : ICommandHandler<RestoreTemplateVersionCommand, Result<TemplateDetailResponse>>
{
    public async Task<Result<TemplateDetailResponse>> Handle(RestoreTemplateVersionCommand command, CancellationToken cancellationToken)
    {
        if (await TemplateQueries.FindAsync(db, command.TemplateId, cancellationToken) is not { } template)
        {
            return TemplateErrors.NotFound();
        }

        db.Entry(template).Property(t => t.Version).OriginalValue = command.Version;
        var restored = template.DraftFrom(TemplateVersionId.From(command.VersionId), user.UserId!.Value, clock.GetUtcNow());
        if (restored.IsFailure)
        {
            return restored.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await mapper.MapAsync(template, cancellationToken);
    }
}

internal sealed class ActivateTemplateVersionHandler(TrimmeDbContext db, ICurrentUser user, TimeProvider clock, IAuditLog audit, TemplateMapper mapper)
    : ICommandHandler<ActivateTemplateVersionCommand, Result<TemplateDetailResponse>>
{
    public async Task<Result<TemplateDetailResponse>> Handle(ActivateTemplateVersionCommand command, CancellationToken cancellationToken)
    {
        if (await TemplateQueries.FindAsync(db, command.TemplateId, cancellationToken) is not { } template)
        {
            return TemplateErrors.NotFound();
        }

        db.Entry(template).Property(t => t.Version).OriginalValue = command.Version;
        var previous = template.ActiveVersion?.Number;
        var activated = template.Activate(TemplateVersionId.From(command.VersionId), user.UserId!.Value, clock.GetUtcNow());
        if (activated.IsFailure)
        {
            return activated.Error;
        }

        audit.Record(new AuditRecord(
            "whatsapp_template.activated", "WhatsAppTemplate", template.Id.Value.ToString(),
            Summary: $"{template.Event} · {template.Audience} · {template.Locale}: v{previous?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"} → v{activated.Value.Number.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
        await db.SaveChangesAsync(cancellationToken);
        return await mapper.MapAsync(template, cancellationToken);
    }
}

internal sealed class PreviewTemplateHandler(TrimmeDbContext db, TimeProvider clock, IPlatformSettings settings, IOptions<MessageLinkOptions> links)
    : ICommandHandler<PreviewTemplateCommand, Result<TemplatePreviewResponse>>
{
    public async Task<Result<TemplatePreviewResponse>> Handle(PreviewTemplateCommand command, CancellationToken cancellationToken)
    {
        var id = WhatsAppTemplateId.From(command.TemplateId);
        if (await db.Set<WhatsAppTemplate>().AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, cancellationToken) is not { } template)
        {
            return TemplateErrors.NotFound();
        }

        var content = TemplateContent.Create(command.Body, command.Buttons, null);
        var issues = TemplateValidator.Validate(template.Audience, content);
        var (booking, shop) = SampleMessage.For(template.Locale, clock.GetUtcNow());
        var offset = (await settings.GetAsync(cancellationToken)).ReminderOffsetMinutes;
        var values = MessageComposer.Values(booking, shop, template.Audience, template.Locale, offset, links.Value.PublicBaseUrl);
        var rendered = RenderedMessage.From(content, values, MessageComposer.Urls(booking, shop, template.Audience, template.Locale, links.Value.PublicBaseUrl));
        return new TemplatePreviewResponse(
            issues.Count == 0, [.. issues.Select(i => new TemplateIssueResponse(i.Field, i.Code, i.Placeholder))], rendered.Body, rendered.Buttons,
            Placeholders.Used(content.Body));
    }
}

internal sealed class TestSendTemplateValidator : AbstractValidator<TestSendTemplateCommand>
{
    public TestSendTemplateValidator() => RuleFor(c => c.Recipient).NotEmpty().MaximumLength(30);
}

/// <summary>
/// Sends one version (default: the draft, else the active one) rendered with sample data to a number the admin typed and
/// confirmed as a test recipient (R-NTF-08). A registered customer's number is refused. Audited without the number.
/// </summary>
internal sealed class TestSendTemplateHandler(
    TrimmeDbContext db,
    ICustomerNumberCheck customers,
    IPersonalDataProtector protector,
    DispatchSender sender,
    IAuditLog audit,
    TimeProvider clock,
    IPlatformSettings settings,
    IOptions<MessageLinkOptions> links) : ICommandHandler<TestSendTemplateCommand, Result<DispatchResponse>>
{
    public async Task<Result<DispatchResponse>> Handle(TestSendTemplateCommand command, CancellationToken cancellationToken)
    {
        if (!command.ConfirmTestRecipient)
        {
            return TestSendErrors.ConfirmationRequired();
        }

        if (!PhoneNumber.TryParseMobile(command.Recipient, out var phone))
        {
            return TestSendErrors.RecipientInvalid();
        }

        if (await TemplateQueries.FindAsync(db, command.TemplateId, cancellationToken) is not { } template)
        {
            return TemplateErrors.NotFound();
        }

        var version = command.VersionId is { } requested
            ? template.Versions.FirstOrDefault(v => v.Id.Value == requested)
            : template.Draft ?? template.ActiveVersion;
        if (version is null)
        {
            return TemplateErrors.VersionNotFound();
        }

        if (await customers.IsCustomerNumberAsync(phone.E164, cancellationToken))
        {
            return TestSendErrors.RecipientIsCustomer();
        }

        var now = clock.GetUtcNow();
        var (booking, shop) = SampleMessage.For(template.Locale, now);
        var offset = (await settings.GetAsync(cancellationToken)).ReminderOffsetMinutes;
        var message = RenderedMessage.From(
            version.Content,
            MessageComposer.Values(booking, shop, template.Audience, template.Locale, offset, links.Value.PublicBaseUrl),
            MessageComposer.Urls(booking, shop, template.Audience, template.Locale, links.Value.PublicBaseUrl));
        var dispatch = WhatsAppDispatch.Create(
            DispatchKind.Test, $"test:{Guid.CreateVersion7(now):N}", bookingId: null, shopId: null, template, version,
            protector.Protect(phone.E164, NotificationPurposes.DispatchRecipient), phone.Masked, recipientId: null, message, now);
        db.Add(dispatch);
        audit.Record(new AuditRecord(
            "whatsapp_template.test_sent", "WhatsAppTemplate", template.Id.Value.ToString(),
            Summary: $"Test send of v{version.Number.ToString(System.Globalization.CultureInfo.InvariantCulture)} ({template.Event} · {template.Audience} · {template.Locale})"));
        await db.SaveChangesAsync(cancellationToken);

        // One attempt now; a test send is not retried automatically.
        await sender.AttemptAsync(dispatch, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return DispatchMapping.ToResponse(dispatch);
    }
}
