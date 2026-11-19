using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Auditing;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Media;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Qr;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Media;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Privacy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Professionals.Domain;

namespace Trimme.Modules.Professionals.Application;

// The owner's barbers (D-146, at the user's request; it overrides spec §7 "may not create professionals or change their
// core profile" for the owner only). Every use case reads through the tenant filter: the shop is the owner's own, from
// the claims, and another shop's barber is not found (404). A barber's shop is fixed at creation and never changes
// (D-011); there is no delete (a barber with history is disabled instead) and the number is never shown unmasked.

/// <summary>The barber's WhatsApp settings as the owner sees them (D-146): the number masked only, with no reveal.</summary>
public sealed record ShopBarberWhatsAppResponse(string? Masked, bool NotificationsEnabled, WhatsAppVerification Verification);

/// <summary>
/// A barber as the shop's owner manages them (D-146): profile, photo, status and the masked WhatsApp number, which is also
/// their sign-in. Owner only (<c>Shop.Barbers.Manage</c>); the shop-wide list a barber reads stays without contact data.
/// </summary>
public sealed record ShopBarberResponse(
    Guid Id,
    string Slug,
    string NameAr,
    string NameEn,
    string? SpecialtyAr,
    string? SpecialtyEn,
    string? BioAr,
    string? BioEn,
    string? AvatarUrl,
    ProfessionalStatus Status,
    ShopBarberWhatsAppResponse WhatsApp,
    DateTimeOffset CreatedAt,
    uint Version,
    IReadOnlyList<string> Languages);

/// <summary>
/// A barber the owner just added, and the temporary password of the login made with them (D-146). The password is in this
/// one answer only, for the owner to hand to the barber, who replaces it at the first sign-in.
/// </summary>
public sealed record ShopBarberCreatedResponse(ShopBarberResponse Barber, string TemporaryPassword)
{
    public override string ToString() => $"{nameof(ShopBarberCreatedResponse)} {{ Barber = {Barber.Id} }}";
}

/// <summary>The mobile is required: it is the barber's WhatsApp number and their sign-in (D-146).</summary>
internal sealed record CreateShopBarberCommand(
    string NameAr,
    string NameEn,
    string? SpecialtyAr,
    string? SpecialtyEn,
    string? BioAr,
    string? BioEn,
    string WhatsAppNumber,
    bool NotificationsEnabled,
    IReadOnlyList<string>? Languages = null) : ICommand<Result<ShopBarberCreatedResponse>>, IProfessionalProfileFields
{
    /// <summary>The owner does not choose the address of the barber's page: it is made from the English name.</summary>
    public string? Slug => null;

    public override string ToString() => $"{nameof(CreateShopBarberCommand)} {{ NotificationsEnabled = {NotificationsEnabled} }}";
}

internal sealed record GetShopBarberQuery(Guid ProfessionalId) : IQuery<ShopBarberResponse?>;

/// <summary>Profile edit; the address of the barber's page is kept. No shop field (D-011).</summary>
internal sealed record UpdateShopBarberCommand(
    Guid ProfessionalId,
    string NameAr,
    string NameEn,
    string? SpecialtyAr,
    string? SpecialtyEn,
    string? BioAr,
    string? BioEn,
    uint Version,
    IReadOnlyList<string>? Languages = null) : ICommand<Result<ShopBarberResponse>>, IProfessionalProfileFields
{
    public string? Slug => null;
}

/// <summary>
/// A new number (required: it is the sign-in, and the login follows it) or, with <c>KeepCurrentNumber</c>, the message
/// toggle only.
/// </summary>
internal sealed record SetShopBarberWhatsAppCommand(Guid ProfessionalId, string? WhatsAppNumber, bool NotificationsEnabled, bool KeepCurrentNumber)
    : ICommand<Result<ShopBarberResponse>>
{
    public override string ToString() =>
        $"{nameof(SetShopBarberWhatsAppCommand)} {{ ProfessionalId = {ProfessionalId}, KeepCurrentNumber = {KeepCurrentNumber} }}";
}

internal sealed record ChangeShopBarberStatusCommand(Guid ProfessionalId, bool Enable) : ICommand<Result<ShopBarberResponse>>;

internal sealed record ReplaceShopBarberAvatarCommand(Guid ProfessionalId, byte[]? Content) : ICommand<Result<ShopBarberResponse>>;

internal sealed class CreateShopBarberValidator : AbstractValidator<CreateShopBarberCommand>
{
    public CreateShopBarberValidator()
    {
        Include(new ProfessionalProfileRules<CreateShopBarberCommand>());
        RuleFor(c => c.WhatsAppNumber).Cascade(CascadeMode.Stop)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithErrorCode("validation.required")
            .Must(n => PhoneNumber.TryParseMobile(n, out _)).WithErrorCode("validation.phone_invalid");
    }
}

internal sealed class UpdateShopBarberValidator : AbstractValidator<UpdateShopBarberCommand>
{
    public UpdateShopBarberValidator() => Include(new ProfessionalProfileRules<UpdateShopBarberCommand>());
}

internal sealed class SetShopBarberWhatsAppValidator : AbstractValidator<SetShopBarberWhatsAppCommand>
{
    public SetShopBarberWhatsAppValidator() =>
        RuleFor(c => c.WhatsAppNumber).Cascade(CascadeMode.Stop)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithErrorCode("validation.required")
            .Must(n => PhoneNumber.TryParseMobile(n, out _)).WithErrorCode("validation.phone_invalid")
            .When(c => !c.KeepCurrentNumber);
}

internal static class ShopBarberMapping
{
    public static ShopBarberResponse Response(Professional professional, ProfessionalContact? contact) =>
        new(
            professional.Id.Value,
            professional.Slug,
            professional.NameAr,
            professional.NameEn,
            professional.SpecialtyAr,
            professional.SpecialtyEn,
            professional.BioAr,
            professional.BioEn,
            MediaRules.Url(professional.AvatarMediaId),
            professional.Status,
            new ShopBarberWhatsAppResponse(
                contact?.WhatsAppMasked, contact?.NotificationsEnabled ?? false, contact?.Verification ?? WhatsAppVerification.Unverified),
            professional.CreatedAt,
            professional.Version,
            professional.Languages);

    public static async Task<ShopBarberResponse> ResponseAsync(TrimmeDbContext db, Professional professional, CancellationToken cancellationToken) =>
        Response(
            professional,
            await db.Set<ProfessionalContact>().AsNoTracking().SingleOrDefaultAsync(c => c.ProfessionalId == professional.Id, cancellationToken));
}

/// <summary>
/// The owner adds a barber to their own shop with the barber's login (D-146): one transaction holds the barber, the
/// protected number and the account, whose generated temporary password is answered once.
/// </summary>
internal sealed class CreateShopBarberHandler(
    TrimmeDbContext db,
    ICurrentTenant tenant,
    ProfessionalSupport support,
    IBarberLoginSync logins,
    IProfessionalQrCodes qrCodes,
    IAuditLog audit,
    TimeProvider clock) : ICommandHandler<CreateShopBarberCommand, Result<ShopBarberCreatedResponse>>
{
    public async Task<Result<ShopBarberCreatedResponse>> Handle(CreateShopBarberCommand command, CancellationToken cancellationToken)
    {
        // The owner's own shop, from the claims (a suspended shop has none).
        if (tenant.ShopId is not { } shopId)
        {
            return ProfessionalErrors.ShopNotFound();
        }

        var id = EntityId.New<ProfessionalId>();
        var slug = await support.ChooseSlugAsync(shopId, null, command.NameEn, null, cancellationToken);
        if (slug.IsFailure)
        {
            return slug.Error;
        }

        var number = await support.ProtectAsync(command.WhatsAppNumber, id, cancellationToken);
        if (number.IsFailure)
        {
            return number.Error;
        }

        if (number.Value is not { } phone || support.LoginHashOf(command.WhatsAppNumber) is not { } loginHash)
        {
            return Error.Validation("validation.failed", "The number is required.",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["whatsAppNumber"] = ["validation.required"] });
        }

        var now = clock.GetUtcNow();
        var created = Professional.Create(id, shopId, slug.Value, ProfessionalMapping.Profile(command), now);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var professional = created.Value;
        var contact = ProfessionalContact.For(professional);
        var set = contact.Set(phone, command.NotificationsEnabled, now);
        if (set.IsFailure)
        {
            return set.Error;
        }

        // The barber, their number and their login, or none of them: a failure below leaves without committing.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Add(professional);
        db.Add(contact);
        audit.Record(new AuditRecord(
            "professional.created", nameof(Professional), id.ToString(), shopId,
            $"Added by the shop; WhatsApp set, notifications {(contact.NotificationsEnabled ? "on" : "off")}",
            SummaryArgs: AuditArgs.Of(("by", "shop"), ("whatsApp", true), ("notifications", contact.NotificationsEnabled))));

        // Their own QR code at once (D-155, the client's 7-2-3).
        await qrCodes.EnsureAsync(shopId, id, cancellationToken);
        if (await support.SaveAsync(cancellationToken) is { IsFailure: true } saved)
        {
            return saved.Error;
        }

        Result<string> login;
        try
        {
            login = await logins.CreateAsync(new NewBarberAccount(id, shopId, professional.NameAr, loginHash), cancellationToken);
        }
        catch (DbUpdateException exception) when (DatabaseErrors.IsUniqueViolationOn(exception, "login_phone_hash"))
        {
            return ProfessionalErrors.WhatsAppTaken();
        }

        if (login.IsFailure)
        {
            return login.Error;
        }

        // The login's audit entry, then everything at once.
        if (await support.SaveAsync(cancellationToken) is { IsFailure: true } audited)
        {
            return audited.Error;
        }

        await transaction.CommitAsync(cancellationToken);
        return new ShopBarberCreatedResponse(ShopBarberMapping.Response(professional, contact), login.Value);
    }
}

internal sealed class GetShopBarberHandler(TrimmeDbContext db) : IQueryHandler<GetShopBarberQuery, ShopBarberResponse?>
{
    public async Task<ShopBarberResponse?> Handle(GetShopBarberQuery query, CancellationToken cancellationToken)
    {
        var id = new ProfessionalId(query.ProfessionalId);
        var professional = await db.Set<Professional>().AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        return professional is null ? null : await ShopBarberMapping.ResponseAsync(db, professional, cancellationToken);
    }
}

/// <summary>
/// The owner's edit of a barber's profile (D-146), also the path of a barber renaming themselves (D-151,
/// <see cref="RenameOwnBarberHandler"/>). The barber's login shows their Arabic name, so it follows.
/// </summary>
internal sealed class UpdateShopBarberHandler(TrimmeDbContext db, ProfessionalSupport support, IBarberLoginSync logins, IAuditLog audit, TimeProvider clock)
    : ICommandHandler<UpdateShopBarberCommand, Result<ShopBarberResponse>>
{
    public async Task<Result<ShopBarberResponse>> Handle(UpdateShopBarberCommand command, CancellationToken cancellationToken)
    {
        var professional = await support.FindAsync(command.ProfessionalId, cancellationToken);
        if (professional is null)
        {
            return ProfessionalErrors.NotFound();
        }

        // Optimistic concurrency: a stale version makes the save fail with 409.
        db.Entry(professional).Property(p => p.Version).OriginalValue = command.Version;
        professional.UpdateProfile(ProfessionalMapping.Profile(command), clock.GetUtcNow());
        await logins.NameChangedAsync(professional.Id, professional.NameAr, cancellationToken);
        audit.Record(new AuditRecord("professional.updated", nameof(Professional), professional.Id.ToString(), professional.ShopId, "Profile edited by the shop",
            SummaryArgs: AuditArgs.Of(("by", "shop"))));
        await db.SaveChangesAsync(cancellationToken);
        return await ShopBarberMapping.ResponseAsync(db, professional, cancellationToken);
    }
}

internal sealed class SetShopBarberWhatsAppHandler(
    ProfessionalSupport support,
    IBarberLoginSync logins,
    IAuditLog audit,
    TimeProvider clock) : ICommandHandler<SetShopBarberWhatsAppCommand, Result<ShopBarberResponse>>
{
    public async Task<Result<ShopBarberResponse>> Handle(SetShopBarberWhatsAppCommand command, CancellationToken cancellationToken)
    {
        var professional = await support.FindAsync(command.ProfessionalId, cancellationToken);
        if (professional is null)
        {
            return ProfessionalErrors.NotFound();
        }

        var contact = await support.ContactAsync(professional, cancellationToken);
        var now = clock.GetUtcNow();
        string what;
        if (command.KeepCurrentNumber)
        {
            var toggled = contact.SetNotifications(command.NotificationsEnabled, now);
            if (toggled.IsFailure)
            {
                return toggled.Error;
            }

            what = "number unchanged";
        }
        else
        {
            var number = await support.ProtectAsync(command.WhatsAppNumber, professional.Id, cancellationToken);
            if (number.IsFailure)
            {
                return number.Error;
            }

            var numberChanged = contact.WhatsAppLookupHash != number.Value?.LookupHash;
            var hadNumber = contact.HasNumber;
            var set = contact.Set(number.Value, command.NotificationsEnabled, now);
            if (set.IsFailure)
            {
                return set.Error;
            }

            if (numberChanged)
            {
                // The barber's login follows their number (D-145): the new one signs in, the old one no longer does.
                await logins.NumberChangedAsync(professional.Id, support.LoginHashOf(command.WhatsAppNumber), cancellationToken);
            }

            what = !numberChanged ? "number unchanged" : hadNumber ? "number changed" : "number added";
        }

        // The audit trail never holds the number, only what happened to it.
        audit.Record(new AuditRecord(
            "professional.whatsapp_changed", nameof(Professional), professional.Id.ToString(), professional.ShopId,
            $"By the shop: WhatsApp {what}; notifications {(contact.NotificationsEnabled ? "on" : "off")}",
            SummaryArgs: AuditArgs.Of(("by", "shop"), ("number", what.Replace("number ", string.Empty, StringComparison.Ordinal)), ("notifications", contact.NotificationsEnabled))));
        if (await support.SaveAsync(cancellationToken) is { IsFailure: true } saved)
        {
            return saved.Error;
        }

        return ShopBarberMapping.Response(professional, contact);
    }
}

internal sealed class ChangeShopBarberStatusHandler(
    TrimmeDbContext db,
    ProfessionalSupport support,
    IBarberLoginSync logins,
    IProfessionalQrCodes qrCodes,
    IAuditLog audit,
    TimeProvider clock) : ICommandHandler<ChangeShopBarberStatusCommand, Result<ShopBarberResponse>>
{
    public async Task<Result<ShopBarberResponse>> Handle(ChangeShopBarberStatusCommand command, CancellationToken cancellationToken)
    {
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
            // A disabled barber can no longer sign in and is signed out at once (D-145); enabling them again leaves the
            // login off until the owner turns it on with a new temporary password.
            await logins.ProfessionalDisabledAsync(professional.Id, cancellationToken);
        }

        audit.Record(new AuditRecord(
            command.Enable ? "professional.enabled" : "professional.disabled",
            nameof(Professional), professional.Id.ToString(), professional.ShopId, $"By the shop; now {professional.Status}",
            SummaryArgs: AuditArgs.Of(("by", "shop"), ("status", professional.Status))));
        if (command.Enable)
        {
            // Turned on again without a code: one now (D-155).
            await qrCodes.EnsureAsync(professional.ShopId, professional.Id, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return await ShopBarberMapping.ResponseAsync(db, professional, cancellationToken);
    }
}

internal sealed class ReplaceShopBarberAvatarHandler(
    TrimmeDbContext db,
    ProfessionalSupport support,
    IMediaStore media,
    IAuditLog audit,
    TimeProvider clock) : ICommandHandler<ReplaceShopBarberAvatarCommand, Result<ShopBarberResponse>>
{
    public async Task<Result<ShopBarberResponse>> Handle(ReplaceShopBarberAvatarCommand command, CancellationToken cancellationToken)
    {
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
            command.Content is null ? "Avatar removed by the shop" : "Avatar replaced by the shop",
            SummaryArgs: AuditArgs.Of(("by", "shop"), ("change", command.Content is null ? "removed" : "replaced"))));
        await db.SaveChangesAsync(cancellationToken);
        return await ShopBarberMapping.ResponseAsync(db, professional, cancellationToken);
    }
}
