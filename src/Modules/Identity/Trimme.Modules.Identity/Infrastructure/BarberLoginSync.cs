using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Identity.Domain;
using Trimme.Modules.Identity.Infrastructure.Persistence;

namespace Trimme.Modules.Identity.Infrastructure;

/// <summary>
/// <see cref="IBarberLoginSync"/> (D-145): a barber's login follows their professional. Changes are staged on the shared
/// unit of work, which the Professionals module's admin command saves with its own change.
/// </summary>
internal sealed class BarberLoginSync(TrimmeDbContext db, TimeProvider clock) : IBarberLoginSync
{
    public async Task NumberChangedAsync(ProfessionalId professionalId, string? loginHash, CancellationToken cancellationToken)
    {
        var user = await LoginAsync(professionalId, cancellationToken);
        if (user is not null)
        {
            user.LoginPhoneHash = loginHash;
        }
    }

    public async Task ProfessionalDisabledAsync(ProfessionalId professionalId, CancellationToken cancellationToken)
    {
        var user = await LoginAsync(professionalId, cancellationToken);
        if (user is null)
        {
            return;
        }

        var now = clock.GetUtcNow();
        user.DisabledAt ??= now;
        var sessions = await db.Set<UserSession>().Where(s => s.UserId == user.Id && s.RevokedAt == null).ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
            session.Revoke(SessionRevocationReason.LoginDisabled, now);
        }
    }

    private Task<ApplicationUser?> LoginAsync(ProfessionalId professionalId, CancellationToken cancellationToken) =>
        db.Set<ApplicationUser>().SingleOrDefaultAsync(u => u.ProfessionalId == professionalId.Value && u.UserType == UserType.ShopUser, cancellationToken);
}
