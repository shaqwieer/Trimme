using Trimme.BuildingBlocks.Application.Platform;
using Trimme.BuildingBlocks.Domain.Primitives;

namespace Trimme.Modules.Administration.Domain;

public readonly record struct PlatformSettingsId(Guid Value) : IEntityId<PlatformSettingsId>
{
    /// <summary>The one row.</summary>
    public static readonly PlatformSettingsId Singleton = new(Guid.Parse("0199a0de-0000-7000-8000-00000000c0de"));

    public static PlatformSettingsId From(Guid value) => new(value);
}

/// <summary>The admin-editable part of the settings (locale, currency, time zone and country are fixed in v1, D-076).</summary>
public sealed record EditablePlatformSettings(
    int MinLeadTimeMinutes,
    int BookingHorizonDays,
    int SlotStepMinutes,
    int CancellationCutoffMinutes,
    int ReviewWindowDays,
    int ReminderOffsetMinutes,
    int ExpiringSoonThresholdDays,
    SubscriptionEnforcement ExpiredSubscriptionEnforcement,
    bool HidePausedShopsFromDiscovery,
    double MapDefaultLatitude,
    double MapDefaultLongitude,
    int MapDefaultZoom);

/// <summary>
/// The platform settings row (D-076): typed columns, one row, optimistic concurrency, every change audited with the
/// names of the fields that changed. The <c>migrate</c> command inserts the defaults when the row is missing and never
/// overwrites an admin's edit.
/// </summary>
public sealed class PlatformSettings : Entity<PlatformSettingsId>, IConcurrencyVersioned
{
    public static readonly IReadOnlyList<int> SlotSteps = [5, 10, 15, 20, 30, 60];

    private PlatformSettings(PlatformSettingsId id, DateTimeOffset now)
        : base(id)
    {
        DefaultLocale = "ar";
        Currency = "SAR";
        TimeZone = "Asia/Riyadh";
        CountryCode = "SA";
        UpdatedAt = now;
    }

    private PlatformSettings()
    {
        DefaultLocale = Currency = TimeZone = CountryCode = string.Empty;
    }

    public int MinLeadTimeMinutes { get; private set; }

    public int BookingHorizonDays { get; private set; }

    public int SlotStepMinutes { get; private set; }

    public int CancellationCutoffMinutes { get; private set; }

    public int ReviewWindowDays { get; private set; }

    public int ReminderOffsetMinutes { get; private set; }

    public int ExpiringSoonThresholdDays { get; private set; }

    public SubscriptionEnforcement ExpiredSubscriptionEnforcement { get; private set; }

    public bool HidePausedShopsFromDiscovery { get; private set; }

    public string DefaultLocale { get; private set; }

    public string Currency { get; private set; }

    public string TimeZone { get; private set; }

    public string CountryCode { get; private set; }

    public double MapDefaultLatitude { get; private set; }

    public double MapDefaultLongitude { get; private set; }

    public int MapDefaultZoom { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public uint Version { get; private set; }

    /// <summary>The spec defaults (§6, §12, §15, §16; D-013, D-014, D-015, D-017): Riyadh map, Arabic, SAR.</summary>
    public static EditablePlatformSettings Defaults { get; } = new(
        MinLeadTimeMinutes: 60,
        BookingHorizonDays: 30,
        SlotStepMinutes: 5,
        CancellationCutoffMinutes: 120,
        ReviewWindowDays: 7,
        ReminderOffsetMinutes: 30,
        ExpiringSoonThresholdDays: 14,
        ExpiredSubscriptionEnforcement: SubscriptionEnforcement.HideAndBlockNewOnlineBookings,
        HidePausedShopsFromDiscovery: true,
        MapDefaultLatitude: 24.7136,
        MapDefaultLongitude: 46.6753,
        MapDefaultZoom: 11);

    public static PlatformSettings CreateDefault(DateTimeOffset now)
    {
        var settings = new PlatformSettings(PlatformSettingsId.Singleton, now);
        settings.Apply(Defaults);
        return settings;
    }

    public EditablePlatformSettings Editable => new(
        MinLeadTimeMinutes, BookingHorizonDays, SlotStepMinutes, CancellationCutoffMinutes, ReviewWindowDays, ReminderOffsetMinutes,
        ExpiringSoonThresholdDays, ExpiredSubscriptionEnforcement, HidePausedShopsFromDiscovery, MapDefaultLatitude, MapDefaultLongitude, MapDefaultZoom);

    public PlatformSettingsSnapshot ToSnapshot() => new(
        MinLeadTimeMinutes, BookingHorizonDays, SlotStepMinutes, CancellationCutoffMinutes, ReviewWindowDays, ReminderOffsetMinutes,
        ExpiringSoonThresholdDays, ExpiredSubscriptionEnforcement, HidePausedShopsFromDiscovery, DefaultLocale, Currency, TimeZone, CountryCode,
        MapDefaultLatitude, MapDefaultLongitude, MapDefaultZoom);

    /// <summary>Applies an edit and returns the camelCase names of the fields that changed (for the audit entry).</summary>
    public IReadOnlyList<string> Update(EditablePlatformSettings values, Guid? by, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(values);
        var changed = Changes(Editable, values);
        Apply(values);
        UpdatedAt = now;
        UpdatedBy = by;
        return changed;
    }

    /// <summary>Field names (camelCase, as in the API) whose values differ.</summary>
    public static IReadOnlyList<string> Changes(EditablePlatformSettings before, EditablePlatformSettings after) =>
        typeof(EditablePlatformSettings).GetProperties()
            .Where(p => !Equals(p.GetValue(before), p.GetValue(after)))
            .Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..])
            .ToArray();

    private void Apply(EditablePlatformSettings values)
    {
        MinLeadTimeMinutes = values.MinLeadTimeMinutes;
        BookingHorizonDays = values.BookingHorizonDays;
        SlotStepMinutes = values.SlotStepMinutes;
        CancellationCutoffMinutes = values.CancellationCutoffMinutes;
        ReviewWindowDays = values.ReviewWindowDays;
        ReminderOffsetMinutes = values.ReminderOffsetMinutes;
        ExpiringSoonThresholdDays = values.ExpiringSoonThresholdDays;
        ExpiredSubscriptionEnforcement = values.ExpiredSubscriptionEnforcement;
        HidePausedShopsFromDiscovery = values.HidePausedShopsFromDiscovery;
        MapDefaultLatitude = values.MapDefaultLatitude;
        MapDefaultLongitude = values.MapDefaultLongitude;
        MapDefaultZoom = values.MapDefaultZoom;
    }
}
