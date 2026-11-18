using Trimme.BuildingBlocks.Domain.Results;
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
/// Identity's side (D-145): when an admin or the owner changes a barber's WhatsApp number, a barber login follows it. The
/// change is staged on the shared unit of work; the caller saves it with the number.
/// </summary>
public interface IBarberLoginSync
{
    Task NumberChangedAsync(ProfessionalId professionalId, string? loginHash, CancellationToken cancellationToken);

    /// <summary>The professional was disabled: their login is turned off and every session ends (staged).</summary>
    Task ProfessionalDisabledAsync(ProfessionalId professionalId, CancellationToken cancellationToken);

    /// <summary>
    /// A new barber's login, made with the barber (D-146): a generated temporary password, returned once for whoever made
    /// the barber to hand over, and replaced at the first sign-in. The account is saved at once, so call this inside the
    /// caller's transaction, after the professional itself is saved.
    /// </summary>
    Task<Result<string>> CreateAsync(NewBarberAccount account, CancellationToken cancellationToken);
}

/// <summary>The new barber's account: their professional and shop, the name shown in the dashboard, and the number's login hash.</summary>
public sealed record NewBarberAccount(ProfessionalId ProfessionalId, ShopId ShopId, string DisplayName, string LoginHash);
