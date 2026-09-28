using Trimme.BuildingBlocks.Application.Platform;
using Trimme.BuildingBlocks.Application.Scheduling;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.Modules.Availability.Domain.Engine;

namespace Trimme.Modules.Availability.Application;

/// <summary>
/// <see cref="IAvailabilityChecker"/> (D-088): the engine's rules for one exact time, over data read through the caller's
/// scope. Online uses <see cref="AvailabilityEngine.IsBookable"/> (lead time, horizon, grid); walk-ins use
/// <see cref="AvailabilityEngine.IsFree"/> (collisions only).
/// </summary>
internal sealed class AvailabilityChecker(IShopDirectory shops, IPlatformSettings settings, ScheduleLoader loader, TimeProvider clock) : IAvailabilityChecker
{
    public async Task<IReadOnlyList<ProfessionalId>> FreeProfessionalsAsync(
        ShopId shopId,
        IReadOnlyList<ProfessionalId> candidates,
        DateTimeOffset start,
        int durationMinutes,
        AvailabilityCheckMode mode,
        Guid? ignoreBookingId,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0 || await shops.FindAsync(shopId, cancellationToken) is not { } shop)
        {
            return [];
        }

        var zone = ScheduleLoader.Zone(shop.TimeZone);
        var shopClock = new ShopClock(zone);
        var from = shopClock.Date(start);
        var to = shopClock.Date(start.AddMinutes(durationMinutes));
        var (calendar, professionals) = await loader.CalendarsAsync(shopId, zone, candidates, from, to, cancellationToken, ignoreBookingId);
        var policy = ScheduleLoader.Policy(await settings.GetAsync(cancellationToken));
        var now = clock.GetUtcNow();
        return
        [
            .. professionals.Where(p => mode == AvailabilityCheckMode.Online
                    ? AvailabilityEngine.IsBookable(calendar, p, start, durationMinutes, now, policy)
                    : AvailabilityEngine.IsFree(calendar, p, start, durationMinutes))
                .Select(p => p.Id),
        ];
    }

    public async Task<IReadOnlySet<Guid>> OutsideScheduleAsync(ShopId shopId, IReadOnlyCollection<ScheduledTime> bookings, CancellationToken cancellationToken)
    {
        if (bookings.Count == 0 || await shops.FindAsync(shopId, cancellationToken) is not { } shop)
        {
            return new HashSet<Guid>();
        }

        var zone = ScheduleLoader.Zone(shop.TimeZone);
        var shopClock = new ShopClock(zone);
        var from = shopClock.Date(bookings.Min(b => b.StartsAt));
        var to = shopClock.Date(bookings.Max(b => b.EndsAt));
        var ids = bookings.Select(b => b.ProfessionalId).Distinct().ToList();
        var (calendar, professionals) = await loader.CalendarsAsync(shopId, zone, ids, from, to, cancellationToken, includeBookings: false);
        var free = professionals.ToDictionary(p => p.Id, p => AvailabilityEngine.FreeTime(calendar, p, from, to));
        return bookings
            .Where(b => !free[b.ProfessionalId].Contains(new InstantRange(b.StartsAt, b.EndsAt)))
            .Select(b => b.BookingId)
            .ToHashSet();
    }
}
