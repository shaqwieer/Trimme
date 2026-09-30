using System.Text.RegularExpressions;
using Trimme.BuildingBlocks.Domain.Media;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.Modules.Professionals.Domain;

public enum ProfessionalStatus
{
    Active,
    Disabled,
}

/// <summary>
/// A barber or stylist: a managed entity of exactly one shop (spec §2, §7). Created by a platform admin.
/// <see cref="ShopId"/> is set once, here, and never changes (D-011, R-NEG-01).
/// The WhatsApp number lives in <see cref="ProfessionalContact"/>, never in this aggregate's public shape.
/// </summary>
public sealed partial class Professional : AggregateRoot<ProfessionalId>, IShopOwned, IConcurrencyVersioned, IPublicContent
{
    public const int MaxNameLength = 120;
    public const int MaxSpecialtyLength = 80;
    public const int MaxBioLength = 600;

    private Professional(ProfessionalId id, ShopId shopId, string slug, ProfessionalProfile profile, DateTimeOffset now)
        : base(id)
    {
        ShopId = shopId;
        Slug = slug;
        NameAr = profile.NameAr;
        NameEn = profile.NameEn;
        SpecialtyAr = profile.SpecialtyAr;
        SpecialtyEn = profile.SpecialtyEn;
        BioAr = profile.BioAr;
        BioEn = profile.BioEn;
        Status = ProfessionalStatus.Active;
        CreatedAt = now;
    }

    private Professional()
    {
        Slug = NameAr = NameEn = string.Empty;
    }

    /// <summary>The one shop this professional belongs to. Private setter for EF only; no method changes it.</summary>
    public ShopId ShopId { get; private set; }

    /// <summary>URL segment under the shop page (<c>/shops/{shop}/professionals/{slug}</c>), unique within the shop.</summary>
    public string Slug { get; private set; }

    public string NameAr { get; private set; }

    public string NameEn { get; private set; }

    public string? SpecialtyAr { get; private set; }

    public string? SpecialtyEn { get; private set; }

    public string? BioAr { get; private set; }

    public string? BioEn { get; private set; }

    public MediaId? AvatarMediaId { get; private set; }

    public ProfessionalStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public static Result<Professional> Create(ProfessionalId id, ShopId shopId, string slug, ProfessionalProfile profile, DateTimeOffset now)
    {
        if (shopId.Value == Guid.Empty)
        {
            return ProfessionalErrors.ShopRequired();
        }

        var normalized = slug.Trim().ToLowerInvariant();
        return IsValidSlug(normalized)
            ? new Professional(id, shopId, normalized, profile, now)
            : ProfessionalErrors.InvalidSlug();
    }

    /// <summary>Edits the profile. Deliberately takes no shop: the shop cannot change (D-011).</summary>
    public void UpdateProfile(ProfessionalProfile profile, DateTimeOffset now)
    {
        NameAr = profile.NameAr;
        NameEn = profile.NameEn;
        SpecialtyAr = profile.SpecialtyAr;
        SpecialtyEn = profile.SpecialtyEn;
        BioAr = profile.BioAr;
        BioEn = profile.BioEn;
        UpdatedAt = now;
    }

    /// <summary>Changes the URL segment; the caller checks it is free in the shop.</summary>
    public Result ChangeSlug(string slug, DateTimeOffset now)
    {
        var normalized = slug.Trim().ToLowerInvariant();
        if (!IsValidSlug(normalized))
        {
            return ProfessionalErrors.InvalidSlug();
        }

        Slug = normalized;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result Disable(DateTimeOffset now) => ChangeStatus(ProfessionalStatus.Disabled, now);

    public Result Enable(DateTimeOffset now) => ChangeStatus(ProfessionalStatus.Active, now);

    /// <summary>Replaces the avatar; returns the image it replaced, which the caller deletes.</summary>
    public MediaId? ReplaceAvatar(MediaId? avatar, DateTimeOffset now)
    {
        var previous = AvatarMediaId;
        AvatarMediaId = avatar;
        UpdatedAt = now;
        return previous;
    }

    public static bool IsValidSlug(string slug) => SlugPattern().IsMatch(slug);

    /// <summary>A URL-safe slug from the English name ("Faisal Al-Qahtani" → "faisal-al-qahtani"), or a fallback.</summary>
    public static string SlugFrom(string nameEn, string fallback)
    {
        var slug = NonSlugCharacters().Replace(nameEn.Trim().ToLowerInvariant(), "-").Trim('-');
        slug = RepeatedDashes().Replace(slug, "-");
        if (slug.Length > 50)
        {
            slug = slug[..50].Trim('-');
        }

        return IsValidSlug(slug) ? slug : fallback;
    }

    private Result ChangeStatus(ProfessionalStatus next, DateTimeOffset now)
    {
        if (Status == next)
        {
            return ProfessionalErrors.InvalidTransition(Status, next);
        }

        Status = next;
        UpdatedAt = now;
        return Result.Success();
    }

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{1,58}[a-z0-9])$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonSlugCharacters();

    [GeneratedRegex("-{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex RepeatedDashes();
}

/// <summary>Normalized profile text (trimmed; empty optional text → null).</summary>
public sealed record ProfessionalProfile(string NameAr, string NameEn, string? SpecialtyAr, string? SpecialtyEn, string? BioAr, string? BioEn)
{
    public static ProfessionalProfile Create(string nameAr, string nameEn, string? specialtyAr, string? specialtyEn, string? bioAr, string? bioEn) =>
        new(nameAr.Trim(), nameEn.Trim(), Clean(specialtyAr), Clean(specialtyEn), Clean(bioAr), Clean(bioEn));

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Delivery state of the WhatsApp number, set by the notification worker (Phase 15).</summary>
public enum WhatsAppVerification
{
    Unverified,
    Verified,
    Failed,
}

/// <summary>
/// The professional's WhatsApp contact (spec §8, D-026): the E.164 number encrypted at rest, a keyed lookup hash
/// (unique across the platform, so one number cannot become a second professional in another shop), and a masked form
/// for admin lists. Only admin commands with the right permission and the notification worker read the number.
/// </summary>
public sealed class ProfessionalContact : IShopOwned
{
    private ProfessionalContact(ProfessionalId professionalId, ShopId shopId)
    {
        ProfessionalId = professionalId;
        ShopId = shopId;
    }

    private ProfessionalContact()
    {
    }

    public ProfessionalId ProfessionalId { get; private set; }

    public ShopId ShopId { get; private set; }

    /// <summary>Data Protection ciphertext of the E.164 number (purpose <c>trimme.professional-whatsapp</c>).</summary>
    public string? ProtectedWhatsApp { get; private set; }

    public string? WhatsAppLookupHash { get; private set; }

    /// <summary>For lists: <c>+966 5•• ••• •30</c>.</summary>
    public string? WhatsAppMasked { get; private set; }

    public bool NotificationsEnabled { get; private set; }

    public WhatsAppVerification Verification { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public bool HasNumber => ProtectedWhatsApp is not null;

    /// <summary>A professional notification is sent only to a valid, enabled number (spec §16).</summary>
    public bool CanReceiveNotifications => HasNumber && NotificationsEnabled;

    public static ProfessionalContact For(Professional professional) => new(professional.Id, professional.ShopId);

    /// <summary>What the WhatsApp provider reported for the number (Phase 15): delivered verifies it, a failure marks it failed.</summary>
    public void RecordDelivery(bool delivered, DateTimeOffset now)
    {
        if (!HasNumber)
        {
            return;
        }

        Verification = delivered ? WhatsAppVerification.Verified : WhatsAppVerification.Failed;
        UpdatedAt = now;
    }

    /// <summary>Turns notifications on or off for the current number (none set: they can only be off).</summary>
    public Result SetNotifications(bool enabled, DateTimeOffset now)
    {
        if (enabled && !HasNumber)
        {
            return ProfessionalErrors.NotificationsNeedNumber();
        }

        NotificationsEnabled = enabled;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>Sets or clears the number. Notifications can only be on while a number is set.</summary>
    public Result Set(ProtectedPhone? number, bool notificationsEnabled, DateTimeOffset now)
    {
        if (notificationsEnabled && number is null)
        {
            return ProfessionalErrors.NotificationsNeedNumber();
        }

        if (number?.LookupHash != WhatsAppLookupHash)
        {
            Verification = WhatsAppVerification.Unverified;
        }

        ProtectedWhatsApp = number?.Ciphertext;
        WhatsAppLookupHash = number?.LookupHash;
        WhatsAppMasked = number?.Masked;
        NotificationsEnabled = notificationsEnabled;
        UpdatedAt = now;
        return Result.Success();
    }
}

/// <summary>An already protected phone number: ciphertext, lookup hash and mask (built by the application layer).</summary>
public sealed record ProtectedPhone(string Ciphertext, string LookupHash, string Masked);

public static class ProfessionalErrors
{
    public static Error NotFound() => Error.NotFound("professional.not_found", "The professional was not found.");

    public static Error ShopRequired() =>
        Error.Validation("validation.failed", "A professional belongs to exactly one shop.",
            new Dictionary<string, string[]>(StringComparer.Ordinal) { ["shopId"] = ["validation.required"] });

    public static Error ShopNotFound() =>
        Error.Validation("validation.failed", "The shop does not exist.",
            new Dictionary<string, string[]>(StringComparer.Ordinal) { ["shopId"] = ["validation.invalid"] });

    public static Error InvalidSlug() =>
        Error.Validation("validation.failed", "The slug is invalid.",
            new Dictionary<string, string[]>(StringComparer.Ordinal) { ["slug"] = ["validation.slug_invalid"] });

    public static Error SlugTaken() =>
        Error.Validation("validation.failed", "The slug is already used in this shop.",
            new Dictionary<string, string[]>(StringComparer.Ordinal) { ["slug"] = ["validation.slug_taken"] });

    public static Error WhatsAppTaken() =>
        Error.Validation("validation.failed", "This number already belongs to another professional.",
            new Dictionary<string, string[]>(StringComparer.Ordinal) { ["whatsAppNumber"] = ["validation.whatsapp_taken"] });

    public static Error NotificationsNeedNumber() =>
        Error.Validation("validation.failed", "WhatsApp notifications need a number.",
            new Dictionary<string, string[]>(StringComparer.Ordinal) { ["notificationsEnabled"] = ["validation.whatsapp_required"] });

    public static Error NoNumber() => Error.NotFound("professional.no_whatsapp", "The professional has no WhatsApp number.");

    public static Error InvalidTransition(ProfessionalStatus from, ProfessionalStatus to) =>
        Error.Conflict("professional.invalid_transition", $"A {from} professional cannot become {to}.");
}
