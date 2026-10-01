using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.BuildingBlocks.Application.Scheduling;

/// <summary>
/// What a customer can book at one shop, for the availability engine (implemented by the Services module, D-082).
/// It reads through the caller's data scope: a public use case opens <c>IPublicDataScope</c> for the shop first.
/// </summary>
public interface IBookableOfferCatalog
{
    /// <summary>The shop's service or package (exactly one id is set), or <see langword="null"/> when it is not published.</summary>
    Task<BookableOffer?> FindAsync(ShopId shopId, Guid? serviceId, Guid? packageId, CancellationToken cancellationToken);
}

/// <param name="Id">The service or package id.</param>
/// <param name="IsPackage">A package is booked as one contiguous appointment with one professional (D-072).</param>
/// <param name="DurationMinutes">The shop's own duration (spec §10).</param>
/// <param name="OnlineBookable">Customers may book it online (a service's own rule; a package when every item allows it).</param>
/// <param name="EligibleProfessionalIds">
/// Professionals assigned to the service, or for a package those assigned to every item service. Their status is not
/// checked here; the caller keeps only the active ones.
/// </param>
/// <param name="NameAr">The shop's Arabic name for it, snapshotted onto bookings (R-BKG-01).</param>
/// <param name="NameEn">The optional English name.</param>
/// <param name="Price">The shop's own price.</param>
/// <param name="Currency">ISO currency of the price.</param>
/// <param name="Items">For a package, its services in order; empty for a service.</param>
public sealed record BookableOffer(
    Guid Id,
    bool IsPackage,
    int DurationMinutes,
    bool OnlineBookable,
    IReadOnlyList<ProfessionalId> EligibleProfessionalIds,
    string NameAr,
    string? NameEn,
    decimal Price,
    string Currency,
    IReadOnlyList<BookableOfferItem> Items);

/// <summary>One service of a package, for the booking snapshot and reporting.</summary>
public sealed record BookableOfferItem(Guid ServiceId, string NameAr, string? NameEn);

/// <summary>
/// Existing, non-cancelled appointments, for availability and the schedule conflict preview. The Bookings module
/// implements it in Phase 10; until then the Availability module registers an empty one. The shapes carry no customer
/// contact data by construction (spec §7).
/// </summary>
public interface IBookedTimeReader
{
    /// <summary>Time taken by bookings of these professionals that overlaps [<paramref name="from"/>, <paramref name="to"/>).</summary>
    Task<IReadOnlyList<BusyTime>> GetBusyAsync(
        ShopId shopId, IReadOnlyCollection<ProfessionalId> professionalIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    /// <summary>The same for several shops in one query (discovery's batched probe, Phase 17).</summary>
    Task<IReadOnlyList<BusyTime>> GetBusyAsync(
        IReadOnlyCollection<ShopId> shopIds, IReadOnlyCollection<ProfessionalId> professionalIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    /// <summary>
    /// Upcoming bookings of the shop overlapping [<paramref name="from"/>, <paramref name="to"/>), all professionals when
    /// <paramref name="professionalId"/> is <see langword="null"/>. Shown to the shop before it adds time off, a break or a closure.
    /// </summary>
    Task<IReadOnlyList<BookedAppointment>> GetAppointmentsAsync(
        ShopId shopId, ProfessionalId? professionalId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record BusyTime(Guid BookingId, ProfessionalId ProfessionalId, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

/// <summary>How strictly a time is checked (D-088).</summary>
public enum AvailabilityCheckMode
{
    /// <summary>Online create and reschedule: the full policy (lead time, horizon, the slot grid) plus every collision.</summary>
    Online,

    /// <summary>Walk-ins: the same collisions (hours, breaks, time off, closures, bookings) at any minute, starting now if needed.</summary>
    WalkIn,
}

/// <summary>A booked time, for the schedule-conflict flags (DV-S22).</summary>
public sealed record ScheduledTime(Guid BookingId, ProfessionalId ProfessionalId, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

/// <summary>
/// The availability rules for one exact time (implemented by the Availability module, D-088). It reads through the
/// caller's data scope: a customer's booking opens the public scope for the shop so every booking of the professional is
/// seen; a shop user reads its own shop. Bookings call it inside their transaction (the recheck, R-BKG-03).
/// </summary>
public interface IAvailabilityChecker
{
    /// <summary>
    /// The candidates who can take <c>[start, start + duration)</c> in this mode, in the given order.
    /// <paramref name="ignoreBookingId"/> is the booking being rescheduled, whose own time does not block it.
    /// </summary>
    Task<IReadOnlyList<ProfessionalId>> FreeProfessionalsAsync(
        ShopId shopId,
        IReadOnlyList<ProfessionalId> candidates,
        DateTimeOffset start,
        int durationMinutes,
        AvailabilityCheckMode mode,
        Guid? ignoreBookingId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The bookings that no longer fit their professional's schedule (hours, breaks, time off, closures), whatever the
    /// other bookings: the shop sees them flagged, nothing is cancelled (DV-S22).
    /// </summary>
    Task<IReadOnlySet<Guid>> OutsideScheduleAsync(ShopId shopId, IReadOnlyCollection<ScheduledTime> bookings, CancellationToken cancellationToken);
}

/// <summary>A booking as the shop may see it: the customer's name and the booked item, never a phone number.</summary>
public sealed record BookedAppointment(
    Guid BookingId,
    ProfessionalId ProfessionalId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string CustomerName,
    string ItemNameAr,
    string? ItemNameEn);

/// <summary>
/// The working plan of a shop's business days (implemented by the Availability module, D-100): opening windows and, per
/// professional, working time, breaks and time off. A business day owns every window that opens on it, including its
/// part after midnight. Reads through the caller's data scope (the shop's own tenant).
/// </summary>
public interface IShopDayPlanReader
{
    Task<IReadOnlyList<ShopDayPlan>> GetAsync(
        ShopId shopId, string timeZone, IReadOnlyList<ProfessionalId> professionalIds, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

public sealed record TimeWindow(DateTimeOffset Start, DateTimeOffset End)
{
    public int Minutes => (int)Math.Round((End - Start).TotalMinutes);
}

/// <param name="Date">The business day.</param>
/// <param name="Closed">A closure covers it (the shop's hours do not apply).</param>
/// <param name="Open">The shop's opening windows (empty when closed or not open that weekday).</param>
/// <param name="Professionals">One plan per professional asked for.</param>
public sealed record ShopDayPlan(DateOnly Date, bool Closed, IReadOnlyList<TimeWindow> Open, IReadOnlyList<ProfessionalDayPlan> Professionals)
{
    /// <summary>Whether an instant falls in this business day's opening windows.</summary>
    public bool Contains(DateTimeOffset instant) => Open.Any(w => w.Start <= instant && instant < w.End);
}

/// <param name="ProfessionalId">The professional.</param>
/// <param name="Working">When they work (open ∩ their hours).</param>
/// <param name="Breaks">Breaks within the working time.</param>
/// <param name="TimeOff">Time off within the working time.</param>
/// <param name="AvailableMinutes">Minutes they can take bookings: the working time outside breaks and time off.</param>
public sealed record ProfessionalDayPlan(
    ProfessionalId ProfessionalId, IReadOnlyList<TimeWindow> Working, IReadOnlyList<TimeWindow> Breaks, IReadOnlyList<TimeWindow> TimeOff, int AvailableMinutes);
