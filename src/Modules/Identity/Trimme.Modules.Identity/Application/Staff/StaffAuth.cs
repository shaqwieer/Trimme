using FluentValidation;
using Microsoft.Extensions.Logging;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Privacy;
using Trimme.BuildingBlocks.Domain.Privacy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.Modules.Identity.Application.Sessions;
using Trimme.Modules.Identity.Domain;

namespace Trimme.Modules.Identity.Application.Staff;

/// <summary>
/// Shop and admin staff sign in with email + password (D-005); a barber with their mobile number + password (D-145).
/// Lockout after repeated failures, on either path.
/// </summary>
internal sealed record StaffSignInCommand(string Email, string Password, ClientContext Client, string? Phone = null) : ICommand<Result<SignInOutcome>>;

/// <summary>Always succeeds (enumeration-safe); emails a reset link only to an active staff account.</summary>
internal sealed record ForgotPasswordCommand(string Email, string Locale) : ICommand<Result>;

internal sealed record ResetPasswordCommand(Guid UserId, string Token, string NewPassword) : ICommand<Result>;

/// <summary>
/// Replaces the temporary password an admin set (D-139) and signs in. The email and current password are checked as
/// at sign-in (lockout included), because the account has no session until this succeeds.
/// </summary>
internal sealed record ChangeTemporaryPasswordCommand(string Email, string Password, string NewPassword, ClientContext Client, string? Phone = null)
    : ICommand<Result<SignInOutcome>>;

/// <summary>
/// Who is signing in: an email (owners and admins) or a barber's mobile number (D-145). A number is normalised as the
/// Professionals module stores it and hashed under the login purpose; an unknown or invalid number costs the same as a
/// wrong password and answers the same.
/// </summary>
internal sealed class StaffCredentials(IAccountStore accounts, IPersonalDataProtector protector)
{
    public Task<StaffPasswordCheck> CheckAsync(string? email, string? phone, string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return accounts.CheckStaffPasswordAsync((email ?? string.Empty).Trim(), password, cancellationToken);
        }

        var hash = PhoneNumber.TryParseMobile(phone, out var number)
            ? protector.LookupHash(number.E164, PersonalDataPurposes.BarberLogin)
            : "no-such-number";
        return accounts.CheckBarberPasswordAsync(hash, password, cancellationToken);
    }
}

internal sealed class StaffSignInValidator : AbstractValidator<StaffSignInCommand>
{
    public StaffSignInValidator()
    {
        RuleFor(c => c.Email).NotEmpty().WithErrorCode(ValidationCodes.Required)
            .MaximumLength(256).WithErrorCode(ValidationCodes.TooLong)
            .When(c => string.IsNullOrWhiteSpace(c.Phone));
        RuleFor(c => c.Phone).MaximumLength(32).WithErrorCode(ValidationCodes.TooLong);
        RuleFor(c => c.Password).NotEmpty().WithErrorCode(ValidationCodes.Required)
            .MaximumLength(256).WithErrorCode(ValidationCodes.TooLong);
    }
}

internal sealed class ForgotPasswordValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordValidator()
    {
        RuleFor(c => c.Email).NotEmpty().WithErrorCode(ValidationCodes.Required)
            .EmailAddress().WithErrorCode(ValidationCodes.EmailInvalid)
            .MaximumLength(256).WithErrorCode(ValidationCodes.TooLong);
        RuleFor(c => c.Locale).Must(Locales.IsSupported).WithErrorCode(ValidationCodes.Invalid);
    }
}

internal sealed class ResetPasswordValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordValidator()
    {
        RuleFor(c => c.UserId).NotEmpty().WithErrorCode(ValidationCodes.Required);
        RuleFor(c => c.Token).NotEmpty().WithErrorCode(ValidationCodes.Required)
            .MaximumLength(2048).WithErrorCode(ValidationCodes.TooLong);
        RuleFor(c => c.NewPassword).NotEmpty().WithErrorCode(ValidationCodes.Required)
            .MaximumLength(256).WithErrorCode(ValidationCodes.TooLong);
    }
}

internal sealed class ChangeTemporaryPasswordValidator : AbstractValidator<ChangeTemporaryPasswordCommand>
{
    public ChangeTemporaryPasswordValidator()
    {
        RuleFor(c => c.Email).NotEmpty().WithErrorCode(ValidationCodes.Required)
            .MaximumLength(256).WithErrorCode(ValidationCodes.TooLong)
            .When(c => string.IsNullOrWhiteSpace(c.Phone));
        RuleFor(c => c.Phone).MaximumLength(32).WithErrorCode(ValidationCodes.TooLong);
        RuleFor(c => c.Password).NotEmpty().WithErrorCode(ValidationCodes.Required)
            .MaximumLength(256).WithErrorCode(ValidationCodes.TooLong);
        RuleFor(c => c.NewPassword).NotEmpty().WithErrorCode(ValidationCodes.Required)
            .MaximumLength(256).WithErrorCode(ValidationCodes.TooLong);
    }
}

internal sealed class StaffSignInHandler(StaffCredentials credentials, SessionManager sessions, MeReader me, TimeProvider clock)
    : ICommandHandler<StaffSignInCommand, Result<SignInOutcome>>
{
    public async Task<Result<SignInOutcome>> Handle(StaffSignInCommand command, CancellationToken cancellationToken)
    {
        var check = await credentials.CheckAsync(command.Email, command.Phone, command.Password, cancellationToken);
        switch (check.Outcome)
        {
            case StaffPasswordOutcome.LockedOut:
                return IdentityErrors.LockedOut((check.LockedUntil ?? clock.GetUtcNow()) - clock.GetUtcNow());
            case StaffPasswordOutcome.Disabled:
                return IdentityErrors.AccountDisabled();
            case StaffPasswordOutcome.Invalid:
                return IdentityErrors.InvalidCredentials();
        }

        var account = check.Account!;
        if (account.MustChangePassword)
        {
            return IdentityErrors.PasswordChangeRequired();
        }

        var session = await sessions.StartAsync(account, command.Client, cancellationToken);
        return new SignInOutcome(session, new SignInResponse(false, await me.BuildAsync(account, cancellationToken)));
    }
}

internal sealed class ChangeTemporaryPasswordHandler(
    IAccountStore accounts, StaffCredentials credentials, SessionManager sessions, MeReader me, TimeProvider clock)
    : ICommandHandler<ChangeTemporaryPasswordCommand, Result<SignInOutcome>>
{
    public async Task<Result<SignInOutcome>> Handle(ChangeTemporaryPasswordCommand command, CancellationToken cancellationToken)
    {
        var check = await credentials.CheckAsync(command.Email, command.Phone, command.Password, cancellationToken);
        switch (check.Outcome)
        {
            case StaffPasswordOutcome.LockedOut:
                return IdentityErrors.LockedOut((check.LockedUntil ?? clock.GetUtcNow()) - clock.GetUtcNow());
            case StaffPasswordOutcome.Disabled:
                return IdentityErrors.AccountDisabled();
            case StaffPasswordOutcome.Invalid:
                return IdentityErrors.InvalidCredentials();
        }

        // Only a temporary password is replaced here; anyone else changes theirs through the emailed reset.
        var account = check.Account!;
        if (!account.MustChangePassword)
        {
            return IdentityErrors.PasswordChangeNotRequired();
        }

        var changed = await accounts.ChangePasswordAsync(account.UserId, command.Password, command.NewPassword, cancellationToken);
        if (changed.IsFailure)
        {
            return changed.Error;
        }

        var session = await sessions.StartAsync(account, command.Client, cancellationToken);
        return new SignInOutcome(session, new SignInResponse(false, await me.BuildAsync(account, cancellationToken)));
    }
}

internal sealed partial class ForgotPasswordHandler(IAccountStore accounts, IIdentityMailer mailer, ILogger<ForgotPasswordHandler> logger)
    : ICommandHandler<ForgotPasswordCommand, Result>
{
    public async Task<Result> Handle(ForgotPasswordCommand command, CancellationToken cancellationToken)
    {
        var ticket = await accounts.CreatePasswordResetAsync(command.Email.Trim(), cancellationToken);
        if (ticket is not null)
        {
            try
            {
                // The link is in the account's saved language, falling back to the language of the request.
                await mailer.SendPasswordResetAsync(
                    ticket with { PreferredLocale = Locales.IsSupported(ticket.PreferredLocale) ? ticket.PreferredLocale : command.Locale },
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Never let a delivery failure answer differently from an unknown address (enumeration-safe, D-055).
                LogResetEmailFailed(logger, ex);
            }
        }

        return Result.Success();
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Password-reset email could not be sent")]
    private static partial void LogResetEmailFailed(ILogger logger, Exception exception);
}

internal sealed class ResetPasswordHandler(IAccountStore accounts, SessionManager sessions)
    : ICommandHandler<ResetPasswordCommand, Result>
{
    public async Task<Result> Handle(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        var result = await accounts.ResetPasswordAsync(command.UserId, command.Token, command.NewPassword, cancellationToken);
        if (result.IsFailure)
        {
            return result;
        }

        // A reset signs out every device: whoever knew the old password loses access.
        await sessions.RevokeAllAsync(command.UserId, keep: null, SessionRevocationReason.PasswordReset, cancellationToken);
        return Result.Success();
    }
}
