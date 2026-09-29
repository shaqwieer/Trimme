using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.BuildingBlocks.Application.Reporting;

// Read ports for the admin overview (R-AD-01, D-101). Each module answers its own part with SQL aggregates and reads
// through the caller's data scope: the overview handler opens IAdminDataScope once, so every shop's rows are counted.

/// <summary>Booking figures over a window of start times (implemented by the Bookings module).</summary>
public interface IBookingStatistics
{
    /// <summary>Bookings starting in <c>[from, to)</c>, per status.</summary>
    Task<BookingTotals> TotalsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    /// <summary>
    /// Bookings starting in <c>[from, to)</c> per local date in <paramref name="timeZone"/>: completed, and cancelled or no-show.
    /// Only dates with bookings are returned.
    /// </summary>
    Task<IReadOnlyList<DailyBookings>> DailyAsync(DateTimeOffset from, DateTimeOffset to, string timeZone, CancellationToken cancellationToken);

    /// <summary>Bookings starting in <c>[from, to)</c> that were not cancelled, per booked service or package.</summary>
    Task<IReadOnlyList<BookedItemCount>> ItemsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    /// <summary>The shops with the most bookings starting in <c>[from, to)</c> (cancellations included), most first.</summary>
    Task<IReadOnlyList<ShopBookingTotals>> TopShopsAsync(DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken cancellationToken);

    /// <summary>Per customer: bookings, cancellations, no-shows, the last past start and the next upcoming one.</summary>
    Task<IReadOnlyDictionary<Guid, CustomerBookingStats>> ByCustomerAsync(IReadOnlyCollection<Guid> customerIds, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>Bookings per status; <c>Cancelled</c> counts both cancellation statuses.</summary>
public sealed record BookingTotals(int Total, int Pending, int Confirmed, int Arrived, int Completed, int Cancelled, int NoShow)
{
    public static readonly BookingTotals Empty = new(0, 0, 0, 0, 0, 0, 0);

    /// <summary>Everything except cancellations.</summary>
    public int Kept => Total - Cancelled;
}

public sealed record DailyBookings(DateOnly Date, int Completed, int CancelledOrNoShow, int Other);

/// <summary>Exactly one of <paramref name="ServiceId"/> and <paramref name="PackageId"/> is set.</summary>
public sealed record BookedItemCount(Guid? ServiceId, Guid? PackageId, int Count);

public sealed record ShopBookingTotals(ShopId ShopId, int Total, int Cancelled);

/// <summary>
/// One customer's bookings: all of them (cancellations included); the active ones still ahead; completed, cancelled and
/// no-show; the latest start before now and the soonest active start after it.
/// </summary>
public sealed record CustomerBookingStats(int Bookings, int Upcoming, int Completed, int Cancelled, int NoShows, DateTimeOffset? LastBookingAt, DateTimeOffset? NextBookingAt)
{
    public static readonly CustomerBookingStats Empty = new(0, 0, 0, 0, 0, null, null);
}

/// <summary>Shop counts (implemented by the Shops module).</summary>
public interface IShopStatistics
{
    Task<ShopCounts> CountsAsync(CancellationToken cancellationToken);
}

public sealed record ShopCounts(int Total, int Active, int Draft, int Suspended);

/// <summary>Professional counts (implemented by the Professionals module).</summary>
public interface IProfessionalStatistics
{
    /// <summary>Active and disabled professionals, and how many were created at or after <paramref name="addedSince"/>.</summary>
    Task<ProfessionalCounts> CountsAsync(DateTimeOffset addedSince, CancellationToken cancellationToken);
}

public sealed record ProfessionalCounts(int Active, int Disabled, int AddedSince);

/// <summary>Customer registrations (implemented by the Identity module).</summary>
public interface ICustomerStatistics
{
    /// <summary>Customer accounts created in <c>[from, to)</c>.</summary>
    Task<int> RegisteredAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

/// <summary>The platform category of shop services (implemented by the Services module; categories are platform data).</summary>
public interface IServiceCategoryLookup
{
    /// <summary>Each given service's category; services without a category are absent.</summary>
    Task<IReadOnlyDictionary<Guid, ServiceCategoryRef>> CategoriesOfAsync(IReadOnlyCollection<Guid> serviceIds, CancellationToken cancellationToken);
}

public sealed record ServiceCategoryRef(Guid Id, string NameAr, string NameEn);
