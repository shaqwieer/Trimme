using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Platform;
using Trimme.BuildingBlocks.Application.Scheduling;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Availability.Domain;
using Trimme.Modules.Availability.Domain.Engine;

namespace Trimme.Modules.Availability.Application;

/// <summary>
/// Loads one shop's schedule into the engine's inputs. It reads through the caller's data scope (the shop's own
/// tenant, or the public scope opened for that shop) and also filters by the shop id.
/// </summary>
internal sealed class ScheduleLoader(TrimmeDbContext db, IBookedTimeReader bookings)
{
    public static BookingPolicy Policy(PlatformSettingsSnapshot settings) =>
        new(settings.MinLeadTimeMinutes, settings.BookingHorizonDays, settings.SlotStepMinutes);

    public static TimeZoneInfo Zone(string timeZone) => TimeZoneInfo.FindSystemTimeZoneById(timeZone);

    public async Task<IReadOnlyList<WeeklyInterval>> OpeningHoursAsync(ShopId shopId, CancellationToken cancellationToken) =>
        (await db.Set<ShopOpeningHours>().AsNoTracking().SingleOrDefaultAsync(h => h.ShopId == shopId, cancellationToken))?.Week() ?? [];

    /// <summary>
    /// The shop calendar and one calendar per professional for local dates <paramref name="from"/>…<paramref name="to"/>,
    /// with a day of margin on each side for windows that pass midnight. <c>ignoreBookingId</c> is a booking whose own
    /// time does not count (the one being rescheduled); <c>includeBookings: false</c> leaves bookings out, to test a
    /// booking against the schedule alone.
    /// </summary>
    public async Task<(ShopCalendar Shop, IReadOnlyList<ProfessionalCalendar> Professionals)> CalendarsAsync(
        ShopId shopId,
        TimeZoneInfo zone,
        IReadOnlyList<ProfessionalId> professionalIds,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken,
        Guid? ignoreBookingId = null,
        bool includeBookings = true) =>
        (await CalendarsAsync([new CalendarRequest(shopId, zone, professionalIds, from, to)], cancellationToken, ignoreBookingId, includeBookings))[0];

    /// <summary>
    /// The calendars of several requests (shops, professionals and dates), in request order, read with a fixed number of
    /// queries whatever the number of requests: discovery probes many shops at once (D-091, Phase 17 N+1 review).
    /// </summary>
    public async Task<IReadOnlyList<(ShopCalendar Shop, IReadOnlyList<ProfessionalCalendar> Professionals)>> CalendarsAsync(
        IReadOnlyList<CalendarRequest> requests,
        CancellationToken cancellationToken,
        Guid? ignoreBookingId = null,
        bool includeBookings = true)
    {
        if (requests.Count == 0)
        {
            return [];
        }

        var windows = requests.Select(r =>
        {
            var clock = new ShopClock(r.Zone);
            return (Start: clock.StartOfDay(r.From.AddDays(-1)), End: clock.StartOfDay(r.To.AddDays(2)));
        }).ToList();
        var windowStart = windows.Min(w => w.Start);
        var windowEnd = windows.Max(w => w.End);
        var firstDay = requests.Min(r => r.From).AddDays(-1);
        var lastDay = requests.Max(r => r.To);
        var shopIds = requests.Select(r => r.ShopId).Distinct().ToArray();
        var ids = requests.SelectMany(r => r.ProfessionalIds).Distinct().ToArray();

        var opening = await db.Set<ShopOpeningHours>().AsNoTracking()
            .Where(h => shopIds.Contains(h.ShopId)).ToDictionaryAsync(h => h.ShopId, cancellationToken);
        var closures = await db.Set<ShopClosure>().AsNoTracking()
            .Where(c => shopIds.Contains(c.ShopId) && c.EndDate >= firstDay && c.StartDate <= lastDay)
            .Select(c => new { c.ShopId, c.StartDate, c.EndDate }).ToListAsync(cancellationToken);
        var hours = await db.Set<ProfessionalWorkingHours>().AsNoTracking()
            .Where(h => shopIds.Contains(h.ShopId) && ids.Contains(h.ProfessionalId)).ToDictionaryAsync(h => h.ProfessionalId, cancellationToken);
        var breaks = await db.Set<ScheduleBreak>().AsNoTracking()
            .Where(b => shopIds.Contains(b.ShopId) && (b.ProfessionalId == null || ids.Contains(b.ProfessionalId.Value)))
            .ToListAsync(cancellationToken);
        var timeOff = await db.Set<ProfessionalTimeOff>().AsNoTracking()
            .Where(t => shopIds.Contains(t.ShopId) && ids.Contains(t.ProfessionalId) && t.EndsAt > windowStart && t.StartsAt < windowEnd)
            .ToListAsync(cancellationToken);
        var busy = ids.Length == 0 || !includeBookings
            ? []
            : (await bookings.GetBusyAsync(shopIds, ids, windowStart, windowEnd, cancellationToken)).Where(b => b.BookingId != ignoreBookingId).ToList();

        return
        [
            .. requests.Select((request, index) =>
            {
                var (start, end) = windows[index];
                var shopBreaks = breaks.Where(b => b.ShopId == request.ShopId).ToList();
                var calendars = request.ProfessionalIds.Select(id => new ProfessionalCalendar(
                    id,
                    hours.GetValueOrDefault(id)?.Week(),
                    [.. shopBreaks.Where(b => b.AppliesTo(id)).Select(b => b.ToRule())],
                    [
                        .. timeOff.Where(t => t.ProfessionalId == id && t.EndsAt > start && t.StartsAt < end).Select(t => t.Span()),
                        .. busy.Where(b => b.ProfessionalId == id && b.StartsAt < end && b.EndsAt > start)
                            .Select(b => new InstantRange(b.StartsAt, b.EndsAt)),
                    ])).ToList();
                var shopClosures = closures
                    .Where(c => c.ShopId == request.ShopId && c.EndDate >= request.From.AddDays(-1) && c.StartDate <= request.To)
                    .Select(c => new DateRange(c.StartDate, c.EndDate)).ToList();
                var week = opening.GetValueOrDefault(request.ShopId)?.Week() ?? [];
                return (new ShopCalendar(request.Zone, week, shopClosures), (IReadOnlyList<ProfessionalCalendar>)calendars);
            }),
        ];
    }
}

/// <summary>One shop's calendars to load: these professionals over local dates <c>From</c>…<c>To</c>.</summary>
internal sealed record CalendarRequest(
    ShopId ShopId, TimeZoneInfo Zone, IReadOnlyList<ProfessionalId> ProfessionalIds, DateOnly From, DateOnly To);
