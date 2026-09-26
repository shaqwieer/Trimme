using System.Text.RegularExpressions;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.Modules.Shops.Domain;

/// <summary>
/// A salon or barber shop: the tenant root (spec §7). Created by a platform admin as Draft; profile, location and
/// gallery arrive in Phase 06, booking settings in Phases 08–10.
/// </summary>
public sealed partial class Shop : AggregateRoot<ShopId>, ITenantRoot, IConcurrencyVersioned
{
    public const string DefaultTimeZone = "Asia/Riyadh";
    public const int MaxNameLength = 120;

    private Shop(ShopId id, string slug, string nameAr, string nameEn, string timeZone, DateTimeOffset now)
        : base(id)
    {
        Slug = slug;
        NameAr = nameAr;
        NameEn = nameEn;
        TimeZone = timeZone;
        Status = ShopStatus.Draft;
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

    public ShopStatus Status { get; private set; }

    /// <summary>IANA time zone used for opening hours and availability (D-030).</summary>
    public string TimeZone { get; private set; }

    /// <summary>D-006: when true, online bookings start as Pending and the shop confirms them. Default off.</summary>
    public bool RequireManualConfirmation { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public static Result<Shop> Create(ShopId id, string slug, string nameAr, string nameEn, string? timeZone, DateTimeOffset now)
    {
        var normalizedSlug = slug.Trim().ToLowerInvariant();
        if (!SlugPattern().IsMatch(normalizedSlug))
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

    public static bool IsValidSlug(string slug) => SlugPattern().IsMatch(slug);

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{1,58}[a-z0-9])$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
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
}
