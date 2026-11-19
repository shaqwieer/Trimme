using Trimme.BuildingBlocks.Domain.Results;
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

    /// <summary>
    /// Several of the shop's services booked together as one contiguous appointment with one professional (like a package):
    /// durations and prices add up, the eligible professionals are those assigned to every service, and the services are
    /// the offer's items. One id is the service itself. <see langword="null"/> when any is not published or the
    /// currencies differ.
    /// </summary>
    Task<BookableOffer?> FindServicesAsync(ShopId shopId, IReadOnlyList<Guid> serviceIds, CancellationToken cancellationToken);

    /// <summary>
    /// A package and other services booked together (D-148), as one appointment with one professional: durations and
    /// prices add up (the package at its own price, each extra at its own), the eligible professionals do every service of
    /// the package and every extra, and the items are the package's services followed by the extras with their own price
    /// and length. <see langword="null"/> when any is not published, the currencies differ, or an extra is already in the
    /// package (see <see cref="BookableOfferRequest.OverlapsPackageAsync"/>).
    /// </summary>
    Task<BookableOffer?> FindPackageWithServicesAsync(ShopId shopId, Guid packageId, IReadOnlyList<Guid> serviceIds, CancellationToken cancellationToken);

    /// <summary>
    /// The shop's published services <paramref name="professionalId"/> does, each at its price and their own length (D-149),
    /// in the shop's order: what a barber may add to a booking at the visit (D-151, the client's 9-2).
    /// </summary>
    Task<IReadOnlyList<ProfessionalServiceOffer>> ServicesForAsync(ShopId shopId, ProfessionalId professionalId, CancellationToken cancellationToken);
}

/// <summary>One published service a barber does (D-151): its section, its price and the barber's own length.</summary>
public sealed record ProfessionalServiceOffer(
    Guid ServiceId, string NameAr, string? NameEn, Guid? SectionId, decimal Price, string Currency, int DurationMinutes);

/// <summary>How a request names what it books: one service, several services, one package, or a package with services (D-148).</summary>
public static class BookableOfferRequest
{
    /// <summary>At most this many services in one booking.</summary>
    public const int MaxServices = 10;

    /// <summary>
    /// <paramref name="serviceId"/> and <paramref name="serviceIds"/> as one list, in order and without repeats, or
    /// <see langword="null"/> when the request is invalid: no item at all, or too many services. A package may come with
    /// services (D-148).
    /// </summary>
    public static IReadOnlyList<Guid>? Services(Guid? serviceId, IEnumerable<Guid>? serviceIds, Guid? packageId)
    {
        List<Guid> services = [.. new[] { serviceId }.OfType<Guid>().Concat(serviceIds ?? []).Distinct()];
        return (services.Count == 0 && packageId is null) || services.Count > MaxServices ? null : services;
    }

    /// <summary>The offer for validated <paramref name="serviceIds"/> (see <see cref="Services"/>), a package, or both.</summary>
    public static Task<BookableOffer?> FindAsync(
        this IBookableOfferCatalog catalog, ShopId shopId, IReadOnlyList<Guid> serviceIds, Guid? packageId, CancellationToken cancellationToken) =>
        packageId is { } package
            ? serviceIds.Count > 0
                ? catalog.FindPackageWithServicesAsync(shopId, package, serviceIds, cancellationToken)
                : catalog.FindAsync(shopId, null, package, cancellationToken)
            : catalog.FindServicesAsync(shopId, serviceIds, cancellationToken);

    /// <summary>
    /// Whether a chosen service is already inside the chosen package (D-148): the request answers
    /// <see cref="ServiceInPackage"/> rather than dropping either.
    /// </summary>
    public static async Task<bool> OverlapsPackageAsync(
        this IBookableOfferCatalog catalog, ShopId shopId, IReadOnlyList<Guid> serviceIds, Guid? packageId, CancellationToken cancellationToken) =>
        packageId is { } package && serviceIds.Count > 0
        && await catalog.FindAsync(shopId, null, package, cancellationToken) is { } offer
        && serviceIds.Any(id => offer.Items.Any(item => item.ServiceId == id));

    /// <summary>400: a chosen service is already in the chosen package (D-148).</summary>
    public static Error ServiceInPackage() =>
        Error.Validation("booking.service_in_package", "A chosen service is already in the package.",
            new Dictionary<string, string[]>(StringComparer.Ordinal) { ["serviceIds"] = ["booking.service_in_package"] });

    /// <summary>
    /// The offer a booking was made for: its package (with the services booked with it, D-148: the items that carry their
    /// own price), its services when several were booked together (the first is the booking's service and all of them are
    /// its items), else its service.
    /// </summary>
    public static Task<BookableOffer?> FindBookedAsync(
        this IBookableOfferCatalog catalog, ShopId shopId, Guid? serviceId, Guid? packageId, IReadOnlyList<Guid> itemServiceIds, CancellationToken cancellationToken,
        IReadOnlyList<Guid>? packageExtras = null) =>
        packageId is { } package
            ? packageExtras is { Count: > 0 }
                ? catalog.FindPackageWithServicesAsync(shopId, package, packageExtras, cancellationToken)
                : catalog.FindAsync(shopId, null, package, cancellationToken)
            : itemServiceIds.Count > 1
                ? catalog.FindServicesAsync(shopId, itemServiceIds, cancellationToken)
                : catalog.FindAsync(shopId, serviceId, null, cancellationToken);
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
/// <param name="Items">For a package or several services booked together, the services in order; empty for one service.</param>
/// <param name="Lengths">
/// The barbers whose own length differs from <paramref name="DurationMinutes"/> (D-149: a haircut takes one barber 30
/// minutes and another 45); <see langword="null"/> or missing means the standard length. A package keeps the length the
/// shop set for it; services booked with it add each barber's own.
/// </param>
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
    IReadOnlyList<BookableOfferItem> Items,
    IReadOnlyDictionary<ProfessionalId, BarberLength>? Lengths = null)
{
    /// <summary>How long <paramref name="professional"/> takes for the whole offer.</summary>
    public int DurationFor(ProfessionalId professional) =>
        Lengths is not null && Lengths.TryGetValue(professional, out var length) ? length.DurationMinutes : DurationMinutes;

    /// <summary>Each barber's own length where it is not the standard one, for the availability engine.</summary>
    public IReadOnlyDictionary<ProfessionalId, int>? Durations =>
        Lengths is null || Lengths.Count == 0 ? null : Lengths.ToDictionary(l => l.Key, l => l.Value.DurationMinutes);

    /// <summary>
    /// The offer as <paramref name="professional"/> does it: their length, and their length for every item that carries
    /// its own (services booked together, or with a package), so the booking snapshot holds what this barber takes.
    /// </summary>
    public BookableOffer For(ProfessionalId professional)
    {
        if (Lengths is null || !Lengths.TryGetValue(professional, out var length))
        {
            return Lengths is null ? this : this with { Lengths = null };
        }

        return this with
        {
            DurationMinutes = length.DurationMinutes,
            Items = [.. Items.Select(i => i.DurationMinutes is not null && length.ItemDurations.TryGetValue(i.ServiceId, out var minutes) ? i with { DurationMinutes = minutes } : i)],
            Lengths = null,
        };
    }
}

/// <summary>One barber's length for an offer (D-149): the whole, and each item that carries its own length.</summary>
public sealed record BarberLength(int DurationMinutes, IReadOnlyDictionary<Guid, int> ItemDurations);

/// <summary>
/// One service of a package or of several services booked together, for the booking snapshot and reporting. Services
/// booked together carry their own price and length (D-147), so one can later be taken off the booking.
/// </summary>
public sealed record BookableOfferItem(Guid ServiceId, string NameAr, string? NameEn, decimal? Price = null, int? DurationMinutes = null);

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

/// <summary>
/// A booking's time. <paramref name="BlockedUntil"/> is when the barber is free again: the end plus the shop's cleaning
/// time at booking time (D-149); the same range the database's exclusion constraint compares.
/// </summary>
public sealed record BusyTime(Guid BookingId, ProfessionalId ProfessionalId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, DateTimeOffset? BlockedUntil = null)
{
    public DateTimeOffset BusyUntil => BlockedUntil is { } until && until > EndsAt ? until : EndsAt;
}

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
    /// The candidates who can take <c>[start, start + duration)</c> in this mode, in the given order, with the shop's
    /// cleaning time after it (D-149). <paramref name="durations"/> are the barbers whose own length differs from
    /// <paramref name="durationMinutes"/> (<see cref="BookableOffer.Durations"/>). <paramref name="ignoreBookingId"/> is the
    /// booking being rescheduled, whose own time does not block it.
    /// </summary>
    Task<IReadOnlyList<ProfessionalId>> FreeProfessionalsAsync(
        ShopId shopId,
        IReadOnlyList<ProfessionalId> candidates,
        DateTimeOffset start,
        int durationMinutes,
        AvailabilityCheckMode mode,
        Guid? ignoreBookingId,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<ProfessionalId, int>? durations = null);

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

/// <summary>
/// Each barber's own breaks on a run of shop-local dates, for the owner's reports (implemented by the Availability module,
/// D-147): how many and how many minutes. Only the one-off breaks a barber sets on a day; the shop's weekly breaks are
/// the owner's setting, not a barber's.
/// </summary>
public interface IBarberBreakReport
{
    Task<IReadOnlyDictionary<ProfessionalId, BreakTotals>> TotalsAsync(ShopId shopId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

public sealed record BreakTotals(int Count, int Minutes);

/// <summary>
/// The barbers marked «غير متوفر اليوم» (D-155, the client's 1-2), implemented by the Availability module, which stores the
/// mark as time off from the moment it was made to the end of the shop's day. Reads through the caller's scope (the shop's
/// tenant, an admin's, the public scope for one shop, or the system's).
/// </summary>
public interface IUnavailableBarbers
{
    /// <summary>The shop's barbers marked unavailable for the rest of today, with since when and the shop's day.</summary>
    Task<IReadOnlyList<UnavailableBarber>> TodayAsync(ShopId shopId, CancellationToken cancellationToken);

    /// <summary>How many days each barber was marked unavailable, by the marks made within [<paramref name="from"/>, <paramref name="to"/>).</summary>
    Task<IReadOnlyDictionary<ProfessionalId, int>> DaysAsync(ShopId shopId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

/// <summary>
/// A barber marked «غير متوفر اليوم» (D-155): when it was marked (<paramref name="Since"/>), the shop's local day it covers
/// ([<paramref name="DayStart"/>, <paramref name="DayEnd"/>)), and the account that marked it (the barber or the owner).
/// </summary>
public sealed record UnavailableBarber(ProfessionalId ProfessionalId, DateTimeOffset Since, DateTimeOffset DayStart, DateTimeOffset DayEnd, Guid? MarkedBy);
