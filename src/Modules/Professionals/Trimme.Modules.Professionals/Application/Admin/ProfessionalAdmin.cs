using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Auditing;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Media;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Qr;
using Trimme.BuildingBlocks.Application.Paging;
using Trimme.BuildingBlocks.Application.Privacy;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Media;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Privacy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Professionals.Domain;

namespace Trimme.Modules.Professionals.Application.Admin;

/// <summary>The WhatsApp settings an admin sees. The number itself is only ever masked here (reveal is a separate, audited action).</summary>
public sealed record AdminWhatsAppResponse(string? Masked, bool NotificationsEnabled, WhatsAppVerification Verification);

/// <summary>A professional in the admin list.</summary>
public sealed record AdminProfessionalListItem(
    Guid Id,
    Guid ShopId,
    string ShopNameAr,
    string ShopNameEn,
    string NameAr,
    string NameEn,
    string? SpecialtyAr,
    string? SpecialtyEn,
    string? AvatarUrl,
    ProfessionalStatus Status,
    AdminWhatsAppResponse WhatsApp);

/// <summary>
/// A professional as the platform admin edits it. <see cref="ShopId"/> is read-only: it is fixed at creation and no
/// request can change it (R-NEG-01).
/// </summary>
public sealed record AdminProfessionalResponse(
    Guid Id,
    Guid ShopId,
    string ShopSlug,
    string ShopNameAr,
    string ShopNameEn,
    string Slug,
    string NameAr,
    string NameEn,
    string? SpecialtyAr,
    string? SpecialtyEn,
    string? BioAr,
    string? BioEn,
    string? AvatarUrl,
    ProfessionalStatus Status,
    AdminWhatsAppResponse WhatsApp,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    uint Version,
    IReadOnlyList<string> Languages);

/// <summary>The full number, returned once per audited reveal. Never cached.</summary>
public sealed record RevealedWhatsAppResponse(string Number);

internal sealed record CreateProfessionalCommand(
    Guid ShopId,
    string NameAr,
    string NameEn,
    string? Slug,
    string? SpecialtyAr,
    string? SpecialtyEn,
    string? BioAr,
    string? BioEn,
    string? WhatsAppNumber,
    bool NotificationsEnabled,
    IReadOnlyList<string>? Languages = null) : ICommand<Result<AdminProfessionalResponse>>, IProfessionalProfileFields;

/// <summary>No shop id: a professional's shop never changes (D-011).</summary>
internal sealed record UpdateProfessionalCommand(
    Guid ProfessionalId,
    string NameAr,
    string NameEn,
    string? Slug,
    string? SpecialtyAr,
    string? SpecialtyEn,
    string? BioAr,
    string? BioEn,
    uint Version,
    IReadOnlyList<string>? Languages = null) : ICommand<Result<AdminProfessionalResponse>>, IProfessionalProfileFields;

internal sealed record ChangeProfessionalStatusCommand(Guid ProfessionalId, bool Enable, string? Reason) : ICommand<Result<AdminProfessionalResponse>>;

internal sealed record SetProfessionalWhatsAppCommand(Guid ProfessionalId, string? WhatsAppNumber, bool NotificationsEnabled, bool KeepCurrentNumber = false)
    : ICommand<Result<AdminProfessionalResponse>>;

internal sealed record RevealProfessionalWhatsAppCommand(Guid ProfessionalId, string Reason) : ICommand<Result<RevealedWhatsAppResponse>>;

internal sealed record ReplaceProfessionalAvatarCommand(Guid ProfessionalId, byte[]? Content) : ICommand<Result<AdminProfessionalResponse>>;

internal sealed record ListProfessionalsQuery(PageRequest Page, Guid? ShopId, ProfessionalStatus? Status, string? Search) : IQuery<PagedResponse<AdminProfessionalListItem>>;

internal sealed record GetProfessionalQuery(Guid ProfessionalId) : IQuery<AdminProfessionalResponse?>;

internal sealed class CreateProfessionalValidator : AbstractValidator<CreateProfessionalCommand>
{
    public CreateProfessionalValidator()
    {
        Include(new ProfessionalProfileRules<CreateProfessionalCommand>());
        RuleFor(c => c.ShopId).NotEmpty().WithErrorCode("validation.required");
        RuleFor(c => c.WhatsAppNumber)
            .Must(n => string.IsNullOrWhiteSpace(n) || PhoneNumber.TryParseMobile(n, out _)).WithErrorCode("validation.phone_invalid");
    }
}

internal sealed class UpdateProfessionalValidator : AbstractValidator<UpdateProfessionalCommand>
{
    public UpdateProfessionalValidator() => Include(new ProfessionalProfileRules<UpdateProfessionalCommand>());
}

internal sealed class ChangeProfessionalStatusValidator : AbstractValidator<ChangeProfessionalStatusCommand>
{
    public ChangeProfessionalStatusValidator() =>
        RuleFor(c => c.Reason).MaximumLength(500).WithErrorCode("validation.too_long");
}

internal sealed class SetProfessionalWhatsAppValidator : AbstractValidator<SetProfessionalWhatsAppCommand>
{
    public SetProfessionalWhatsAppValidator() =>
        RuleFor(c => c.WhatsAppNumber)
            .Must(n => string.IsNullOrWhiteSpace(n) || PhoneNumber.TryParseMobile(n, out _)).WithErrorCode("validation.phone_invalid");
}

internal sealed class RevealProfessionalWhatsAppValidator : AbstractValidator<RevealProfessionalWhatsAppCommand>
{
    public RevealProfessionalWhatsAppValidator() =>
        RuleFor(c => c.Reason).Must(r => !string.IsNullOrWhiteSpace(r) && r.Trim().Length >= 5).WithErrorCode("validation.reason_required")
            .MaximumLength(500).WithErrorCode("validation.too_long");
}


internal sealed class CreateProfessionalHandler(
    TrimmeDbContext db,
    IAdminDataScope scope,
    ProfessionalSupport support,
    IShopDirectory shops,
    IProfessionalQrCodes qrCodes,
    IAuditLog audit,
    TimeProvider clock) : ICommandHandler<CreateProfessionalCommand, Result<AdminProfessionalResponse>>
{
    public async Task<Result<AdminProfessionalResponse>> Handle(CreateProfessionalCommand command, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var shopId = new ShopId(command.ShopId);
        if (await shops.FindAsync(shopId, cancellationToken) is null)
        {
            return ProfessionalErrors.ShopNotFound();
        }

        var id = EntityId.New<ProfessionalId>();
        var slug = await support.ChooseSlugAsync(shopId, command.Slug, command.NameEn, null, cancellationToken);
        if (slug.IsFailure)
        {
            return slug.Error;
        }

        var number = await support.ProtectAsync(command.WhatsAppNumber, id, cancellationToken);
        if (number.IsFailure)
        {
            return number.Error;
        }

        var now = clock.GetUtcNow();
        var created = Professional.Create(id, shopId, slug.Value, ProfessionalMapping.Profile(command), now);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var professional = created.Value;
        var contact = ProfessionalContact.For(professional);
        var set = contact.Set(number.Value, command.NotificationsEnabled, now);
        if (set.IsFailure)
        {
            return set.Error;
        }

        db.Add(professional);
        db.Add(contact);
        audit.Record(new AuditRecord(
            "professional.created", nameof(Professional), id.ToString(), shopId,
            $"Created in shop {shopId}; WhatsApp {(contact.HasNumber ? "set" : "not set")}, notifications {(contact.NotificationsEnabled ? "on" : "off")}",
            SummaryArgs: AuditArgs.Of(("by", "admin"), ("whatsApp", contact.HasNumber), ("notifications", contact.NotificationsEnabled))));

        // Their own QR code at once (D-155, the client's 7-2-3).
        await qrCodes.EnsureAsync(shopId, id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await support.ToResponseAsync(professional, contact, cancellationToken);
    }
}

internal sealed class UpdateProfessionalHandler(
    TrimmeDbContext db,
    IAdminDataScope scope,
    ProfessionalSupport support,
    IBarberLoginSync logins,
    IAuditLog audit,
    TimeProvider clock) : ICommandHandler<UpdateProfessionalCommand, Result<AdminProfessionalResponse>>
{
    public async Task<Result<AdminProfessionalResponse>> Handle(UpdateProfessionalCommand command, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var professional = await support.FindAsync(command.ProfessionalId, cancellationToken);
        if (professional is null)
        {
            return ProfessionalErrors.NotFound();
        }

        var slug = string.IsNullOrWhiteSpace(command.Slug) || string.Equals(command.Slug.Trim(), professional.Slug, StringComparison.OrdinalIgnoreCase)
            ? Result<string>.Success(professional.Slug)
            : await support.ChooseSlugAsync(professional.ShopId, command.Slug, command.NameEn, professional.Id, cancellationToken);
        if (slug.IsFailure)
        {
            return slug.Error;
        }

        // Optimistic concurrency: a stale version makes the save fail with 409.
        db.Entry(professional).Property(p => p.Version).OriginalValue = command.Version;
        professional.UpdateProfile(ProfessionalMapping.Profile(command), clock.GetUtcNow());
        if (slug.Value != professional.Slug && professional.ChangeSlug(slug.Value, clock.GetUtcNow()) is { IsFailure: true } invalid)
        {
            return invalid.Error;
        }

        // The barber's login shows their Arabic name (D-151).
        await logins.NameChangedAsync(professional.Id, professional.NameAr, cancellationToken);

        audit.Record(new AuditRecord("professional.updated", nameof(Professional), professional.Id.ToString(), professional.ShopId, "Profile edited",
            SummaryArgs: AuditArgs.Of(("by", "admin"))));
        await db.SaveChangesAsync(cancellationToken);
        var contact = await db.Set<ProfessionalContact>().AsNoTracking().SingleOrDefaultAsync(c => c.ProfessionalId == professional.Id, cancellationToken);
        return await support.ToResponseAsync(professional, contact, cancellationToken);
    }
}

internal sealed class ChangeProfessionalStatusHandler(
    TrimmeDbContext db,
    IAdminDataScope scope,
    ProfessionalSupport support,
    IAuditLog audit,
    IBarberLoginSync logins,
    IProfessionalQrCodes qrCodes,
    TimeProvider clock) : ICommandHandler<ChangeProfessionalStatusCommand, Result<AdminProfessionalResponse>>
{
    public async Task<Result<AdminProfessionalResponse>> Handle(ChangeProfessionalStatusCommand command, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var professional = await support.FindAsync(command.ProfessionalId, cancellationToken);
        if (professional is null)
        {
            return ProfessionalErrors.NotFound();
        }

        var changed = command.Enable ? professional.Enable(clock.GetUtcNow()) : professional.Disable(clock.GetUtcNow());
        if (changed.IsFailure)
        {
            return changed.Error;
        }

        if (!command.Enable)
        {
            // A disabled barber can no longer sign in, and is signed out at once (D-145). Enabling them again does not
            // turn the login back on: the owner or admin does that, with a new temporary password.
            await logins.ProfessionalDisabledAsync(professional.Id, cancellationToken);
        }

        audit.Record(new AuditRecord(
            command.Enable ? "professional.enabled" : "professional.disabled",
            nameof(Professional), professional.Id.ToString(), professional.ShopId, $"Now {professional.Status}", command.Reason?.Trim(),
            SummaryArgs: AuditArgs.Of(("by", "admin"), ("status", professional.Status))));
        if (command.Enable)
        {
            // Turned on again without a code: one now (D-155).
            await qrCodes.EnsureAsync(professional.ShopId, professional.Id, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        var contact = await db.Set<ProfessionalContact>().AsNoTracking().SingleOrDefaultAsync(c => c.ProfessionalId == professional.Id, cancellationToken);
        return await support.ToResponseAsync(professional, contact, cancellationToken);
    }
}

internal sealed class SetProfessionalWhatsAppHandler(
    TrimmeDbContext db,
    IAdminDataScope scope,
    ProfessionalSupport support,
    IAuditLog audit,
    IBarberLoginSync logins,
    TimeProvider clock) : ICommandHandler<SetProfessionalWhatsAppCommand, Result<AdminProfessionalResponse>>
{
    public async Task<Result<AdminProfessionalResponse>> Handle(SetProfessionalWhatsAppCommand command, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var professional = await support.FindAsync(command.ProfessionalId, cancellationToken);
        if (professional is null)
        {
            return ProfessionalErrors.NotFound();
        }

        var contact = await support.ContactAsync(professional, cancellationToken);
        if (command.KeepCurrentNumber)
        {
            var toggled = contact.SetNotifications(command.NotificationsEnabled, clock.GetUtcNow());
            if (toggled.IsFailure)
            {
                return toggled.Error;
            }

            audit.Record(new AuditRecord(
                "professional.whatsapp_changed", nameof(Professional), professional.Id.ToString(), professional.ShopId,
                $"WhatsApp number unchanged; notifications {(contact.NotificationsEnabled ? "on" : "off")}",
                SummaryArgs: AuditArgs.Of(("by", "admin"), ("number", "unchanged"), ("notifications", contact.NotificationsEnabled))));
            await db.SaveChangesAsync(cancellationToken);
            return await support.ToResponseAsync(professional, contact, cancellationToken);
        }

        var number = await support.ProtectAsync(command.WhatsAppNumber, professional.Id, cancellationToken);
        if (number.IsFailure)
        {
            return number.Error;
        }

        var hadNumber = contact.HasNumber;
        var numberChanged = contact.WhatsAppLookupHash != number.Value?.LookupHash;
        var set = contact.Set(number.Value, command.NotificationsEnabled, clock.GetUtcNow());
        if (set.IsFailure)
        {
            return set.Error;
        }

        if (numberChanged)
        {
            // A barber's login follows their number (D-145): the new one signs in, the old one no longer does.
            await logins.NumberChangedAsync(professional.Id, support.LoginHashOf(command.WhatsAppNumber), cancellationToken);
        }

        // The audit trail never holds the number, only what happened to it.
        var what = !numberChanged ? "number unchanged" : !contact.HasNumber ? "number removed" : hadNumber ? "number changed" : "number added";
        audit.Record(new AuditRecord(
            "professional.whatsapp_changed", nameof(Professional), professional.Id.ToString(), professional.ShopId,
            $"WhatsApp {what}; notifications {(contact.NotificationsEnabled ? "on" : "off")}",
            SummaryArgs: AuditArgs.Of(("by", "admin"), ("number", what.Replace("number ", string.Empty, StringComparison.Ordinal)), ("notifications", contact.NotificationsEnabled))));
        await db.SaveChangesAsync(cancellationToken);
        return await support.ToResponseAsync(professional, contact, cancellationToken);
    }
}

internal sealed class RevealProfessionalWhatsAppHandler(
    TrimmeDbContext db,
    IAdminDataScope scope,
    IPersonalDataProtector protector,
    IAuditLog audit) : ICommandHandler<RevealProfessionalWhatsAppCommand, Result<RevealedWhatsAppResponse>>
{
    public async Task<Result<RevealedWhatsAppResponse>> Handle(RevealProfessionalWhatsAppCommand command, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var id = new ProfessionalId(command.ProfessionalId);
        var contact = await db.Set<ProfessionalContact>().AsNoTracking().SingleOrDefaultAsync(c => c.ProfessionalId == id, cancellationToken);
        if (contact is null)
        {
            return await db.Set<Professional>().AnyAsync(p => p.Id == id, cancellationToken)
                ? ProfessionalErrors.NoNumber()
                : ProfessionalErrors.NotFound();
        }

        if (contact.ProtectedWhatsApp is null)
        {
            return ProfessionalErrors.NoNumber();
        }

        audit.Record(new AuditRecord(
            "professional.whatsapp_revealed", nameof(Professional), id.ToString(), contact.ShopId, "Full WhatsApp number revealed", command.Reason.Trim()));
        await db.SaveChangesAsync(cancellationToken);
        return new RevealedWhatsAppResponse(protector.Unprotect(contact.ProtectedWhatsApp, PersonalDataPurposes.ProfessionalWhatsApp));
    }
}

internal sealed class ReplaceProfessionalAvatarHandler(
    TrimmeDbContext db,
    IAdminDataScope scope,
    ProfessionalSupport support,
    IMediaStore media,
    IAuditLog audit,
    TimeProvider clock) : ICommandHandler<ReplaceProfessionalAvatarCommand, Result<AdminProfessionalResponse>>
{
    public async Task<Result<AdminProfessionalResponse>> Handle(ReplaceProfessionalAvatarCommand command, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var professional = await support.FindAsync(command.ProfessionalId, cancellationToken);
        if (professional is null)
        {
            return ProfessionalErrors.NotFound();
        }

        MediaId? next = null;
        if (command.Content is not null)
        {
            var stored = media.AddImage(command.Content, MediaPurpose.ProfessionalAvatar);
            if (stored.IsFailure)
            {
                return stored.Error;
            }

            next = stored.Value.Id;
        }

        if (professional.ReplaceAvatar(next, clock.GetUtcNow()) is { } previous)
        {
            media.Remove(previous);
        }

        audit.Record(new AuditRecord(
            "professional.avatar_changed", nameof(Professional), professional.Id.ToString(), professional.ShopId,
            command.Content is null ? "Avatar removed" : "Avatar replaced",
            SummaryArgs: AuditArgs.Of(("by", "admin"), ("change", command.Content is null ? "removed" : "replaced"))));
        await db.SaveChangesAsync(cancellationToken);
        var contact = await db.Set<ProfessionalContact>().AsNoTracking().SingleOrDefaultAsync(c => c.ProfessionalId == professional.Id, cancellationToken);
        return await support.ToResponseAsync(professional, contact, cancellationToken);
    }
}

internal sealed class ListProfessionalsHandler(TrimmeDbContext db, IAdminDataScope scope, IShopDirectory shops)
    : IQueryHandler<ListProfessionalsQuery, PagedResponse<AdminProfessionalListItem>>
{
    public async Task<PagedResponse<AdminProfessionalListItem>> Handle(ListProfessionalsQuery query, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var professionals = db.Set<Professional>().AsNoTracking();
        if (query.ShopId is { } shop)
        {
            var shopId = new ShopId(shop);
            professionals = professionals.Where(p => p.ShopId == shopId);
        }

        if (query.Status is { } status)
        {
            professionals = professionals.Where(p => p.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = $"%{query.Search.Trim()}%";
            professionals = professionals.Where(p => EF.Functions.ILike(p.NameAr, term) || EF.Functions.ILike(p.NameEn, term) || EF.Functions.ILike(p.Slug, term));
        }

        var total = await professionals.CountAsync(cancellationToken);
        var page = await professionals
            .OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id)
            .Skip(query.Page.Skip).Take(query.Page.PageSize)
            .ToListAsync(cancellationToken);

        // Two batched look-ups for the page (contacts, shop names): no per-row queries.
        var ids = page.Select(p => p.Id).ToArray();
        var contacts = await db.Set<ProfessionalContact>().AsNoTracking()
            .Where(c => ids.Contains(c.ProfessionalId))
            .ToDictionaryAsync(c => c.ProfessionalId, cancellationToken);
        var shopNames = await shops.FindManyAsync([.. page.Select(p => p.ShopId)], cancellationToken);

        var items = page.Select(p => new AdminProfessionalListItem(
            p.Id.Value,
            p.ShopId.Value,
            shopNames.GetValueOrDefault(p.ShopId)?.NameAr ?? string.Empty,
            shopNames.GetValueOrDefault(p.ShopId)?.NameEn ?? string.Empty,
            p.NameAr,
            p.NameEn,
            p.SpecialtyAr,
            p.SpecialtyEn,
            MediaRules.Url(p.AvatarMediaId),
            p.Status,
            ProfessionalSupport.WhatsApp(contacts.GetValueOrDefault(p.Id))));
        return new PagedResponse<AdminProfessionalListItem>([.. items], query.Page.Page, query.Page.PageSize, total);
    }
}

internal sealed class GetProfessionalHandler(TrimmeDbContext db, IAdminDataScope scope, ProfessionalSupport support)
    : IQueryHandler<GetProfessionalQuery, AdminProfessionalResponse?>
{
    public async Task<AdminProfessionalResponse?> Handle(GetProfessionalQuery query, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var id = new ProfessionalId(query.ProfessionalId);
        var professional = await db.Set<Professional>().AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (professional is null)
        {
            return null;
        }

        var contact = await db.Set<ProfessionalContact>().AsNoTracking().SingleOrDefaultAsync(c => c.ProfessionalId == id, cancellationToken);
        return await support.ToResponseAsync(professional, contact, cancellationToken);
    }
}
