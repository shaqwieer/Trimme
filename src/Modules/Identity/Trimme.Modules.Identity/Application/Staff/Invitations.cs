using System.Security.Cryptography;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Trimme.BuildingBlocks.Application.Auditing;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Identity.Application.Sessions;
using Trimme.Modules.Identity.Domain;

namespace Trimme.Modules.Identity.Application.Staff;

/// <summary>
/// An admin invites a platform-admin colleague by email. Shop-user invitations reuse <see cref="InvitationIssuer"/>
/// from Phase 05, once shops exist.
/// </summary>
internal sealed record InviteStaffCommand(string Email, string Role, string Locale, Guid InvitedByUserId)
    : ICommand<Result<InvitationResponse>>;

/// <summary>An admin invites a shop owner or shop staff member to one shop (R-AUTH-02).</summary>
internal sealed record InviteShopUserCommand(Guid ShopId, string Email, string Role, string Locale, Guid InvitedByUserId)
    : ICommand<Result<InvitationResponse>>;

/// <summary>The invitee sets a name and password; the account is created and signed in.</summary>
internal sealed record AcceptInvitationCommand(string Token, string DisplayName, string Password, ClientContext Client)
    : ICommand<Result<SignInOutcome>>;

internal sealed class InviteStaffValidator : AbstractValidator<InviteStaffCommand>
{
    public InviteStaffValidator()
    {
        RuleFor(c => c.Email).NotEmpty().WithErrorCode(ValidationCodes.Required)
            .EmailAddress().WithErrorCode(ValidationCodes.EmailInvalid)
            .MaximumLength(256).WithErrorCode(ValidationCodes.TooLong);
        RuleFor(c => c.Role).NotEmpty().WithErrorCode(ValidationCodes.Required);
        RuleFor(c => c.Locale).Must(Locales.IsSupported).WithErrorCode(ValidationCodes.Invalid);
    }
}

internal sealed class InviteShopUserValidator : AbstractValidator<InviteShopUserCommand>
{
    public InviteShopUserValidator()
    {
        RuleFor(c => c.ShopId).NotEmpty().WithErrorCode(ValidationCodes.Required);
        RuleFor(c => c.Email).NotEmpty().WithErrorCode(ValidationCodes.Required)
            .EmailAddress().WithErrorCode(ValidationCodes.EmailInvalid)
            .MaximumLength(256).WithErrorCode(ValidationCodes.TooLong);
        RuleFor(c => c.Role).NotEmpty().WithErrorCode(ValidationCodes.Required);
        RuleFor(c => c.Locale).Must(Locales.IsSupported).WithErrorCode(ValidationCodes.Invalid);
    }
}

internal sealed class AcceptInvitationValidator : AbstractValidator<AcceptInvitationCommand>
{
    public AcceptInvitationValidator()
    {
        RuleFor(c => c.Token).NotEmpty().WithErrorCode(ValidationCodes.Required)
            .MaximumLength(256).WithErrorCode(ValidationCodes.TooLong);
        RuleFor(c => c.DisplayName).Must(n => !string.IsNullOrWhiteSpace(n)).WithErrorCode(ValidationCodes.Required)
            .MaximumLength(60).WithErrorCode(ValidationCodes.TooLong);
        RuleFor(c => c.Password).NotEmpty().WithErrorCode(ValidationCodes.Required)
            .MaximumLength(256).WithErrorCode(ValidationCodes.TooLong);
    }
}

/// <summary>Creates invitations and emails them. Shared by admin-staff (now) and shop-user (Phase 05) invitations.</summary>
internal sealed class InvitationIssuer(
    TrimmeDbContext db,
    TimeProvider clock,
    IAccountStore accounts,
    IIdentityMailer mailer,
    IOptions<InvitationOptions> options,
    IShopDirectory shops,
    IAuditLog audit)
{
    public async Task<Result<InvitationResponse>> IssueAsync(
        string email,
        UserType userType,
        string roleName,
        Guid? shopId,
        string locale,
        Guid invitedByUserId,
        CancellationToken cancellationToken)
    {
        var role = SystemRoles.Find(roleName);
        if ((role is not null && role.UserType != userType)
            || !await accounts.RoleExistsAsync(roleName, userType, cancellationToken))
        {
            return IdentityErrors.RoleNotAssignable();
        }

        // A shop user belongs to exactly one existing shop; platform staff belong to none.
        if (userType == UserType.ShopUser)
        {
            if (shopId is not { } target || await shops.FindAsync(new ShopId(target), cancellationToken) is null)
            {
                return IdentityErrors.ShopNotFound();
            }
        }
        else if (shopId is not null)
        {
            return IdentityErrors.RoleNotAssignable();
        }

        var trimmed = email.Trim();
        if (await accounts.EmailExistsAsync(trimmed, cancellationToken))
        {
            return IdentityErrors.EmailAlreadyRegistered();
        }

        var now = clock.GetUtcNow();
        var normalizedEmail = accounts.NormalizeEmail(trimmed);

        // Only the newest invitation for an address stays usable.
        var pending = await db.Set<Invitation>()
            .Where(i => i.NormalizedEmail == normalizedEmail && i.AcceptedAt == null && i.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var previous in pending)
        {
            previous.Revoke(now);
        }

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var invitation = new Invitation(
            EntityId.New<InvitationId>(),
            trimmed,
            normalizedEmail,
            userType,
            roleName,
            shopId is { } invitedShop ? new ShopId(invitedShop) : null,
            SessionManager.HashToken(token),
            locale,
            invitedByUserId,
            now,
            options.Value.Lifetime);

        db.Add(invitation);

        // Audited without the email address (PII): the invitation id links to it for authorized readers.
        audit.Record(new AuditRecord(
            userType == UserType.ShopUser ? "shop_user.invited" : "staff.invited",
            nameof(Invitation),
            invitation.Id.Value.ToString(),
            shopId is { } auditedShop ? new ShopId(auditedShop) : null,
            $"Invited as {roleName}"));
        await db.SaveChangesAsync(cancellationToken);
        await mailer.SendInvitationAsync(trimmed, locale, roleName, token, invitation.ExpiresAt, cancellationToken);

        return new InvitationResponse(invitation.Id.Value, trimmed, roleName, invitation.ExpiresAt);
    }
}

internal sealed class InviteStaffHandler(InvitationIssuer issuer) : ICommandHandler<InviteStaffCommand, Result<InvitationResponse>>
{
    public Task<Result<InvitationResponse>> Handle(InviteStaffCommand command, CancellationToken cancellationToken) =>
        issuer.IssueAsync(command.Email, UserType.PlatformAdmin, command.Role, shopId: null, command.Locale, command.InvitedByUserId, cancellationToken);
}

internal sealed class InviteShopUserHandler(InvitationIssuer issuer) : ICommandHandler<InviteShopUserCommand, Result<InvitationResponse>>
{
    public Task<Result<InvitationResponse>> Handle(InviteShopUserCommand command, CancellationToken cancellationToken) =>
        issuer.IssueAsync(command.Email, UserType.ShopUser, command.Role, command.ShopId, command.Locale, command.InvitedByUserId, cancellationToken);
}

internal sealed class AcceptInvitationHandler(
    TrimmeDbContext db,
    TimeProvider clock,
    IAccountStore accounts,
    SessionManager sessions,
    MeReader me) : ICommandHandler<AcceptInvitationCommand, Result<SignInOutcome>>
{
    public async Task<Result<SignInOutcome>> Handle(AcceptInvitationCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var tokenHash = SessionManager.HashToken(command.Token);
        var invitation = await db.Set<Invitation>().SingleOrDefaultAsync(i => i.TokenHash == tokenHash, cancellationToken);
        if (invitation is null || !invitation.IsPending(now))
        {
            return IdentityErrors.InvitationInvalid();
        }

        var created = await accounts.CreateStaffAsync(
            new NewStaffAccount(invitation.Email, command.DisplayName.Trim(), command.Password, invitation.UserType, invitation.RoleName, invitation.Locale, invitation.ShopId?.Value),
            cancellationToken);
        if (created.IsFailure)
        {
            return created.Error;
        }

        invitation.Accept(created.Value.UserId, now);
        await db.SaveChangesAsync(cancellationToken);

        var session = await sessions.StartAsync(created.Value, command.Client, cancellationToken);
        return new SignInOutcome(session, new SignInResponse(true, await me.BuildAsync(created.Value, cancellationToken)));
    }
}
