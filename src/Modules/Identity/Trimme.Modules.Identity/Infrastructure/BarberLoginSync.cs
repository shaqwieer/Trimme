using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Auditing;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Identity.Application;
using Trimme.Modules.Identity.Application.Barbers;
using Trimme.Modules.Identity.Domain;
using Trimme.Modules.Identity.Infrastructure.Persistence;

namespace Trimme.Modules.Identity.Infrastructure;

/// <summary>
/// <see cref="IBarberLoginSync"/> (D-145): a barber's login follows their professional. Changes are staged on the shared
/// unit of work, which the Professionals module's command saves with its own change; a new barber's login (D-146) is
/// saved at once, inside that command's transaction.
/// </summary>
internal sealed class BarberLoginSync(TrimmeDbContext db, IAccountStore accounts, IAuditLog audit, TimeProvider clock) : IBarberLoginSync
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

    public async Task NameChangedAsync(ProfessionalId professionalId, string nameAr, CancellationToken cancellationToken)
    {
        var user = await LoginAsync(professionalId, cancellationToken);
        if (user is not null)
        {
            user.DisplayName = nameAr;
        }
    }

    public async Task<Result<string>> CreateAsync(NewBarberAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        var password = TemporaryPasswords.New();
        var saved = await accounts.SetBarberLoginAsync(
            new NewBarberLogin(account.ProfessionalId.Value, account.ShopId.Value, account.DisplayName, account.LoginHash, password, "ar"),
            cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        // Staged: the caller saves it with its own change, before its transaction commits. Never the password.
        audit.Record(new AuditRecord(
            "barber_login.enabled", "Professional", account.ProfessionalId.ToString(), account.ShopId, "Login made with the barber, temporary password generated",
            SummaryCode: "barber_login.created_with_barber"));
        return password;
    }

    private Task<ApplicationUser?> LoginAsync(ProfessionalId professionalId, CancellationToken cancellationToken) =>
        db.Set<ApplicationUser>().SingleOrDefaultAsync(u => u.ProfessionalId == professionalId.Value && u.UserType == UserType.ShopUser, cancellationToken);
}
