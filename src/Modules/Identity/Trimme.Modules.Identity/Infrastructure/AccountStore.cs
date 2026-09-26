using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Identity.Application;
using Trimme.Modules.Identity.Domain;
using Trimme.Modules.Identity.Infrastructure.Persistence;

namespace Trimme.Modules.Identity.Infrastructure;

/// <summary><see cref="IAccountStore"/> and <see cref="IRoleDirectory"/> over ASP.NET Core Identity.</summary>
internal sealed class AccountStore(
    UserManager<ApplicationUser> users,
    TrimmeDbContext db,
    TimeProvider clock,
    IPasswordHasher<ApplicationUser> hasher) : IAccountStore, IRoleDirectory
{
    /// <summary>Hash verified for unknown emails so both paths cost one password hash (no timing oracle).</summary>
    private static readonly Lazy<string> DummyHash = new(() =>
        new PasswordHasher<ApplicationUser>().HashPassword(new ApplicationUser(), Guid.NewGuid().ToString("N")));

    public string NormalizeEmail(string email) => users.NormalizeEmail(email) ?? string.Empty;

    public async Task<AccountSummary?> FindAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Set<ApplicationUser>().AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        return user is null ? null : await SummarizeAsync(user, cancellationToken);
    }

    public async Task<AccountSummary?> FindCustomerByPhoneHashAsync(string phoneHash, CancellationToken cancellationToken)
    {
        var user = await db.Set<ApplicationUser>().AsNoTracking()
            .SingleOrDefaultAsync(u => u.PhoneLookupHash == phoneHash && u.UserType == UserType.Customer, cancellationToken);
        return user is null ? null : await SummarizeAsync(user, cancellationToken);
    }

    public async Task<(AccountSummary Account, bool Created)> FindOrCreateCustomerAsync(NewCustomer customer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(customer);

        if (await FindCustomerByPhoneHashAsync(customer.PhoneHash, cancellationToken) is { } existing)
        {
            return (existing, false);
        }

        var id = Guid.CreateVersion7();
        var user = new ApplicationUser
        {
            Id = id,
            UserName = $"customer-{id:N}",
            UserType = UserType.Customer,
            PhoneLookupHash = customer.PhoneHash,
            ProtectedPhone = customer.ProtectedPhone,
            PhoneNumberConfirmed = true,
            PreferredLocale = customer.PreferredLocale,
            TermsAcceptedAt = customer.TermsAcceptedAt,
            CreatedAt = clock.GetUtcNow(),
            LockoutEnabled = true,
        };

        try
        {
            EnsureSucceeded(await users.CreateAsync(user));
            EnsureSucceeded(await users.AddToRoleAsync(user, SystemRoles.Customer));
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The same number verified twice at once: the other request created the account.
            db.ChangeTracker.Clear();
            var winner = await FindCustomerByPhoneHashAsync(customer.PhoneHash, cancellationToken);
            return (winner ?? throw new InvalidOperationException("Customer creation conflicted but no account was found.", ex), false);
        }

        return ((await FindAsync(id, cancellationToken))!, true);
    }

    public async Task UpdateProfileAsync(Guid userId, string displayName, string preferredLocale, DateTimeOffset? termsAcceptedAt, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new InvalidOperationException("User not found.");
        user.DisplayName = displayName;
        user.PreferredLocale = preferredLocale;
        user.TermsAcceptedAt = termsAcceptedAt;
        EnsureSucceeded(await users.UpdateAsync(user));
    }

    public async Task<StaffPasswordCheck> CheckStaffPasswordAsync(string email, string password, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null || user.UserType == UserType.Customer || user.PasswordHash is null)
        {
            hasher.VerifyHashedPassword(new ApplicationUser(), DummyHash.Value, password);
            return new StaffPasswordCheck(StaffPasswordOutcome.Invalid);
        }

        if (await users.IsLockedOutAsync(user))
        {
            return new StaffPasswordCheck(StaffPasswordOutcome.LockedOut, LockedUntil: user.LockoutEnd);
        }

        if (!await users.CheckPasswordAsync(user, password))
        {
            await users.AccessFailedAsync(user);
            return await users.IsLockedOutAsync(user)
                ? new StaffPasswordCheck(StaffPasswordOutcome.LockedOut, LockedUntil: user.LockoutEnd)
                : new StaffPasswordCheck(StaffPasswordOutcome.Invalid);
        }

        if (user.DisabledAt is not null)
        {
            return new StaffPasswordCheck(StaffPasswordOutcome.Disabled);
        }

        await users.ResetAccessFailedCountAsync(user);
        return new StaffPasswordCheck(StaffPasswordOutcome.Succeeded, await SummarizeAsync(user, cancellationToken));
    }

    public async Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken) =>
        await users.FindByEmailAsync(email) is not null;

    public Task<bool> RoleExistsAsync(string roleName, UserType userType, CancellationToken cancellationToken) =>
        db.Set<ApplicationRole>().AnyAsync(r => r.Name == roleName && r.UserType == userType, cancellationToken);

    public async Task<Result<AccountSummary>> CreateStaffAsync(NewStaffAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (await users.FindByEmailAsync(account.Email) is not null)
        {
            return IdentityErrors.EmailAlreadyRegistered();
        }

        var id = Guid.CreateVersion7();
        var user = new ApplicationUser
        {
            Id = id,
            UserName = $"staff-{id:N}",
            Email = account.Email,
            EmailConfirmed = true,
            DisplayName = account.DisplayName,
            UserType = account.UserType,
            ShopId = account.ShopId is { } shopId ? new ShopId(shopId) : null,
            PreferredLocale = account.PreferredLocale,
            CreatedAt = clock.GetUtcNow(),
            LockoutEnabled = true,
        };

        var created = await users.CreateAsync(user, account.Password);
        if (!created.Succeeded)
        {
            return PasswordOrThrow(created);
        }

        EnsureSucceeded(await users.AddToRoleAsync(user, account.RoleName));
        return (await FindAsync(id, cancellationToken))!;
    }

    public async Task<PasswordResetTicket?> CreatePasswordResetAsync(string email, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null || user.UserType == UserType.Customer || user.DisabledAt is not null || user.Email is null)
        {
            return null;
        }

        var token = await users.GeneratePasswordResetTokenAsync(user);
        return new PasswordResetTicket(user.Id, user.Email, user.DisplayName, user.PreferredLocale, token);
    }

    public async Task<Result> ResetPasswordAsync(Guid userId, string token, string newPassword, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null || user.UserType == UserType.Customer || user.DisabledAt is not null)
        {
            return IdentityErrors.PasswordResetInvalid();
        }

        var reset = await users.ResetPasswordAsync(user, token, newPassword);
        if (!reset.Succeeded)
        {
            return reset.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken))
                ? IdentityErrors.PasswordResetInvalid()
                : PasswordOrThrow(reset);
        }

        // Proving control of the mailbox also lifts a lockout.
        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        return Result.Success();
    }

    public Task<bool> AnyUserInRoleAsync(string roleName, CancellationToken cancellationToken) =>
        (from userRole in db.Set<IdentityUserRole<Guid>>()
         join role in db.Set<ApplicationRole>() on userRole.RoleId equals role.Id
         where role.Name == roleName
         select userRole).AnyAsync(cancellationToken);

    public async Task<IReadOnlyList<RoleSummary>> ListAsync(CancellationToken cancellationToken) =>
        await db.Set<ApplicationRole>().AsNoTracking()
            .OrderBy(r => r.UserType).ThenBy(r => r.Name)
            .Select(r => new RoleSummary(r.Id, r.Name!, r.UserType))
            .ToListAsync(cancellationToken);

    private async Task<AccountSummary> SummarizeAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var roles = await (from userRole in db.Set<IdentityUserRole<Guid>>()
                           join role in db.Set<ApplicationRole>() on userRole.RoleId equals role.Id
                           where userRole.UserId == user.Id
                           orderby role.Name
                           select role.Name!).ToListAsync(cancellationToken);

        return new AccountSummary(
            user.Id,
            user.UserType,
            user.DisplayName,
            user.UserType == UserType.Customer ? null : user.Email,
            user.ProtectedPhone,
            user.PreferredLocale,
            user.TermsAcceptedAt,
            roles,
            user.DisabledAt is not null,
            user.ShopId?.Value);
    }

    /// <summary>Password-policy failures become field errors on <c>password</c>; anything else is a bug.</summary>
    private static Error PasswordOrThrow(IdentityResult result)
    {
        var codes = result.Errors
            .Select(e => e.Code)
            .Where(c => c.StartsWith("Password", StringComparison.Ordinal))
            .Select(c => c == nameof(IdentityErrorDescriber.PasswordTooShort) ? ValidationCodes.PasswordTooShort : ValidationCodes.PasswordTooWeak)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (codes.Length == 0)
        {
            EnsureSucceeded(result);
        }

        return IdentityErrors.PasswordPolicy(codes);
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Identity operation failed: " + string.Join(", ", result.Errors.Select(e => e.Code)));
        }
    }
}
