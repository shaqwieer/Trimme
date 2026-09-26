using Trimme.BuildingBlocks.Domain.Results;
using Trimme.Modules.Identity.Domain;

namespace Trimme.Modules.Identity.Application;

/// <summary>An account as the use cases see it; ASP.NET Core Identity stays behind <see cref="IAccountStore"/>.</summary>
public sealed record AccountSummary(
    Guid UserId,
    UserType UserType,
    string? DisplayName,
    string? Email,
    string? ProtectedPhone,
    string PreferredLocale,
    DateTimeOffset? TermsAcceptedAt,
    IReadOnlyList<string> Roles,
    bool IsDisabled);

public sealed record NewCustomer(string PhoneHash, string ProtectedPhone, string PreferredLocale, DateTimeOffset? TermsAcceptedAt);

public sealed record NewStaffAccount(string Email, string DisplayName, string Password, UserType UserType, string RoleName, string PreferredLocale);

public sealed record PasswordResetTicket(Guid UserId, string Email, string? DisplayName, string PreferredLocale, string Token);

public enum StaffPasswordOutcome
{
    Succeeded,
    Invalid,
    LockedOut,
    Disabled,
}

public sealed record StaffPasswordCheck(StaffPasswordOutcome Outcome, AccountSummary? Account = null, DateTimeOffset? LockedUntil = null);

/// <summary>Account persistence and credential checks (implemented over ASP.NET Core Identity in Infrastructure).</summary>
internal interface IAccountStore
{
    string NormalizeEmail(string email);

    Task<AccountSummary?> FindAsync(Guid userId, CancellationToken cancellationToken);

    Task<AccountSummary?> FindCustomerByPhoneHashAsync(string phoneHash, CancellationToken cancellationToken);

    /// <summary>Creates a customer with a verified mobile. If the number was registered concurrently, returns that account.</summary>
    Task<(AccountSummary Account, bool Created)> FindOrCreateCustomerAsync(NewCustomer customer, CancellationToken cancellationToken);

    Task UpdateProfileAsync(Guid userId, string displayName, string preferredLocale, DateTimeOffset? termsAcceptedAt, CancellationToken cancellationToken);

    /// <summary>Checks a staff password with lockout (5 failures → 15 minutes). Unknown emails cost the same as known ones.</summary>
    Task<StaffPasswordCheck> CheckStaffPasswordAsync(string email, string password, CancellationToken cancellationToken);

    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken);

    Task<bool> RoleExistsAsync(string roleName, UserType userType, CancellationToken cancellationToken);

    Task<Result<AccountSummary>> CreateStaffAsync(NewStaffAccount account, CancellationToken cancellationToken);

    /// <summary>Returns a reset token for an active staff account, or <see langword="null"/> (never reveals which).</summary>
    Task<PasswordResetTicket?> CreatePasswordResetAsync(string email, CancellationToken cancellationToken);

    Task<Result> ResetPasswordAsync(Guid userId, string token, string newPassword, CancellationToken cancellationToken);

    Task<bool> AnyUserInRoleAsync(string roleName, CancellationToken cancellationToken);
}

/// <summary>Delivers a customer sign-in code (WhatsApp authentication template in production, Phase 15; a dev inbox locally).</summary>
public interface IOtpSender
{
    /// <returns><see langword="false"/> when the code could not be delivered.</returns>
    Task<bool> TrySendAsync(OtpMessage message, CancellationToken cancellationToken);
}

public sealed record OtpMessage(string PhoneE164, string Code, string Locale, DateTimeOffset ExpiresAt);

/// <summary>Sends the Identity module's emails (password reset, staff invitation) in the recipient's language.</summary>
internal interface IIdentityMailer
{
    Task SendPasswordResetAsync(PasswordResetTicket ticket, CancellationToken cancellationToken);

    Task SendInvitationAsync(string email, string locale, string roleName, string token, DateTimeOffset expiresAt, CancellationToken cancellationToken);
}

/// <summary>Caller details used to label a session. The IP address is hashed before storage.</summary>
public sealed record ClientContext(string? UserAgent, string? IpAddress);
