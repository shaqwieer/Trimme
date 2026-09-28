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
public sealed record BookableOffer(Guid Id, bool IsPackage, int DurationMinutes, bool OnlineBookable, IReadOnlyList<ProfessionalId> EligibleProfessionalIds);

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

    /// <summary>
    /// Upcoming bookings of the shop overlapping [<paramref name="from"/>, <paramref name="to"/>), all professionals when
    /// <paramref name="professionalId"/> is <see langword="null"/>. Shown to the shop before it adds time off, a break or a closure.
    /// </summary>
    Task<IReadOnlyList<BookedAppointment>> GetAppointmentsAsync(
        ShopId shopId, ProfessionalId? professionalId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record BusyTime(ProfessionalId ProfessionalId, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

/// <summary>A booking as the shop may see it: the customer's name and the booked item, never a phone number.</summary>
public sealed record BookedAppointment(
    Guid BookingId,
    ProfessionalId ProfessionalId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string CustomerName,
    string ItemNameAr,
    string? ItemNameEn);
