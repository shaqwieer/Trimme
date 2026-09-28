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
    /// with a day of margin on each side for windows that pass midnight.
    /// </summary>
    public async Task<(ShopCalendar Shop, IReadOnlyList<ProfessionalCalendar> Professionals)> CalendarsAsync(
        ShopId shopId, TimeZoneInfo zone, IReadOnlyList<ProfessionalId> professionalIds, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var clock = new ShopClock(zone);
        var windowStart = clock.StartOfDay(from.AddDays(-1));
        var windowEnd = clock.StartOfDay(to.AddDays(2));

        var opening = await OpeningHoursAsync(shopId, cancellationToken);
        var closures = await db.Set<ShopClosure>().AsNoTracking()
            .Where(c => c.ShopId == shopId && c.EndDate >= from.AddDays(-1) && c.StartDate <= to)
            .Select(c => new DateRange(c.StartDate, c.EndDate)).ToListAsync(cancellationToken);
        var ids = professionalIds.ToArray();
        var hours = await db.Set<ProfessionalWorkingHours>().AsNoTracking()
            .Where(h => h.ShopId == shopId && ids.Contains(h.ProfessionalId)).ToDictionaryAsync(h => h.ProfessionalId, cancellationToken);
        var breaks = await db.Set<ScheduleBreak>().AsNoTracking()
            .Where(b => b.ShopId == shopId && (b.ProfessionalId == null || ids.Contains(b.ProfessionalId.Value)))
            .ToListAsync(cancellationToken);
        var timeOff = await db.Set<ProfessionalTimeOff>().AsNoTracking()
            .Where(t => t.ShopId == shopId && ids.Contains(t.ProfessionalId) && t.EndsAt > windowStart && t.StartsAt < windowEnd)
            .ToListAsync(cancellationToken);
        var busy = ids.Length == 0 ? [] : await bookings.GetBusyAsync(shopId, ids, windowStart, windowEnd, cancellationToken);

        var calendars = ids.Select(id => new ProfessionalCalendar(
            id,
            hours.GetValueOrDefault(id)?.Week(),
            [.. breaks.Where(b => b.AppliesTo(id)).Select(b => b.ToRule())],
            [
                .. timeOff.Where(t => t.ProfessionalId == id).Select(t => t.Span()),
                .. busy.Where(b => b.ProfessionalId == id).Select(b => new InstantRange(b.StartsAt, b.EndsAt)),
            ])).ToList();
        return (new ShopCalendar(zone, opening, closures), calendars);
    }
}
