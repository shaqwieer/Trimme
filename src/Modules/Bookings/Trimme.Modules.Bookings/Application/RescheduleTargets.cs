using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Scheduling;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.Modules.Bookings.Domain;

namespace Trimme.Modules.Bookings.Application;

/// <summary>Who may take a rescheduled booking: its own professional, or another active one assigned to the booked item.</summary>
internal static class RescheduleTargets
{
    public static async Task<IReadOnlyList<ProfessionalSummary>> CandidatesAsync(
        Booking booking, IBookableOfferCatalog catalog, IProfessionalDirectory professionals, CancellationToken cancellationToken)
    {
        var staff = await professionals.ListByShopAsync(booking.ShopId, cancellationToken);
        var offer = await catalog.FindBookedAsync(
            booking.ShopId, booking.ServiceId, booking.PackageId, [.. booking.PackageItems.Select(i => i.ServiceId)], cancellationToken);
        var eligible = offer?.EligibleProfessionalIds.ToHashSet() ?? [];
        return [.. staff.Where(p => p.Id == booking.ProfessionalId || (p.IsActive && eligible.Contains(p.Id)))];
    }

    public static async Task<Result<BookedProfessional>> ResolveAsync(
        Booking booking, Guid? professionalId, IBookableOfferCatalog catalog, IProfessionalDirectory professionals, CancellationToken cancellationToken)
    {
        var requested = new ProfessionalId(professionalId ?? booking.ProfessionalId.Value);
        var candidates = await CandidatesAsync(booking, catalog, professionals, cancellationToken);
        if (candidates.SingleOrDefault(p => p.Id == requested) is not { } professional)
        {
            return requested == booking.ProfessionalId
                ? Error.NotFound("professional.not_found", "The professional was not found.")
                : BookingErrors.ProfessionalNotEligible();
        }

        if (!professional.IsActive)
        {
            return BookingErrors.ProfessionalNotEligible();
        }

        return new BookedProfessional(professional.Id, professional.NameAr, professional.NameEn);
    }
}
