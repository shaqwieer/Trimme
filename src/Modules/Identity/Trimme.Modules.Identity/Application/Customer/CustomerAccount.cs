using FluentValidation;
using Microsoft.Extensions.Options;
using Trimme.BuildingBlocks.Application.Auditing;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Privacy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.Modules.Identity.Application.Sessions;
using Trimme.Modules.Identity.Domain;

namespace Trimme.Modules.Identity.Application.Customer;

// The customer's own account (D-144): personal info with an optional email and a change of mobile number, and deleting
// the account. Only the signed-in customer reaches these; the number is shown to them masked, as everywhere else.

/// <summary>
/// "Personal info": the name shops see, the language, an optional email (for the customer's records only), and the
/// number masked. <c>PhoneChangeAllowed</c> is false while sign-in codes are on (a new number would need a code).
/// </summary>
public sealed record CustomerProfileResponse(string? DisplayName, string PreferredLocale, string? Email, string? PhoneMasked, bool PhoneChangeAllowed);

internal sealed record GetCustomerProfileQuery(Guid UserId) : IQuery<CustomerProfileResponse?>;

/// <summary><c>Phone</c> empty or the current number keeps it; another number moves the account to it.</summary>
internal sealed record UpdateCustomerProfileCommand(Guid UserId, string DisplayName, string PreferredLocale, string? Email, string? Phone)
    : ICommand<Result<CustomerProfileResponse>>;

internal sealed record DeleteCustomerAccountCommand(Guid UserId) : ICommand<Result>;

internal sealed class UpdateCustomerProfileValidator : AbstractValidator<UpdateCustomerProfileCommand>
{
    public const int EmailMaxLength = 254;

    public UpdateCustomerProfileValidator()
    {
        RuleFor(c => c.DisplayName).Must(n => !string.IsNullOrWhiteSpace(n)).WithErrorCode(ValidationCodes.Required)
            .MaximumLength(60).WithErrorCode(ValidationCodes.TooLong);
        RuleFor(c => c.PreferredLocale).Must(Locales.IsSupported).WithErrorCode(ValidationCodes.Invalid);
        RuleFor(c => c.Email).MaximumLength(EmailMaxLength).WithErrorCode(ValidationCodes.TooLong)
            .EmailAddress().WithErrorCode(ValidationCodes.EmailInvalid)
            .When(c => !string.IsNullOrWhiteSpace(c.Email));
    }
}

internal sealed class GetCustomerProfileHandler(IAccountStore accounts, IPersonalDataProtector protector, IOptions<CustomerSignInOptions> signIn)
    : IQueryHandler<GetCustomerProfileQuery, CustomerProfileResponse?>
{
    public async Task<CustomerProfileResponse?> Handle(GetCustomerProfileQuery query, CancellationToken cancellationToken) =>
        await accounts.FindCustomerProfileAsync(query.UserId, cancellationToken) is { } profile
            ? CustomerProfiles.ToResponse(profile, protector, signIn.Value)
            : null;
}

internal sealed class UpdateCustomerProfileHandler(
    IAccountStore accounts,
    IPersonalDataProtector protector,
    IOptions<CustomerSignInOptions> signIn,
    IAuditLog audit) : ICommandHandler<UpdateCustomerProfileCommand, Result<CustomerProfileResponse>>
{
    public async Task<Result<CustomerProfileResponse>> Handle(UpdateCustomerProfileCommand command, CancellationToken cancellationToken)
    {
        if (await accounts.FindCustomerProfileAsync(command.UserId, cancellationToken) is not { } current)
        {
            return IdentityErrors.UserNotFound();
        }

        string? newHash = null;
        string? newProtected = null;
        if (!string.IsNullOrWhiteSpace(command.Phone))
        {
            if (!MobileNumber.TryNormalize(command.Phone, out var phone))
            {
                return Error.Validation("validation.failed", "The mobile number is invalid.",
                    new Dictionary<string, string[]>(StringComparer.Ordinal) { ["phone"] = ["validation.phone_invalid"] });
            }

            var hash = protector.LookupHash(phone, PersonalDataPurposes.MobileNumber);
            if (hash != current.PhoneHash)
            {
                // With codes on, a new number must be confirmed by a code first; this form has no code step yet.
                if (signIn.Value.Enabled)
                {
                    return IdentityErrors.PhoneChangeNeedsCode();
                }

                newHash = hash;
                newProtected = protector.Protect(phone, PersonalDataPurposes.MobileNumber);
            }
        }

        var email = string.IsNullOrWhiteSpace(command.Email) ? null : command.Email.Trim();
        var changed = new List<string>();
        if (newHash is not null)
        {
            changed.Add("phone");
        }

        if (!string.Equals(email, current.ContactEmail, StringComparison.OrdinalIgnoreCase))
        {
            changed.Add("email");
        }

        if (changed.Count > 0)
        {
            // Which fields changed, never their values (no personal data in the audit trail).
            audit.Record(new AuditRecord("customer.contact_changed", "Customer", command.UserId.ToString(), Summary: $"Changed: {string.Join(", ", changed)}"));
        }

        var saved = await accounts.UpdateCustomerProfileAsync(
            command.UserId,
            new CustomerProfileUpdate(command.DisplayName.Trim(), command.PreferredLocale, email, newHash, newProtected),
            cancellationToken);
        if (!saved)
        {
            return IdentityErrors.PhoneTaken();
        }

        var profile = await accounts.FindCustomerProfileAsync(command.UserId, cancellationToken);
        return CustomerProfiles.ToResponse(profile!, protector, signIn.Value);
    }
}

/// <summary>
/// Deleting the account (D-144): refused while bookings are ahead; otherwise the name, number and email are erased, the
/// modules' personal data about the customer goes (favorites), every session ends and the account stays closed. The
/// number can sign up again as a new account. Bookings and reviews keep the name they were made with, for the shops.
/// </summary>
internal sealed class DeleteCustomerAccountHandler(
    IAccountStore accounts,
    ICustomerUpcomingBookings upcoming,
    IEnumerable<ICustomerDataEraser> erasers,
    SessionManager sessions,
    IAuditLog audit,
    TimeProvider clock) : ICommandHandler<DeleteCustomerAccountCommand, Result>
{
    public async Task<Result> Handle(DeleteCustomerAccountCommand command, CancellationToken cancellationToken)
    {
        if (await accounts.FindCustomerProfileAsync(command.UserId, cancellationToken) is null)
        {
            return IdentityErrors.UserNotFound();
        }

        var ahead = await upcoming.CountAsync(command.UserId, cancellationToken);
        if (ahead > 0)
        {
            return IdentityErrors.AccountHasUpcomingBookings(ahead);
        }

        await accounts.EraseCustomerAsync(command.UserId, clock.GetUtcNow(), cancellationToken);
        foreach (var eraser in erasers)
        {
            await eraser.EraseAsync(command.UserId, cancellationToken);
        }

        audit.Record(new AuditRecord("customer.account_deleted", "Customer", command.UserId.ToString(), Summary: "Account deleted by the customer"));

        // Saves the erasure, the removed data and the audit entry together with the revoked sessions.
        await sessions.RevokeAllAsync(command.UserId, keep: null, SessionRevocationReason.AccountDeleted, cancellationToken);
        return Result.Success();
    }
}

internal static class CustomerProfiles
{
    public static CustomerProfileResponse ToResponse(CustomerProfileRow profile, IPersonalDataProtector protector, CustomerSignInOptions signIn) =>
        new(
            profile.DisplayName,
            profile.PreferredLocale,
            profile.ContactEmail,
            profile.ProtectedPhone is null ? null : MobileNumber.Mask(protector.Unprotect(profile.ProtectedPhone, PersonalDataPurposes.MobileNumber)),
            PhoneChangeAllowed: !signIn.Enabled);
}
