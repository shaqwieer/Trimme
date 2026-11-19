using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Bookings.Domain;

namespace Trimme.Modules.Bookings.Application;

/// <summary>
/// The shop gives an online customer a new time at most once a day (D-155, the client's «المحل ما يقدر يغيّر موعد نفس
/// الزبون أكثر من مرة وحدة في اليوم»): once its users changed the time of any of this customer's bookings at this shop
/// since the shop's local midnight, another change waits until tomorrow. The alternatives then are another barber at the
/// same time, or cancelling with a reason. Walk-in and phone bookings (the shop's own), changing the barber and the
/// platform admin are not limited. Counted from the booking history, so it holds whoever pressed it.
/// </summary>
internal sealed class ShopChangeLimit(TrimmeDbContext db, IShopDirectory shops, TimeProvider clock)
{
    public static Error Reached() =>
        Error.Conflict(
            "booking.changed_today",
            "The shop already gave this customer a new time today: give the booking to a colleague, or cancel it with a reason.");

    /// <summary>Whether the shop may not change this booking's time again today.</summary>
    public async Task<bool> ReachedAsync(Booking booking, CancellationToken cancellationToken)
    {
        if (booking.Channel != BookingChannel.Online || booking.CustomerId is not { } customerId)
        {
            return false;
        }

        var shop = await shops.FindAsync(booking.ShopId, cancellationToken);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(shop?.TimeZone ?? "Asia/Riyadh");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
        var since = BusinessDays.StartOf(today, zone);
        var shopId = booking.ShopId;
        return await db.Set<Booking>().AsNoTracking()
            .Where(b => b.ShopId == shopId && b.CustomerId == customerId)
            .SelectMany(b => b.History)
            .AnyAsync(h => h.Kind == BookingEventKind.Rescheduled && h.ActorType == ActorType.ShopUser && h.OccurredAt >= since, cancellationToken);
    }
}
