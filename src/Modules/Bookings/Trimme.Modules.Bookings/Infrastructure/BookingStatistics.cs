using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Reporting;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Bookings.Domain;

namespace Trimme.Modules.Bookings.Infrastructure;

/// <summary>
/// <see cref="IBookingStatistics"/> as SQL aggregates (D-101). It reads through the caller's scope: the admin overview
/// and the customers directory open the admin scope, so every shop's bookings are counted.
/// </summary>
internal sealed class BookingStatistics(TrimmeDbContext db) : IBookingStatistics
{
    public async Task<BookingTotals> TotalsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var counts = await Window(from, to)
            .GroupBy(b => b.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        int Of(BookingStatus status) => counts.GetValueOrDefault(status);
        return new BookingTotals(
            counts.Values.Sum(), Of(BookingStatus.Pending), Of(BookingStatus.Confirmed), Of(BookingStatus.Arrived), Of(BookingStatus.Completed),
            Of(BookingStatus.CancelledByCustomer) + Of(BookingStatus.CancelledByShop), Of(BookingStatus.NoShow));
    }

    public async Task<IReadOnlyList<DailyBookings>> DailyAsync(DateTimeOffset from, DateTimeOffset to, string timeZone, CancellationToken cancellationToken)
    {
        var rows = await Window(from, to)
            .GroupBy(b => new { Day = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(b.StartsAt.UtcDateTime, timeZone).Date, b.Status })
            .Select(g => new { g.Key.Day, g.Key.Status, Count = g.Count() })
            .ToListAsync(cancellationToken);
        return
        [
            .. rows.GroupBy(r => DateOnly.FromDateTime(r.Day)).OrderBy(g => g.Key).Select(g => new DailyBookings(
                g.Key,
                g.Where(r => r.Status == BookingStatus.Completed).Sum(r => r.Count),
                g.Where(r => BookingRules.IsCancelled(r.Status) || r.Status == BookingStatus.NoShow).Sum(r => r.Count),
                g.Where(r => BookingRules.IsActive(r.Status)).Sum(r => r.Count))),
        ];
    }

    public async Task<IReadOnlyList<BookedItemCount>> ItemsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var cancelled = Cancelled();
        return await Window(from, to)
            .Where(b => !cancelled.Contains(b.Status))
            .GroupBy(b => new { b.ServiceId, b.PackageId })
            .Select(g => new BookedItemCount(g.Key.ServiceId, g.Key.PackageId, g.Count()))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ShopBookingTotals>> TopShopsAsync(DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken cancellationToken)
    {
        var cancelled = Cancelled();
        var rows = await Window(from, to)
            .GroupBy(b => b.ShopId)
            .Select(g => new { ShopId = g.Key, Total = g.Count(), Cancelled = g.Count(b => cancelled.Contains(b.Status)) })
            .OrderByDescending(s => s.Total).ThenBy(s => s.ShopId)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(r => new ShopBookingTotals(r.ShopId, r.Total, r.Cancelled))];
    }

    public async Task<IReadOnlyDictionary<Guid, CustomerBookingStats>> ByCustomerAsync(
        IReadOnlyCollection<Guid> customerIds, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (customerIds.Count == 0)
        {
            return new Dictionary<Guid, CustomerBookingStats>();
        }

        var ids = customerIds.Select(id => (Guid?)id).ToArray();
        var cancelled = Cancelled();
        var active = BookingRules.Active.ToArray();
        var rows = await db.Set<Booking>().AsNoTracking()
            .Where(b => ids.Contains(b.CustomerId))
            .GroupBy(b => b.CustomerId)
            .Select(g => new
            {
                CustomerId = g.Key!.Value,
                Bookings = g.Count(),
                Upcoming = g.Count(b => active.Contains(b.Status) && b.StartsAt > now),
                Completed = g.Count(b => b.Status == BookingStatus.Completed),
                Cancelled = g.Count(b => cancelled.Contains(b.Status)),
                NoShows = g.Count(b => b.Status == BookingStatus.NoShow),
                Last = g.Where(b => b.StartsAt <= now).Max(b => (DateTimeOffset?)b.StartsAt),
                Next = g.Where(b => active.Contains(b.Status) && b.StartsAt > now).Min(b => (DateTimeOffset?)b.StartsAt),
            })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(
            r => r.CustomerId,
            r => new CustomerBookingStats(r.Bookings, r.Upcoming, r.Completed, r.Cancelled, r.NoShows, r.Last, r.Next));
    }

    private IQueryable<Booking> Window(DateTimeOffset from, DateTimeOffset to) =>
        db.Set<Booking>().AsNoTracking().Where(b => b.StartsAt >= from && b.StartsAt < to);

    private static BookingStatus[] Cancelled() => [BookingStatus.CancelledByCustomer, BookingStatus.CancelledByShop];
}
