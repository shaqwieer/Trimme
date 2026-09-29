using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Discovery;
using Trimme.BuildingBlocks.Application.Platform;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Availability.Domain;
using Trimme.Modules.Availability.Domain.Engine;

namespace Trimme.Modules.Availability.Application;

/// <summary>
/// <see cref="IShopOpeningReader"/> (D-091): opening status of many shops with two queries, computed by the same engine
/// rules as the slots. Reads through the caller's data scope (the public multi-shop scope for discovery).
/// </summary>
internal sealed class ShopOpeningReader(TrimmeDbContext db, ScheduleLoader loader) : IShopOpeningReader
{
    public async Task<IReadOnlyDictionary<ShopId, ShopOpenStatus>> GetStatusesAsync(
        IReadOnlyDictionary<ShopId, string> timeZones, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(timeZones);
        if (timeZones.Count == 0)
        {
            return new Dictionary<ShopId, ShopOpenStatus>();
        }

        var ids = timeZones.Keys.ToArray();

        // Wide enough for every zone: yesterday (a window past midnight) to the end of the look-ahead.
        var utcToday = DateOnly.FromDateTime(now.UtcDateTime);
        var from = utcToday.AddDays(-2);
        var to = utcToday.AddDays(AvailabilityEngine.OpenStatusLookaheadDays + 2);
        var hours = await db.Set<ShopOpeningHours>().AsNoTracking()
            .Where(h => ids.Contains(h.ShopId)).ToListAsync(cancellationToken);
        var closures = await db.Set<ShopClosure>().AsNoTracking()
            .Where(c => ids.Contains(c.ShopId) && c.EndDate >= from && c.StartDate <= to).ToListAsync(cancellationToken);

        var weeks = hours.ToDictionary(h => h.ShopId, h => h.Week());
        var result = new Dictionary<ShopId, ShopOpenStatus>();
        foreach (var (shopId, timeZone) in timeZones)
        {
            var calendar = new ShopCalendar(
                ScheduleLoader.Zone(timeZone),
                weeks.GetValueOrDefault(shopId) ?? [],
                [.. closures.Where(c => c.ShopId == shopId).Select(c => new DateRange(c.StartDate, c.EndDate))]);
            var (isOpen, closesAt, nextOpensAt) = AvailabilityEngine.OpenStatus(calendar, now);
            result[shopId] = new ShopOpenStatus(isOpen, closesAt, nextOpensAt);
        }

        return result;
    }

    public async Task<IReadOnlyList<OpeningInterval>> GetWeekAsync(ShopId shopId, CancellationToken cancellationToken) =>
        [.. (await loader.OpeningHoursAsync(shopId, cancellationToken)).Select(i => new OpeningInterval(i.Day, i.StartMinute, i.EndMinute))];
}

/// <summary>
/// <see cref="ISlotProbe"/> (D-091): the public slot rules for one item, used by discovery for "earliest slot" and
/// "bookable today". Reads through the caller's data scope.
/// </summary>
internal sealed class SlotProbe(ScheduleLoader loader, IPlatformSettings settings, TimeProvider clock) : ISlotProbe
{
    public async Task<IReadOnlyList<ProbedSlot>> ProbeAsync(
        ShopId shopId,
        string timeZone,
        int durationMinutes,
        IReadOnlyList<ProfessionalId> professionalIds,
        DateOnly from,
        DateOnly to,
        int maxSlots,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(professionalIds);
        if (professionalIds.Count == 0 || maxSlots <= 0 || to < from)
        {
            return [];
        }

        var zone = ScheduleLoader.Zone(timeZone);
        var policy = ScheduleLoader.Policy(await settings.GetAsync(cancellationToken));
        var (shop, calendars) = await loader.CalendarsAsync(shopId, zone, professionalIds, from, to, cancellationToken);
        var slots = AvailabilityEngine.FindSlots(shop, calendars, new AvailabilityQuery(from, to, durationMinutes, clock.GetUtcNow(), policy));
        return
        [
            .. slots.Take(maxSlots).Select(s => new ProbedSlot(
                s.StartsAt, s.Date, s.LocalTime.ToString("HH:mm", CultureInfo.InvariantCulture), s.Professionals)),
        ];
    }

    public DateOnly Today(string timeZone, DateTimeOffset now) => new ShopClock(ScheduleLoader.Zone(timeZone)).Date(now);
}
