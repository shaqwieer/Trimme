using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Privacy;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Identity.Application.Sessions;
using Trimme.Modules.Identity.Domain;

namespace Trimme.Modules.Identity.Application.Otp;

/// <summary>Step 1 of customer sign-in/sign-up (D-005): send a one-time code to the mobile number.</summary>
internal sealed record RequestOtpCommand(string Phone, bool TermsAccepted, string Locale, ClientContext Client)
    : ICommand<Result<OtpChallengeResponse>>;

/// <summary>Step 2: verify the code; signs in an existing customer or creates the account (<c>isNewUser</c>).</summary>
internal sealed record VerifyOtpCommand(Guid ChallengeId, string Code, ClientContext Client) : ICommand<Result<SignInOutcome>>;

/// <summary>Step 3 (new customers): name, language and terms acceptance.</summary>
internal sealed record CompleteProfileCommand(Guid UserId, string DisplayName, string PreferredLocale, bool TermsAccepted)
    : ICommand<Result<MeResponse>>;

internal sealed class RequestOtpValidator : AbstractValidator<RequestOtpCommand>
{
    public RequestOtpValidator()
    {
        RuleFor(c => c.Phone).NotEmpty().WithErrorCode(ValidationCodes.Required);
        RuleFor(c => c.Locale).Must(Locales.IsSupported).WithErrorCode(ValidationCodes.Invalid);
    }
}

internal sealed class VerifyOtpValidator : AbstractValidator<VerifyOtpCommand>
{
    public VerifyOtpValidator()
    {
        RuleFor(c => c.ChallengeId).NotEmpty().WithErrorCode(ValidationCodes.Required);
        RuleFor(c => c.Code).NotEmpty().WithErrorCode(ValidationCodes.Required)
            .Matches($"^[0-9]{{{OtpPolicy.CodeLength}}}$").WithErrorCode(ValidationCodes.OtpIncomplete);
    }
}

internal sealed class CompleteProfileValidator : AbstractValidator<CompleteProfileCommand>
{
    public CompleteProfileValidator()
    {
        RuleFor(c => c.DisplayName).Must(n => !string.IsNullOrWhiteSpace(n)).WithErrorCode(ValidationCodes.Required)
            .MaximumLength(60).WithErrorCode(ValidationCodes.TooLong);
        RuleFor(c => c.PreferredLocale).Must(Locales.IsSupported).WithErrorCode(ValidationCodes.Invalid);
    }
}

internal sealed class RequestOtpHandler(
    TrimmeDbContext db,
    TimeProvider clock,
    IPersonalDataProtector protector,
    IOtpSender sender,
    OtpPolicy policy) : ICommandHandler<RequestOtpCommand, Result<OtpChallengeResponse>>
{
    public async Task<Result<OtpChallengeResponse>> Handle(RequestOtpCommand command, CancellationToken cancellationToken)
    {
        if (!MobileNumber.TryNormalize(command.Phone, out var phone))
        {
            return IdentityErrors.InvalidPhone();
        }

        var now = clock.GetUtcNow();
        var phoneHash = protector.LookupHash(phone, PersonalDataPurposes.MobileNumber);
        var windowStart = now - policy.CodeRequestWindow;

        var recent = await db.Set<OtpChallenge>()
            .Where(c => c.PhoneHash == phoneHash && c.CreatedAt > windowStart)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

        if (recent.Count > 0 && recent[^1].ResendAvailableAt > now)
        {
            return IdentityErrors.OtpResendCooldown(recent[^1].ResendAvailableAt - now);
        }

        if (recent.Count >= policy.MaxCodesPerNumber)
        {
            return IdentityErrors.OtpTooManyRequests(recent[^policy.MaxCodesPerNumber].CreatedAt + policy.CodeRequestWindow - now);
        }

        foreach (var previous in recent)
        {
            previous.Supersede(now);
        }

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        var id = EntityId.New<OtpChallengeId>();
        var challenge = new OtpChallenge(
            id,
            phoneHash,
            protector.Protect(phone, PersonalDataPurposes.MobileNumber),
            OtpCodes.Hash(protector, id, code),
            now,
            policy,
            command.TermsAccepted,
            command.Locale,
            string.IsNullOrWhiteSpace(command.Client.IpAddress) ? null : protector.LookupHash(command.Client.IpAddress, PersonalDataPurposes.IpAddress));

        db.Add(challenge);
        await db.SaveChangesAsync(cancellationToken);

        if (!await sender.TrySendAsync(new OtpMessage(phone, code, command.Locale, challenge.ExpiresAt), cancellationToken))
        {
            return IdentityErrors.OtpDeliveryUnavailable();
        }

        return new OtpChallengeResponse(id.Value, OtpPolicy.CodeLength, challenge.ExpiresAt, challenge.ResendAvailableAt);
    }
}

internal sealed class VerifyOtpHandler(
    TrimmeDbContext db,
    TimeProvider clock,
    IPersonalDataProtector protector,
    IAccountStore accounts,
    SessionManager sessions,
    MeReader me) : ICommandHandler<VerifyOtpCommand, Result<SignInOutcome>>
{
    public async Task<Result<SignInOutcome>> Handle(VerifyOtpCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var id = new OtpChallengeId(command.ChallengeId);
        var challenge = await db.Set<OtpChallenge>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (challenge is null || challenge.ConsumedAt is not null || challenge.SupersededAt is not null)
        {
            return IdentityErrors.OtpChallengeInvalid();
        }

        if (challenge.ExpiresAt <= now)
        {
            return IdentityErrors.OtpExpired();
        }

        if (challenge.AttemptsRemaining == 0)
        {
            return IdentityErrors.OtpAttemptsExhausted();
        }

        var expected = Encoding.ASCII.GetBytes(challenge.CodeHash);
        var actual = Encoding.ASCII.GetBytes(OtpCodes.Hash(protector, id, command.Code));
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            // Atomic increment so parallel guesses cannot exceed the attempt limit.
            var counted = await db.Set<OtpChallenge>()
                .Where(c => c.Id == id && c.FailedAttempts < c.MaxAttempts && c.ConsumedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.FailedAttempts, c => c.FailedAttempts + 1), cancellationToken);
            var remaining = counted == 0 ? 0 : challenge.AttemptsRemaining - 1;
            return remaining == 0 ? IdentityErrors.OtpAttemptsExhausted() : IdentityErrors.OtpIncorrect(remaining);
        }

        var consumed = await db.Set<OtpChallenge>()
            .Where(c => c.Id == id && c.ConsumedAt == null && c.SupersededAt == null && c.FailedAttempts < c.MaxAttempts)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now), cancellationToken);
        if (consumed == 0)
        {
            return IdentityErrors.OtpChallengeInvalid();
        }

        var (account, created) = await accounts.FindOrCreateCustomerAsync(
            new NewCustomer(challenge.PhoneHash, challenge.ProtectedPhone, challenge.Locale, challenge.TermsAccepted ? now : null),
            cancellationToken);

        if (account.IsDisabled)
        {
            return IdentityErrors.AccountDisabled();
        }

        var session = await sessions.StartAsync(account.UserId, UserType.Customer, command.Client, cancellationToken);
        return new SignInOutcome(session, new SignInResponse(created, await me.BuildAsync(account, cancellationToken)));
    }
}

internal sealed class CompleteProfileHandler(IAccountStore accounts, TimeProvider clock, MeReader me)
    : ICommandHandler<CompleteProfileCommand, Result<MeResponse>>
{
    public async Task<Result<MeResponse>> Handle(CompleteProfileCommand command, CancellationToken cancellationToken)
    {
        var account = await accounts.FindAsync(command.UserId, cancellationToken);
        if (account is null || account.UserType != UserType.Customer)
        {
            return IdentityErrors.UserNotFound();
        }

        var termsAcceptedAt = account.TermsAcceptedAt ?? (command.TermsAccepted ? clock.GetUtcNow() : null);
        if (termsAcceptedAt is null)
        {
            return Error.Validation(
                "validation.failed",
                "The terms must be accepted.",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["termsAccepted"] = [ValidationCodes.TermsRequired] });
        }

        await accounts.UpdateProfileAsync(account.UserId, command.DisplayName.Trim(), command.PreferredLocale, termsAcceptedAt, cancellationToken);
        return (await me.ReadAsync(account.UserId, cancellationToken))!;
    }
}

internal static class OtpCodes
{
    /// <summary>Keyed hash bound to the challenge, so a stored hash cannot be replayed against another challenge.</summary>
    public static string Hash(IPersonalDataProtector protector, OtpChallengeId id, string code) =>
        protector.LookupHash($"{id.Value:N}:{code}", PersonalDataPurposes.OtpCode);
}
