using Trimme.BuildingBlocks.Application.Scheduling;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.Modules.Availability.Domain.Engine;

namespace Trimme.Modules.Availability.Application;

/// <summary><see cref="IShopDayPlanReader"/>: the engine's business-day rules over the shop's schedule (D-100).</summary>
internal sealed class ShopDayPlanReader(ScheduleLoader loader) : IShopDayPlanReader
{
    public async Task<IReadOnlyList<ShopDayPlan>> GetAsync(
        ShopId shopId, string timeZone, IReadOnlyList<ProfessionalId> professionalIds, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var (shop, calendars) = await loader.CalendarsAsync(shopId, ScheduleLoader.Zone(timeZone), professionalIds, from, to, cancellationToken, includeBookings: false);
        var plans = new List<ShopDayPlan>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var professionals = calendars.Select(calendar =>
            {
                var plan = AvailabilityEngine.DayOf(shop, calendar, day);
                var available = plan.Working.Subtract(plan.Breaks).Subtract(plan.Blocked);
                return new ProfessionalDayPlan(
                    calendar.Id, Windows(plan.Working), Windows(plan.Breaks), Windows(plan.Blocked),
                    (int)Math.Round(available.Ranges.Sum(r => (r.End - r.Start).TotalMinutes)));
            }).ToList();
            plans.Add(new ShopDayPlan(day, shop.Closures.Any(c => c.Contains(day)), Windows(AvailabilityEngine.OpenWindows(shop, day)), professionals));
        }

        return plans;
    }

    private static IReadOnlyList<TimeWindow> Windows(InstantSet set) => [.. set.Ranges.Select(r => new TimeWindow(r.Start, r.End))];
}
