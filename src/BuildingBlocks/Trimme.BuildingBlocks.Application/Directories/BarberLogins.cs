using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.BuildingBlocks.Application.Directories;

/// <summary>
/// The Professionals module's side of a barber's sign-in (D-145): the login hash of the professional's WhatsApp number
/// (<c>PersonalDataPurposes.BarberLogin</c>), never the number. Reads through the caller's data scope.
/// </summary>
public interface IProfessionalLoginNumbers
{
    /// <summary>The professional's login hash, or <see langword="null"/> when they have no WhatsApp number.</summary>
    Task<string?> LoginHashAsync(ProfessionalId professionalId, CancellationToken cancellationToken);
}

/// <summary>
/// Identity's side (D-145): when an admin changes or removes a barber's WhatsApp number, a barber login follows it. The
/// change is staged on the shared unit of work; the caller saves it with the number.
/// </summary>
public interface IBarberLoginSync
{
    Task NumberChangedAsync(ProfessionalId professionalId, string? loginHash, CancellationToken cancellationToken);

    /// <summary>The professional was disabled: their login is turned off and every session ends (staged).</summary>
    Task ProfessionalDisabledAsync(ProfessionalId professionalId, CancellationToken cancellationToken);
}
