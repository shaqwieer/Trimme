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
    bool IsDisabled,
    Guid? ShopId = null,
    bool MustChangePassword = false,
    Guid? ProfessionalId = null);

/// <summary>
/// A customer to find or create by number. <c>PhoneVerified</c> is false for a sign-in without a code (D-143); a later
/// code sign-in marks the account's number as confirmed.
/// </summary>
public sealed record NewCustomer(
    string PhoneHash, string ProtectedPhone, string PreferredLocale, DateTimeOffset? TermsAcceptedAt, bool PhoneVerified = true);

/// <summary>
/// A staff account to create. <c>MustChangePassword</c> marks a temporary password an admin set (D-139): the user
/// must replace it before signing in.
/// </summary>
public sealed record NewStaffAccount(
    string Email,
    string DisplayName,
    string Password,
    UserType UserType,
    string RoleName,
    string PreferredLocale,
    Guid? ShopId = null,
    bool MustChangePassword = false);

/// <summary>A customer's own profile as stored (D-144); the number stays protected here and is masked on the way out.</summary>
public sealed record CustomerProfileRow(
    Guid UserId, string? DisplayName, string PreferredLocale, string? ContactEmail, string? ProtectedPhone, string? PhoneHash);

/// <summary>
/// What a customer saves on "personal info" (D-144). <c>NewPhoneHash</c> and <c>NewProtectedPhone</c> are set only
/// when the number changes; the account then signs in with the new number, which is unconfirmed until a code confirms it.
/// </summary>
public sealed record CustomerProfileUpdate(
    string DisplayName, string PreferredLocale, string? ContactEmail, string? NewPhoneHash = null, string? NewProtectedPhone = null);

/// <summary>A barber's login as the owner and admin see it (D-145): never the number or the password.</summary>
public sealed record BarberLogin(Guid UserId, bool Enabled, bool MustChangePassword, bool HasNumber);

/// <summary>What turning a barber's login on writes (D-145). <c>LoginHash</c> is the professional's number under the login purpose.</summary>
public sealed record NewBarberLogin(Guid ProfessionalId, Guid ShopId, string DisplayName, string LoginHash, string Password, string PreferredLocale);

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

    /// <summary>An enabled customer's own profile (D-144), or <see langword="null"/> for anyone else.</summary>
    Task<CustomerProfileRow?> FindCustomerProfileAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Saves a customer's profile. <see langword="false"/> when the new number belongs to another account.</summary>
    Task<bool> UpdateCustomerProfileAsync(Guid userId, CustomerProfileUpdate update, CancellationToken cancellationToken);

    /// <summary>
    /// Erases a customer's name, number and email and closes the account for good (D-144). Staged on the context: the
    /// caller saves it with the rest of the deletion.
    /// </summary>
    Task EraseCustomerAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Checks a staff password with lockout (5 failures → 15 minutes). Unknown emails cost the same as known ones.</summary>
    Task<StaffPasswordCheck> CheckStaffPasswordAsync(string email, string password, CancellationToken cancellationToken);

    /// <summary>As <see cref="CheckStaffPasswordAsync"/>, for a barber signing in with their number's login hash (D-145).</summary>
    Task<StaffPasswordCheck> CheckBarberPasswordAsync(string loginHash, string password, CancellationToken cancellationToken);

    /// <summary>A professional's barber login, if one was ever created (D-145).</summary>
    Task<BarberLogin?> FindBarberLoginAsync(Guid professionalId, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the barber's login, or turns it on again with a new temporary password (D-145). Saved; returns the
    /// account id, or the password policy's errors.
    /// </summary>
    Task<Result<Guid>> SetBarberLoginAsync(NewBarberLogin login, CancellationToken cancellationToken);

    /// <summary>Turns a barber's login off; the caller revokes its sessions. <see langword="null"/> when there is none.</summary>
    Task<Guid?> DisableBarberLoginAsync(Guid professionalId, CancellationToken cancellationToken);

    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken);

    Task<bool> RoleExistsAsync(string roleName, UserType userType, CancellationToken cancellationToken);

    Task<Result<AccountSummary>> CreateStaffAsync(NewStaffAccount account, CancellationToken cancellationToken);

    /// <summary>Returns a reset token for an active staff account, or <see langword="null"/> (never reveals which).</summary>
    Task<PasswordResetTicket?> CreatePasswordResetAsync(string email, CancellationToken cancellationToken);

    Task<Result> ResetPasswordAsync(Guid userId, string token, string newPassword, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces a staff password the caller has just proven (D-139) and clears <c>MustChangePassword</c>. The new
    /// password must differ from the current one.
    /// </summary>
    Task<Result> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken);

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
