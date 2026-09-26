using Trimme.BuildingBlocks.Domain.Results;

namespace Trimme.Modules.Identity.Domain;

/// <summary>Stable error codes of the Identity module (public API contract; the web app localises them).</summary>
public static class IdentityErrors
{
    public static Error InvalidPhone() =>
        Error.Validation("validation.failed", "The mobile number is invalid.", Field("phone", "validation.phone_invalid"));

    public static Error OtpResendCooldown(TimeSpan retryAfter) =>
        Error.RateLimited("otp.resend_cooldown", "Wait before requesting another code.")
            .WithDetail("retryAfterSeconds", Seconds(retryAfter));

    public static Error OtpTooManyRequests(TimeSpan retryAfter) =>
        Error.RateLimited("otp.too_many_requests", "Too many codes were requested for this number.")
            .WithDetail("retryAfterSeconds", Seconds(retryAfter));

    public static Error OtpDeliveryUnavailable() =>
        Error.Unavailable("otp.delivery_unavailable", "The verification code could not be sent. Try again later.");

    public static Error OtpChallengeInvalid() =>
        Error.Validation("otp.challenge_invalid", "This code is no longer valid. Request a new code.");

    public static Error OtpExpired() =>
        Error.Validation("otp.expired", "The code has expired. Request a new code.");

    public static Error OtpAttemptsExhausted() =>
        Error.Validation("otp.attempts_exhausted", "Too many incorrect attempts. Request a new code.");

    public static Error OtpIncorrect(int attemptsRemaining) =>
        Error.Validation("otp.incorrect", "The code is incorrect.", Field("code", "otp.incorrect"))
            .WithDetail("attemptsRemaining", attemptsRemaining);

    public static Error InvalidCredentials() =>
        Error.Unauthorized("auth.invalid_credentials", "The email or password is incorrect.");

    public static Error LockedOut(TimeSpan retryAfter) =>
        Error.Forbidden("auth.locked_out", "The account is temporarily locked.")
            .WithDetail("retryAfterSeconds", Seconds(retryAfter));

    public static Error AccountDisabled() =>
        Error.Forbidden("auth.account_disabled", "The account is disabled.");

    public static Error RefreshInvalid() =>
        Error.Unauthorized("auth.refresh_invalid", "The session has ended. Sign in again.");

    /// <summary>Another tab rotated the same refresh token a moment ago; the client should retry with its current cookies.</summary>
    public static Error RefreshRace() =>
        Error.Conflict("auth.refresh_race", "The session was refreshed concurrently. Retry the request.");

    public static Error PasswordResetInvalid() =>
        Error.Validation("auth.reset_invalid", "The reset link is invalid or has expired.");

    public static Error PasswordPolicy(IReadOnlyList<string> codes) =>
        Error.Validation("validation.failed", "The password does not meet the policy.", Field("password", [.. codes]));

    public static Error InvitationInvalid() =>
        Error.Validation("invitation.invalid", "The invitation is invalid, used or expired.");

    public static Error EmailAlreadyRegistered() =>
        Error.Conflict("invitation.email_registered", "An account with this email already exists.");

    public static Error RoleNotAssignable() =>
        Error.Validation("validation.failed", "The role cannot be assigned to this account type.", Field("role", "invitation.role_invalid"));

    public static Error ShopNotFound() =>
        Error.NotFound("shop.not_found", "The shop was not found.");

    public static Error SessionNotFound() =>
        Error.NotFound("auth.session_not_found", "The session was not found.");

    public static Error UserNotFound() =>
        Error.NotFound("auth.user_not_found", "The user was not found.");

    private static Dictionary<string, string[]> Field(string field, params string[] codes) =>
        new(StringComparer.Ordinal) { [field] = codes };

    private static int Seconds(TimeSpan value) => (int)Math.Ceiling(Math.Max(1, value.TotalSeconds));
}
