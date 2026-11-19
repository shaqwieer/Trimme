using System.Text.RegularExpressions;
using Trimme.BuildingBlocks.Domain.Media;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.Modules.Shops.Domain;

/// <summary>
/// A salon or barber shop: the tenant root (spec §7). Created by a platform admin as Draft. It carries its public
/// profile, its exact location and the admin policy that says which profile fields the shop may edit (Phase 06).
/// Booking settings arrive in Phases 08–10.
/// </summary>
public sealed partial class Shop : AggregateRoot<ShopId>, ITenantRoot, IConcurrencyVersioned, IPublicContent
{
    public const string DefaultTimeZone = "Asia/Riyadh";
    public const int MaxNameLength = 120;

    /// <summary>The short name's length (D-152): «الأصالة», not «صالون الأصالة للحلاقة».</summary>
    public const int MaxShortNameLength = 40;
    public const int MaxDescriptionLength = 1000;
    public const int MaxGalleryImages = 12;

    /// <summary>What a new shop may edit until an admin changes its policy (D-065).</summary>
    public static readonly IReadOnlyList<ShopProfileField> DefaultEditableFields =
    [
        ShopProfileField.Description,
        ShopProfileField.PublicPhone,
        ShopProfileField.Amenities,
        ShopProfileField.Logo,
        ShopProfileField.Cover,
        ShopProfileField.Gallery,
    ];

    private Shop(ShopId id, string slug, string nameAr, string nameEn, string timeZone, DateTimeOffset now)
        : base(id)
    {
        Slug = slug;
        NameAr = nameAr;
        NameEn = nameEn;
        TimeZone = timeZone;
        Status = ShopStatus.Draft;
        Category = ShopCategory.Barbershop;
        EditableFields = [.. DefaultEditableFields];
        CreatedAt = now;
    }

    private Shop()
    {
        Slug = NameAr = NameEn = TimeZone = string.Empty;
    }

    /// <summary>URL identifier of the public shop page (<c>/shops/{slug}</c>): lowercase Latin letters, digits and dashes.</summary>
    public string Slug { get; private set; }

    public string NameAr { get; private set; }

    public string NameEn { get; private set; }

    /// <summary>
    /// The shop's short name (D-152, the client's 3-8: «الأصالة» without «صالون»), shown beside the welcome on the shop's
    /// own screens. Optional: without it the full name is shown. The owner may always change it, whatever the admin's
    /// policy for the name.
    /// </summary>
    public string? ShortNameAr { get; private set; }

    public string? ShortNameEn { get; private set; }

    public string? DescriptionAr { get; private set; }

    public string? DescriptionEn { get; private set; }

    public ShopCategory Category { get; private set; }

    /// <summary>The shop's own public business number (E.164). Business data, not customer data.</summary>
    public string? PublicPhone { get; private set; }

    public ShopAmenity[] Amenities { get; private set; } = [];

    /// <summary>Set by an admin after checking the shop's documents; never editable by the shop.</summary>
    public bool IsVerified { get; private set; }

    /// <summary>
    /// A demo shop (D-148): its card and page say «تجريبي» so its data never passes for a real salon. Set by the platform
    /// admin; turned off (or the shop archived) at launch.
    /// </summary>
    public bool IsDemo { get; private set; }

    /// <summary>
    /// D-155 (the client's 5-1-7): the «اتصال بالمحل» button on the public page, the admin's choice only (off by default).
    /// The owner never sees or edits it; it is not <see cref="PublicPhone"/>, which the owner edits.
    /// </summary>
    public bool CallButtonEnabled { get; private set; }

    /// <summary>The number the call button dials, in E.164, set by the admin (D-155).</summary>
    public string? CallButtonPhone { get; private set; }

    public MediaId? LogoMediaId { get; private set; }

    public MediaId? CoverMediaId { get; private set; }

    /// <summary>Gallery images in display order (stored images, D-064).</summary>
    public Guid[] GalleryMediaIds { get; private set; } = [];

    /// <summary>The exact entrance, set with the map pin picker; <see langword="null"/> until an admin places it.</summary>
    public ShopLocation? Location { get; private set; }

    /// <summary>Profile fields the shop itself may change (admin policy, DV-S16). Enforced server-side.</summary>
    public ShopProfileField[] EditableFields { get; private set; } = [];

    public ShopStatus Status { get; private set; }

    /// <summary>IANA time zone used for opening hours and availability (D-030).</summary>
    public string TimeZone { get; private set; }

    /// <summary>The shop's own start-time interval (D-149); <see langword="null"/> follows the platform's.</summary>
    public int? SlotStepMinutes { get; private set; }

    /// <summary>The shop's own minimum notice before an online booking (D-149); <see langword="null"/> follows the platform's.</summary>
    public int? MinLeadTimeMinutes { get; private set; }

    /// <summary>The shop's own furthest bookable day, counted from today (D-149); <see langword="null"/> follows the platform's.</summary>
    public int? BookingHorizonDays { get; private set; }

    /// <summary>Cleaning time after each booking before the barber's next one (D-149); 0 = none.</summary>
    public int BufferMinutes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public static Result<Shop> Create(ShopId id, string slug, string nameAr, string nameEn, string? timeZone, DateTimeOffset now)
    {
        var normalizedSlug = slug.Trim().ToLowerInvariant();
        if (!IsValidSlug(normalizedSlug))
        {
            return ShopErrors.InvalidSlug();
        }

        return new Shop(id, normalizedSlug, nameAr.Trim(), nameEn.Trim(), string.IsNullOrWhiteSpace(timeZone) ? DefaultTimeZone : timeZone, now);
    }

    public Result Activate(DateTimeOffset now)
    {
        if (Status == ShopStatus.Active)
        {
            return ShopErrors.InvalidTransition(Status, ShopStatus.Active);
        }

        Status = ShopStatus.Active;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result Suspend(DateTimeOffset now)
    {
        if (Status == ShopStatus.Suspended)
        {
            return ShopErrors.InvalidTransition(Status, ShopStatus.Suspended);
        }

        Status = ShopStatus.Suspended;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>Whether the shop may change <paramref name="field"/>: the admin's policy, and always its short name (D-152).</summary>
    public bool IsEditableByShop(ShopProfileField field) => field == ShopProfileField.ShortName || EditableFields.Contains(field);

    /// <summary>The profile fields whose value <paramref name="profile"/> would change.</summary>
    public IReadOnlyList<ShopProfileField> ChangedFields(ShopProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var changed = new List<ShopProfileField>();
        if (profile.NameAr != NameAr || profile.NameEn != NameEn)
        {
            changed.Add(ShopProfileField.Name);
        }

        if (profile.ShortNameAr != ShortNameAr || profile.ShortNameEn != ShortNameEn)
        {
            changed.Add(ShopProfileField.ShortName);
        }

        if (profile.DescriptionAr != DescriptionAr || profile.DescriptionEn != DescriptionEn)
        {
            changed.Add(ShopProfileField.Description);
        }

        if (profile.Category != Category)
        {
            changed.Add(ShopProfileField.Category);
        }

        if (profile.PublicPhone != PublicPhone)
        {
            changed.Add(ShopProfileField.PublicPhone);
        }

        if (!profile.Amenities.Order().SequenceEqual(Amenities.Order()))
        {
            changed.Add(ShopProfileField.Amenities);
        }

        return changed;
    }

    /// <summary>Applies a normalized profile (see <see cref="ShopProfile.Create"/>); returns the fields that changed.</summary>
    public IReadOnlyList<ShopProfileField> UpdateProfile(ShopProfile profile, DateTimeOffset now)
    {
        var changed = ChangedFields(profile);
        NameAr = profile.NameAr;
        NameEn = profile.NameEn;
        ShortNameAr = profile.ShortNameAr;
        ShortNameEn = profile.ShortNameEn;
        DescriptionAr = profile.DescriptionAr;
        DescriptionEn = profile.DescriptionEn;
        Category = profile.Category;
        PublicPhone = profile.PublicPhone;
        Amenities = [.. profile.Amenities.Distinct().Order()];
        if (changed.Count > 0)
        {
            UpdatedAt = now;
        }

        return changed;
    }

    public void SetVerified(bool verified, DateTimeOffset now)
    {
        IsVerified = verified;
        UpdatedAt = now;
    }

    /// <summary>
    /// The shop's own booking settings (D-149, the client's «إعدادات الحجز خاصة بكل محل»): the start-time
    /// interval, the minimum notice, the furthest bookable day and the cleaning time after each booking. The values are
    /// validated by <see cref="ShopBookingRules"/>.
    /// </summary>
    public Result SetBookingSettings(int slotStepMinutes, int minLeadTimeMinutes, int bookingHorizonDays, int bufferMinutes, DateTimeOffset now)
    {
        if (ShopBookingRules.Validate(slotStepMinutes, minLeadTimeMinutes, bookingHorizonDays, bufferMinutes) is { } invalid)
        {
            return invalid;
        }

        SlotStepMinutes = slotStepMinutes;
        MinLeadTimeMinutes = minLeadTimeMinutes;
        BookingHorizonDays = bookingHorizonDays;
        BufferMinutes = bufferMinutes;
        UpdatedAt = now;
        return Result.Success();
    }

    public void SetDemo(bool demo, DateTimeOffset now)
    {
        IsDemo = demo;
        UpdatedAt = now;
    }

    /// <summary>The admin's call button (D-155): on needs a number; off keeps the number for next time.</summary>
    public void SetCallButton(bool enabled, string? phoneE164, DateTimeOffset now)
    {
        CallButtonEnabled = enabled;
        CallButtonPhone = phoneE164 ?? CallButtonPhone;
        UpdatedAt = now;
    }

    public void SetEditablePolicy(IEnumerable<ShopProfileField> fields, DateTimeOffset now)
    {
        EditableFields = [.. fields.Distinct().Order()];
        UpdatedAt = now;
    }

    public void SetLocation(ShopLocation location, DateTimeOffset now)
    {
        Location = location;
        UpdatedAt = now;
    }

    /// <summary>Replaces the logo; returns the image it replaced, which the caller deletes.</summary>
    public MediaId? ReplaceLogo(MediaId? logo, DateTimeOffset now)
    {
        var previous = LogoMediaId;
        LogoMediaId = logo;
        UpdatedAt = now;
        return previous;
    }

    /// <summary>Replaces the cover; returns the image it replaced, which the caller deletes.</summary>
    public MediaId? ReplaceCover(MediaId? cover, DateTimeOffset now)
    {
        var previous = CoverMediaId;
        CoverMediaId = cover;
        UpdatedAt = now;
        return previous;
    }

    public Result AddGalleryImage(MediaId image, DateTimeOffset now)
    {
        if (GalleryMediaIds.Length >= MaxGalleryImages)
        {
            return ShopErrors.GalleryFull();
        }

        GalleryMediaIds = [.. GalleryMediaIds, image.Value];
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>Removes an image from this shop's gallery; false when it is not in this gallery.</summary>
    public bool RemoveGalleryImage(MediaId image, DateTimeOffset now)
    {
        if (!GalleryMediaIds.Contains(image.Value))
        {
            return false;
        }

        GalleryMediaIds = [.. GalleryMediaIds.Where(id => id != image.Value)];
        UpdatedAt = now;
        return true;
    }

    /// <summary>Slugs that name API routes under <c>/public/shops/</c> (the discovery search) and so cannot be shop pages.</summary>
    public static readonly IReadOnlySet<string> ReservedSlugs = new HashSet<string>(StringComparer.Ordinal) { "search" };

    public static bool IsValidSlug(string slug) => SlugPattern().IsMatch(slug) && !ReservedSlugs.Contains(slug);

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{1,58}[a-z0-9])$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
}

/// <summary>The bounds of a shop's own booking settings (D-149).</summary>
public static class ShopBookingRules
{
    public static readonly IReadOnlySet<int> SlotSteps = new HashSet<int> { 5, 10, 15, 20, 30, 60 };
    public const int MaxLeadTimeMinutes = 1440;
    public const int MinHorizonDays = 1;
    public const int MaxHorizonDays = 90;
    public const int MaxBufferMinutes = 60;
    public const int BufferStep = 5;

    public static Error? Validate(int slotStepMinutes, int minLeadTimeMinutes, int bookingHorizonDays, int bufferMinutes)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!SlotSteps.Contains(slotStepMinutes))
        {
            errors["slotStepMinutes"] = ["validation.out_of_range"];
        }

        if (minLeadTimeMinutes is < 0 or > MaxLeadTimeMinutes)
        {
            errors["minLeadTimeMinutes"] = ["validation.out_of_range"];
        }

        if (bookingHorizonDays is < MinHorizonDays or > MaxHorizonDays)
        {
            errors["bookingHorizonDays"] = ["validation.out_of_range"];
        }

        if (bufferMinutes is < 0 or > MaxBufferMinutes || bufferMinutes % BufferStep != 0)
        {
            errors["bufferMinutes"] = ["validation.out_of_range"];
        }

        return errors.Count == 0 ? null : Error.Validation("validation.failed", "The booking settings are out of range.", errors);
    }
}

/// <summary>
/// Kind of business. Fixed to a men's barbershop since D-149 (the platform is for men); the other values stay so older
/// rows and the contract read the same.
/// </summary>
public enum ShopCategory
{
    Barbershop,
    Salon,
    Unisex,
}

public enum ShopAmenity
{
    Parking,
    WiFi,
    KidsFriendly,
    WheelchairAccessible,
    WaitingArea,
    PrayerArea,
}

/// <summary>Profile areas an admin can open to or lock from the shop (DV-S16). Verification and slug are never shop-editable.</summary>
public enum ShopProfileField
{
    Name,
    Description,
    Category,
    PublicPhone,
    Amenities,
    Logo,
    Cover,
    Gallery,
    Location,

    /// <summary>The short name (D-152): always the shop's to change (<see cref="Shop.IsEditableByShop"/>).</summary>
    ShortName,
}

/// <summary>The editable text part of a shop profile, normalized (trimmed, empty → null, phone in E.164).</summary>
public sealed record ShopProfile(
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    ShopCategory Category,
    string? PublicPhone,
    IReadOnlyList<ShopAmenity> Amenities,
    string? ShortNameAr = null,
    string? ShortNameEn = null)
{
    public static ShopProfile Create(
        string nameAr,
        string nameEn,
        string? descriptionAr,
        string? descriptionEn,
        ShopCategory category,
        string? publicPhoneE164,
        IEnumerable<ShopAmenity>? amenities,
        string? shortNameAr = null,
        string? shortNameEn = null) =>
        new(
            nameAr.Trim(),
            nameEn.Trim(),
            Clean(descriptionAr),
            Clean(descriptionEn),
            category,
            publicPhoneE164,
            [.. (amenities ?? []).Distinct().Order()],
            Clean(shortNameAr),
            Clean(shortNameEn));

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public static class ShopErrors
{
    public static Error NotFound() => Error.NotFound("shop.not_found", "The shop was not found.");

    public static Error InvalidSlug() =>
        Error.Validation("validation.failed", "The slug is invalid.",
            new Dictionary<string, string[]>(StringComparer.Ordinal) { ["slug"] = ["validation.slug_invalid"] });

    public static Error SlugTaken() =>
        Error.Validation("validation.failed", "The slug is already used.",
            new Dictionary<string, string[]>(StringComparer.Ordinal) { ["slug"] = ["validation.slug_taken"] });

    public static Error InvalidTransition(ShopStatus from, ShopStatus to) =>
        Error.Conflict("shop.invalid_transition", $"A {from} shop cannot become {to}.");

    public static Error GalleryFull() =>
        Error.Validation("validation.failed", "The gallery is full.",
            new Dictionary<string, string[]>(StringComparer.Ordinal) { ["file"] = ["validation.gallery_full"] });

    public static Error OnlineBookingAlreadyPaused() => Error.Conflict("shop.online_booking_already_paused", "Online booking is already paused.");

    public static Error OnlineBookingNotPaused() => Error.Conflict("shop.online_booking_not_paused", "Online booking is not paused.");

    public static Error GalleryImageNotFound() => Error.NotFound("shop.gallery_image_not_found", "The image is not in this shop's gallery.");

    /// <summary>The admin policy does not let the shop change these fields (R-SHP-03).</summary>
    public static Error FieldsLocked(IEnumerable<ShopProfileField> fields) =>
        Error.Forbidden("shop.profile_field_locked", "The platform admin has locked these profile fields.")
            .WithDetail("fields", fields.Select(f => f.ToString()).ToArray());
}
