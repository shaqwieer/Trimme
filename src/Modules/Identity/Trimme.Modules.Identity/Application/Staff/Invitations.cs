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
using Trimme.Modules.Identity.Application.Admin;
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

/// <summary>
/// An admin creates a shop owner or shop staff account directly, with a temporary password and no email (D-139). The
/// user must replace the password before the first session starts.
/// </summary>
internal sealed record CreateShopUserCommand(Guid ShopId, string Email, string DisplayName, string Role, string Password, string Locale)
    : ICommand<Result<ShopUserResponse>>;

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

internal sealed class CreateShopUserValidator : AbstractValidator<CreateShopUserCommand>
{
    public CreateShopUserValidator()
    {
        RuleFor(c => c.ShopId).NotEmpty().WithErrorCode(ValidationCodes.Required);
        RuleFor(c => c.Email).NotEmpty().WithErrorCode(ValidationCodes.Required)
            .EmailAddress().WithErrorCode(ValidationCodes.EmailInvalid)
            .MaximumLength(256).WithErrorCode(ValidationCodes.TooLong);
        RuleFor(c => c.DisplayName).Must(n => !string.IsNullOrWhiteSpace(n)).WithErrorCode(ValidationCodes.Required)
            .MaximumLength(60).WithErrorCode(ValidationCodes.TooLong);
        RuleFor(c => c.Role).NotEmpty().WithErrorCode(ValidationCodes.Required);
        RuleFor(c => c.Password).NotEmpty().WithErrorCode(ValidationCodes.Required)
            .MaximumLength(256).WithErrorCode(ValidationCodes.TooLong);
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

/// <summary>
/// Rules shared by invitations and accounts an admin creates directly (D-139): the role belongs to the user type, a
/// shop user belongs to exactly one existing shop (platform staff to none), and no account uses the email yet. A shop
/// account made here is an owner (D-145): a barber's login comes only from their professional (the barber login
/// endpoints), so it is always linked to one barber and kept to that barber's bookings; the desk role is gone.
/// </summary>
internal sealed class NewStaffAccountRules(TrimmeDbContext db, IAccountStore accounts, IShopDirectory shops)
{
    public async Task<Error?> CheckAsync(string email, UserType userType, string roleName, Guid? shopId, CancellationToken cancellationToken)
    {
        var role = SystemRoles.Find(roleName);
        if ((role is not null && role.UserType != userType)
            || (userType == UserType.ShopUser && !string.Equals(roleName, SystemRoles.ShopOwner, StringComparison.Ordinal))
            || !await accounts.RoleExistsAsync(roleName, userType, cancellationToken))
        {
            return IdentityErrors.RoleNotAssignable();
        }

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

        return await accounts.EmailExistsAsync(email, cancellationToken) ? IdentityErrors.EmailAlreadyRegistered() : null;
    }

    /// <summary>Revokes the address's open invitations (tracked; the caller saves).</summary>
    public async Task RevokePendingInvitationsAsync(string normalizedEmail, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var pending = await db.Set<Invitation>()
            .Where(i => i.NormalizedEmail == normalizedEmail && i.AcceptedAt == null && i.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var previous in pending)
        {
            previous.Revoke(now);
        }
    }
}

/// <summary>Creates invitations and emails them. Shared by admin-staff (now) and shop-user (Phase 05) invitations.</summary>
internal sealed class InvitationIssuer(
    TrimmeDbContext db,
    TimeProvider clock,
    IAccountStore accounts,
    IIdentityMailer mailer,
    IOptions<InvitationOptions> options,
    NewStaffAccountRules rules,
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
        var trimmed = email.Trim();
        if (await rules.CheckAsync(trimmed, userType, roleName, shopId, cancellationToken) is { } refused)
        {
            return refused;
        }

        var now = clock.GetUtcNow();
        var normalizedEmail = accounts.NormalizeEmail(trimmed);

        // Only the newest invitation for an address stays usable.
        await rules.RevokePendingInvitationsAsync(normalizedEmail, now, cancellationToken);

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

/// <summary>
/// Invites a platform-admin colleague with one admin role. The role must be one the inviter could assign directly
/// (D-106): the SuperAdmin role only by a SuperAdmin, and no role with a permission the inviter lacks. Otherwise an
/// admin who may invite staff could invite an address they control with more rights than their own.
/// </summary>
internal sealed class InviteStaffHandler(InvitationIssuer issuer, IAdminAccounts accounts, CallerRights caller, TrimmeDbContext db)
    : ICommandHandler<InviteStaffCommand, Result<InvitationResponse>>
{
    public async Task<Result<InvitationResponse>> Handle(InviteStaffCommand command, CancellationToken cancellationToken)
    {
        if (await accounts.FindRoleByNameAsync(command.Role, cancellationToken) is { UserType: UserType.PlatformAdmin } role
            && await StaffGuards.CheckGrantableAsync(db, caller, [role], cancellationToken) is { } refused)
        {
            return refused;
        }

        return await issuer.IssueAsync(
            command.Email, UserType.PlatformAdmin, command.Role, shopId: null, command.Locale, command.InvitedByUserId, cancellationToken);
    }
}

internal sealed class InviteShopUserHandler(InvitationIssuer issuer) : ICommandHandler<InviteShopUserCommand, Result<InvitationResponse>>
{
    public Task<Result<InvitationResponse>> Handle(InviteShopUserCommand command, CancellationToken cancellationToken) =>
        issuer.IssueAsync(command.Email, UserType.ShopUser, command.Role, command.ShopId, command.Locale, command.InvitedByUserId, cancellationToken);
}

internal sealed class CreateShopUserHandler(
    TrimmeDbContext db,
    TimeProvider clock,
    IAccountStore accounts,
    NewStaffAccountRules rules,
    IAuditLog audit) : ICommandHandler<CreateShopUserCommand, Result<ShopUserResponse>>
{
    public async Task<Result<ShopUserResponse>> Handle(CreateShopUserCommand command, CancellationToken cancellationToken)
    {
        var email = command.Email.Trim();
        if (await rules.CheckAsync(email, UserType.ShopUser, command.Role, command.ShopId, cancellationToken) is { } refused)
        {
            return refused;
        }

        var created = await accounts.CreateStaffAsync(
            new NewStaffAccount(
                email, command.DisplayName.Trim(), command.Password, UserType.ShopUser, command.Role, command.Locale, command.ShopId,
                MustChangePassword: true),
            cancellationToken);
        if (created.IsFailure)
        {
            return created.Error;
        }

        // An invitation emailed earlier to this address can no longer be accepted.
        await rules.RevokePendingInvitationsAsync(accounts.NormalizeEmail(email), clock.GetUtcNow(), cancellationToken);

        // Audited without the email address or the password: the user id links to the account for authorized readers.
        audit.Record(new AuditRecord(
            "shop_user.created",
            "User",
            created.Value.UserId.ToString(),
            new ShopId(command.ShopId),
            $"Created as {command.Role} with a temporary password"));
        await db.SaveChangesAsync(cancellationToken);

        return new ShopUserResponse(created.Value.UserId, email, created.Value.DisplayName ?? string.Empty, command.Role);
    }
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
