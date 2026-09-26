using FluentValidation;
using Microsoft.Extensions.Logging;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.Modules.Identity.Application.Sessions;
using Trimme.Modules.Identity.Domain;

namespace Trimme.Modules.Identity.Application.Staff;

/// <summary>Shop and admin staff sign in with email + password (D-005). Lockout after repeated failures.</summary>
internal sealed record StaffSignInCommand(string Email, string Password, ClientContext Client) : ICommand<Result<SignInOutcome>>;

/// <summary>Always succeeds (enumeration-safe); emails a reset link only to an active staff account.</summary>
internal sealed record ForgotPasswordCommand(string Email, string Locale) : ICommand<Result>;

internal sealed record ResetPasswordCommand(Guid UserId, string Token, string NewPassword) : ICommand<Result>;

internal sealed class StaffSignInValidator : AbstractValidator<StaffSignInCommand>
{
    public StaffSignInValidator()
    {
        RuleFor(c => c.Email).NotEmpty().WithErrorCode(ValidationCodes.Required)
            .MaximumLength(256).WithErrorCode(ValidationCodes.TooLong);
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

internal sealed class StaffSignInHandler(IAccountStore accounts, SessionManager sessions, MeReader me, TimeProvider clock)
    : ICommandHandler<StaffSignInCommand, Result<SignInOutcome>>
{
    public async Task<Result<SignInOutcome>> Handle(StaffSignInCommand command, CancellationToken cancellationToken)
    {
        var check = await accounts.CheckStaffPasswordAsync(command.Email.Trim(), command.Password, cancellationToken);
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
        var session = await sessions.StartAsync(account.UserId, account.UserType, command.Client, cancellationToken);
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
