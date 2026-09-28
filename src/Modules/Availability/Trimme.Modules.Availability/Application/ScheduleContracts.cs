using Trimme.BuildingBlocks.Application.Scheduling;
using Trimme.Modules.Availability.Domain;
using Trimme.Modules.Availability.Domain.Engine;

namespace Trimme.Modules.Availability.Application;

/// <summary>
/// One weekly interval: minutes from the weekday's local midnight. <c>EndMinute</c> may pass 1440 when the window
/// closes after midnight (21:00–02:00 is 1260–1560).
/// </summary>
public sealed record HoursIntervalDto(DayOfWeek Day, int StartMinute, int EndMinute);

/// <summary>The shop's weekly opening hours. <c>Version</c> is null until the shop saves its hours for the first time.</summary>
public sealed record OpeningHoursResponse(IReadOnlyList<HoursIntervalDto> Intervals, uint? Version);

/// <summary>A professional's weekly hours as the shop manages them (no contact data).</summary>
public sealed record ProfessionalHoursResponse(
    Guid ProfessionalId,
    string NameAr,
    string NameEn,
    bool IsActive,
    bool FollowsShopHours,
    IReadOnlyList<HoursIntervalDto> Intervals,
    uint? Version);

/// <summary>Whether a closure or time off is in force now or still ahead (past entries are not listed).</summary>
public enum ScheduleEntryState
{
    Active,
    Scheduled,
}

public sealed record ClosureResponse(Guid Id, DateOnly StartDate, DateOnly EndDate, string? Reason, ScheduleEntryState State, uint Version);

/// <summary>A break; <c>ProfessionalId</c> null means everyone. Weekly (weekdays) or once (date).</summary>
public sealed record BreakResponse(
    Guid Id,
    Guid? ProfessionalId,
    string Label,
    IReadOnlyList<DayOfWeek> Weekdays,
    DateOnly? Date,
    int StartMinute,
    int EndMinute,
    uint Version);

/// <summary>
/// Time off. <c>StartDate</c>/<c>EndDate</c> are the shop-local days it covers (inclusive); the minutes are null for an
/// all-day entry.
/// </summary>
public sealed record TimeOffResponse(
    Guid Id,
    Guid ProfessionalId,
    TimeOffKind Kind,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    bool AllDay,
    DateOnly StartDate,
    DateOnly EndDate,
    int? StartMinute,
    int? EndMinute,
    string? Note,
    ScheduleEntryState State,
    uint Version);

/// <summary>Everything the s-hours screen shows, for the signed-in shop.</summary>
public sealed record ShopScheduleResponse(
    string TimeZone,
    DateOnly Today,
    OpeningHoursResponse OpeningHours,
    IReadOnlyList<ProfessionalHoursResponse> Professionals,
    IReadOnlyList<ClosureResponse> Closures,
    IReadOnlyList<BreakResponse> Breaks,
    IReadOnlyList<TimeOffResponse> TimeOff,
    bool OnlineBookingPaused,
    DateTimeOffset? OnlineBookingPausedAt);

/// <summary>An upcoming booking that a new break, time off or closure would overlap. Never carries a phone number.</summary>
public sealed record AffectedBookingResponse(
    Guid BookingId,
    Guid ProfessionalId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string CustomerName,
    string ItemNameAr,
    string? ItemNameEn);

/// <summary>
/// Bookings the change would overlap. Nothing is cancelled or messaged automatically (DV-S22): the shop sees them and
/// decides.
/// </summary>
public sealed record ConflictPreviewResponse(IReadOnlyList<AffectedBookingResponse> AffectedBookings);

internal static class ScheduleMapping
{
    public static HoursIntervalDto ToDto(WeeklyInterval interval) => new(interval.Day, interval.StartMinute, interval.EndMinute);

    public static IReadOnlyList<WeeklyInterval> ToWeek(IReadOnlyList<HoursIntervalDto>? intervals) =>
        [.. (intervals ?? []).Select(i => new WeeklyInterval(i.Day, i.StartMinute, i.EndMinute))];

    public static ClosureResponse ToResponse(ShopClosure closure, DateOnly today) =>
        new(closure.Id.Value, closure.StartDate, closure.EndDate, closure.Reason,
            closure.StartDate <= today ? ScheduleEntryState.Active : ScheduleEntryState.Scheduled, closure.Version);

    public static BreakResponse ToResponse(ScheduleBreak entry) =>
        new(entry.Id.Value, entry.ProfessionalId?.Value, entry.Label, entry.Weekdays, entry.Date, entry.StartMinute, entry.EndMinute, entry.Version);

    public static TimeOffResponse ToResponse(ProfessionalTimeOff entry, ShopClock clock, DateTimeOffset now)
    {
        var startLocal = clock.Local(entry.StartsAt);
        var endLocal = clock.Local(entry.EndsAt);
        var endDate = DateOnly.FromDateTime(entry.AllDay ? endLocal.AddDays(-1) : endLocal);
        return new TimeOffResponse(
            entry.Id.Value, entry.ProfessionalId.Value, entry.Kind, entry.StartsAt, entry.EndsAt, entry.AllDay,
            DateOnly.FromDateTime(startLocal), endDate,
            entry.AllDay ? null : (startLocal.Hour * 60) + startLocal.Minute,
            entry.AllDay ? null : (endLocal.Hour * 60) + endLocal.Minute,
            entry.Note, entry.StartsAt <= now ? ScheduleEntryState.Active : ScheduleEntryState.Scheduled, entry.Version);
    }

    public static AffectedBookingResponse ToResponse(BookedAppointment booking) =>
        new(booking.BookingId, booking.ProfessionalId.Value, booking.StartsAt, booking.EndsAt, booking.CustomerName, booking.ItemNameAr, booking.ItemNameEn);
}
