using Trimme.BuildingBlocks.Application.Auditing;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.Modules.Identity.Application.Sessions;
using Trimme.Modules.Identity.Domain;

namespace Trimme.Modules.Identity.Application.Barbers;

/// <summary>
/// A barber's login as the owner and the admin see it (D-145). <c>HasNumber</c>: the professional has a WhatsApp number
/// to sign in with. Never the number or the password.
/// </summary>
public sealed record BarberLoginResponse(Guid ProfessionalId, bool Enabled, bool MustChangePassword, bool HasNumber);

internal static class BarberLoginErrors
{
    public static Error ProfessionalNotFound() => Error.NotFound("professional.not_found", "The professional was not found.");

    public static Error NumberRequired() =>
        Error.Validation("barber.number_required", "Add the barber's WhatsApp number before turning on their login.");

    public static Error ProfessionalInactive() =>
        Error.Conflict("barber.professional_inactive", "The barber is disabled; enable them before turning on their login.");
}

/// <summary>
/// Turning a barber's login on and off (D-145), shared by the owner's and the admin's commands. The professional is read
/// through the caller's scope: the owner's tenant, or the admin scope the admin command opens. A login is one account per
/// professional, of the same shop, created on the first «تفعيل»; turning it on again sets a new temporary password, which
/// the barber replaces at the first sign-in. Turning it off ends every session at once.
/// </summary>
internal sealed class BarberLoginManager(
    IAccountStore accounts,
    IProfessionalDirectory professionals,
    IProfessionalLoginNumbers numbers,
    SessionManager sessions,
    IAuditLog audit)
{
    public async Task<Result<BarberLoginResponse>> GetAsync(Guid professionalId, CancellationToken cancellationToken)
    {
        if (await professionals.FindAsync(new ProfessionalId(professionalId), cancellationToken) is null)
        {
            return BarberLoginErrors.ProfessionalNotFound();
        }

        return await ResponseAsync(professionalId, cancellationToken);
    }

    public async Task<Result<BarberLoginResponse>> EnableAsync(Guid professionalId, string password, CancellationToken cancellationToken)
    {
        var id = new ProfessionalId(professionalId);
        var professional = await professionals.FindAsync(id, cancellationToken);
        if (professional is null)
        {
            return BarberLoginErrors.ProfessionalNotFound();
        }

        if (!professional.IsActive)
        {
            return BarberLoginErrors.ProfessionalInactive();
        }

        if (await numbers.LoginHashAsync(id, cancellationToken) is not { } loginHash)
        {
            return BarberLoginErrors.NumberRequired();
        }

        var saved = await accounts.SetBarberLoginAsync(
            new NewBarberLogin(professionalId, professional.ShopId.Value, professional.NameAr, loginHash, password, "ar"), cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        // A new temporary password ends any session the barber still had; the revocation saves the audit entry with it.
        audit.Record(new AuditRecord("barber_login.enabled", "Professional", professionalId.ToString(), professional.ShopId, "Login on, temporary password set"));
        await sessions.RevokeAllAsync(saved.Value, keep: null, SessionRevocationReason.LoginDisabled, cancellationToken);
        return await ResponseAsync(professionalId, cancellationToken);
    }

    public async Task<Result<BarberLoginResponse>> DisableAsync(Guid professionalId, CancellationToken cancellationToken)
    {
        var professional = await professionals.FindAsync(new ProfessionalId(professionalId), cancellationToken);
        if (professional is null)
        {
            return BarberLoginErrors.ProfessionalNotFound();
        }

        if (await accounts.DisableBarberLoginAsync(professionalId, cancellationToken) is { } userId)
        {
            audit.Record(new AuditRecord("barber_login.disabled", "Professional", professionalId.ToString(), professional.ShopId, "Login off"));
            await sessions.RevokeAllAsync(userId, keep: null, SessionRevocationReason.LoginDisabled, cancellationToken);
        }

        return await ResponseAsync(professionalId, cancellationToken);
    }

    private async Task<BarberLoginResponse> ResponseAsync(Guid professionalId, CancellationToken cancellationToken)
    {
        var login = await accounts.FindBarberLoginAsync(professionalId, cancellationToken);
        var hasNumber = await numbers.LoginHashAsync(new ProfessionalId(professionalId), cancellationToken) is not null;
        return new BarberLoginResponse(professionalId, login is { Enabled: true, HasNumber: true }, login?.MustChangePassword ?? false, hasNumber);
    }
}
