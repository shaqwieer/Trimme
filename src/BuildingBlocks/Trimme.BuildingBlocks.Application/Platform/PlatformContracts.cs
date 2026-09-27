using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.BuildingBlocks.Application.Platform;

/// <summary>What happens to a shop whose platform subscription is not in force (D-014).</summary>
public enum SubscriptionEnforcement
{
    /// <summary>Nothing: the subscription status is informational only.</summary>
    None,

    /// <summary>
    /// The shop is hidden from discovery and takes no new online bookings. Existing and future bookings are never
    /// changed or deleted, and walk-ins recorded by the shop are still allowed.
    /// </summary>
    HideAndBlockNewOnlineBookings,
}

/// <summary>
/// The platform settings every module reads (spec §6, §15, D-013, D-014, D-076). One row, edited by admins with
/// <c>Admin.Settings.Edit</c>; the defaults are written by the <c>migrate</c> command when the row is missing.
/// </summary>
public sealed record PlatformSettingsSnapshot(
    int MinLeadTimeMinutes,
    int BookingHorizonDays,
    int SlotStepMinutes,
    int CancellationCutoffMinutes,
    int ReviewWindowDays,
    int ReminderOffsetMinutes,
    int ExpiringSoonThresholdDays,
    SubscriptionEnforcement ExpiredSubscriptionEnforcement,
    bool HidePausedShopsFromDiscovery,
    string DefaultLocale,
    string Currency,
    string TimeZone,
    string CountryCode,
    double MapDefaultLatitude,
    double MapDefaultLongitude,
    int MapDefaultZoom)
{
    /// <summary>The platform's calendar day at <paramref name="instant"/> (subscriptions use this calendar, D-077).</summary>
    public DateOnly LocalDate(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(TimeZone)).DateTime);
}

/// <summary>Read access to the platform settings (implemented by the Administration module).</summary>
public interface IPlatformSettings
{
    Task<PlatformSettingsSnapshot> GetAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Whether a shop may take new online bookings and appear in discovery right now (implemented by the Subscriptions
/// module; used by discovery in Phase 11 and booking creation in Phase 10). It never affects existing bookings or
/// walk-ins (D-014).
/// </summary>
public interface IShopBookability
{
    Task<ShopBookability> GetAsync(ShopId shopId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<ShopId, ShopBookability>> GetManyAsync(IReadOnlyCollection<ShopId> shopIds, CancellationToken cancellationToken);
}

/// <param name="AcceptsOnlineBookings">New online bookings are allowed.</param>
/// <param name="VisibleInDiscovery">The shop may be listed in search and on the map.</param>
/// <param name="BlockedReason">
/// Why it is blocked, as a stable code: <c>shop.not_active</c>, <c>subscription.none</c>, <c>subscription.expired</c> or
/// <c>subscription.suspended</c>; <see langword="null"/> when nothing blocks it.
/// </param>
public sealed record ShopBookability(bool AcceptsOnlineBookings, bool VisibleInDiscovery, string? BlockedReason);
