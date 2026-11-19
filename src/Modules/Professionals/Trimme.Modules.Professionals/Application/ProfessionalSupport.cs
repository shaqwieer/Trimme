using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Media;
using Trimme.BuildingBlocks.Application.Privacy;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Privacy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Professionals.Application.Admin;
using Trimme.Modules.Professionals.Domain;

namespace Trimme.Modules.Professionals.Application;

// Steps shared by the admin's professional use cases and the owner's barber use cases (D-146). They read through the
// caller's data scope: the admin scope the admin handlers open, or the owner's own shop (the tenant filter).

internal interface IProfessionalProfileFields
{
    string NameAr { get; }

    string NameEn { get; }

    string? Slug { get; }

    string? SpecialtyAr { get; }

    string? SpecialtyEn { get; }

    string? BioAr { get; }

    string? BioEn { get; }

    /// <summary>The languages the barber speaks (D-149); <see langword="null"/> keeps them on an edit.</summary>
    IReadOnlyList<string>? Languages { get; }
}

internal sealed class ProfessionalProfileRules<T> : AbstractValidator<T>
    where T : IProfessionalProfileFields
{
    public ProfessionalProfileRules()
    {
        RuleFor(p => p.NameAr).Must(n => !string.IsNullOrWhiteSpace(n)).WithErrorCode("validation.required")
            .MaximumLength(Professional.MaxNameLength).WithErrorCode("validation.too_long");
        RuleFor(p => p.NameEn).Must(n => !string.IsNullOrWhiteSpace(n)).WithErrorCode("validation.required")
            .MaximumLength(Professional.MaxNameLength).WithErrorCode("validation.too_long");
        RuleFor(p => p.Slug)
            .Must(s => string.IsNullOrWhiteSpace(s) || Professional.IsValidSlug(s.Trim().ToLowerInvariant()))
            .WithErrorCode("validation.slug_invalid");
        RuleFor(p => p.SpecialtyAr).MaximumLength(Professional.MaxSpecialtyLength).WithErrorCode("validation.too_long");
        RuleFor(p => p.SpecialtyEn).MaximumLength(Professional.MaxSpecialtyLength).WithErrorCode("validation.too_long");
        RuleFor(p => p.BioAr).MaximumLength(Professional.MaxBioLength).WithErrorCode("validation.too_long");
        RuleFor(p => p.BioEn).MaximumLength(Professional.MaxBioLength).WithErrorCode("validation.too_long");
        RuleForEach(p => p.Languages)
            .Must(code => code is not null && Professional.LanguageCodes.Contains(code.Trim().ToLowerInvariant()))
            .WithErrorCode("validation.invalid");
    }
}

/// <summary>Shared steps of the professional use cases, the admin's and the owner's (D-146).</summary>
internal sealed class ProfessionalSupport(TrimmeDbContext db, IPersonalDataProtector protector, IShopDirectory shops)
{
    public Task<Professional?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var professionalId = new ProfessionalId(id);
        return db.Set<Professional>().SingleOrDefaultAsync(p => p.Id == professionalId, cancellationToken);
    }

    public async Task<ProfessionalContact> ContactAsync(Professional professional, CancellationToken cancellationToken)
    {
        var contact = await db.Set<ProfessionalContact>().SingleOrDefaultAsync(c => c.ProfessionalId == professional.Id, cancellationToken);
        if (contact is null)
        {
            contact = ProfessionalContact.For(professional);
            db.Add(contact);
        }

        return contact;
    }

    /// <summary>The barber-login hash of a valid number (D-145), or <see langword="null"/> without one.</summary>
    public string? LoginHashOf(string? input) =>
        PhoneNumber.TryParseMobile(input, out var phone) ? protector.LookupHash(phone.E164, PersonalDataPurposes.BarberLogin) : null;

    /// <summary>
    /// Saves the unit of work. A number another professional already has is answered as <see cref="ProfessionalErrors.WhatsAppTaken"/>
    /// even when <see cref="ProtectAsync"/> could not see that professional: the owner's check reads their own shop only, the
    /// unique indexes on the number and on the barber login see every shop (D-146). Never says which shop or barber has it.
    /// </summary>
    public async Task<Result> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbUpdateException exception) when (
            DatabaseErrors.IsUniqueViolationOn(exception, "whatsapp_lookup_hash") || DatabaseErrors.IsUniqueViolationOn(exception, "login_phone_hash"))
        {
            return ProfessionalErrors.WhatsAppTaken();
        }
    }

    /// <summary>Encrypts a valid mobile number, or reports that another professional already has it.</summary>
    public async Task<Result<ProtectedPhone?>> ProtectAsync(string? input, ProfessionalId owner, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return (ProtectedPhone?)null;
        }

        if (!PhoneNumber.TryParseMobile(input, out var phone))
        {
            return Error.Validation("validation.failed", "The number is invalid.",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["whatsAppNumber"] = ["validation.phone_invalid"] });
        }

        var hash = protector.LookupHash(phone.E164, PersonalDataPurposes.ProfessionalWhatsApp);
        if (await db.Set<ProfessionalContact>().AnyAsync(c => c.WhatsAppLookupHash == hash && c.ProfessionalId != owner, cancellationToken))
        {
            return ProfessionalErrors.WhatsAppTaken();
        }

        return new ProtectedPhone(protector.Protect(phone.E164, PersonalDataPurposes.ProfessionalWhatsApp), hash, phone.Masked);
    }

    public Task<bool> SlugTakenAsync(ShopId shopId, string slug, ProfessionalId? except, CancellationToken cancellationToken) =>
        db.Set<Professional>().AnyAsync(p => p.ShopId == shopId && p.Slug == slug && (except == null || p.Id != except), cancellationToken);

    /// <summary>The requested slug, or one derived from the English name that is free in the shop.</summary>
    public async Task<Result<string>> ChooseSlugAsync(ShopId shopId, string? requested, string nameEn, ProfessionalId? except, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var slug = requested.Trim().ToLowerInvariant();
            return await SlugTakenAsync(shopId, slug, except, cancellationToken) ? ProfessionalErrors.SlugTaken() : slug;
        }

        var baseSlug = Professional.SlugFrom(nameEn, $"pro-{Guid.NewGuid():N}"[..12]);
        for (var suffix = 1; suffix < 20; suffix++)
        {
            var candidate = suffix == 1 ? baseSlug : $"{baseSlug}-{suffix}";
            if (!await SlugTakenAsync(shopId, candidate, except, cancellationToken))
            {
                return candidate;
            }
        }

        return $"{baseSlug}-{Guid.NewGuid():N}"[..Math.Min(baseSlug.Length + 9, 60)];
    }

    public async Task<AdminProfessionalResponse> ToResponseAsync(Professional professional, ProfessionalContact? contact, CancellationToken cancellationToken)
    {
        var shop = await shops.FindAsync(professional.ShopId, cancellationToken);
        return new AdminProfessionalResponse(
            professional.Id.Value,
            professional.ShopId.Value,
            shop?.Slug ?? string.Empty,
            shop?.NameAr ?? string.Empty,
            shop?.NameEn ?? string.Empty,
            professional.Slug,
            professional.NameAr,
            professional.NameEn,
            professional.SpecialtyAr,
            professional.SpecialtyEn,
            professional.BioAr,
            professional.BioEn,
            MediaRules.Url(professional.AvatarMediaId),
            professional.Status,
            WhatsApp(contact),
            professional.CreatedAt,
            professional.UpdatedAt,
            professional.Version,
            professional.Languages);
    }

    public static AdminWhatsAppResponse WhatsApp(ProfessionalContact? contact) =>
        new(contact?.WhatsAppMasked, contact?.NotificationsEnabled ?? false, contact?.Verification ?? WhatsAppVerification.Unverified);
}

internal static class ProfessionalMapping
{
    public static ProfessionalProfile Profile(IProfessionalProfileFields fields) =>
        ProfessionalProfile.Create(fields.NameAr, fields.NameEn, fields.SpecialtyAr, fields.SpecialtyEn, fields.BioAr, fields.BioEn, fields.Languages);
}
