using System.Globalization;
using Trimme.BuildingBlocks.Application.Auditing;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Notifications;
using Trimme.BuildingBlocks.Application.Privacy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.Modules.Identity.Application.Sessions;
using Trimme.Modules.Identity.Domain;

namespace Trimme.Modules.Identity.Application.Staff;

// A shop user's own «حسابي» (D-151, the client's 2-2: the name, the mobile, the calendar, the language of the shop's pages,
// notifications, signing out and deleting the account). The calendar and the language are the web app's; the server keeps
// the names, shows the number masked, and deletes a barber's login.

/// <summary>
/// The signed-in shop user's account (D-151). For a barber: their names as the shop shows them (<c>NameAr</c>,
/// <c>NameEn</c>, with the <c>Version</c> to rename with) and the WhatsApp number they sign in with, masked. For the owner:
/// their display name and email. <c>CanRename</c> and <c>CanDelete</c>: barbers only.
/// </summary>
public sealed record ShopAccountResponse(
    string? DisplayName,
    string? Email,
    string? MobileMasked,
    Guid? ProfessionalId,
    string? NameAr,
    string? NameEn,
    uint? Version,
    bool CanRename,
    bool CanDelete);

internal sealed record GetShopAccountQuery(Guid UserId, Guid? ProfessionalId) : IQuery<ShopAccountResponse?>;

internal sealed record DeleteBarberAccountCommand(Guid UserId, Guid? ProfessionalId) : ICommand<Result>;

internal static class ShopAccountErrors
{
    public static Error BarbersOnly() =>
        Error.Forbidden("account.barbers_only", "Only a barber's login can do this; the shop's owner account is managed by the platform.");
}

internal sealed class GetShopAccountHandler(IAccountStore accounts, IBarberAccountProfiles barbers, IPersonalDataProtector protector)
    : IQueryHandler<GetShopAccountQuery, ShopAccountResponse?>
{
    public async Task<ShopAccountResponse?> Handle(GetShopAccountQuery query, CancellationToken cancellationToken)
    {
        if (await accounts.FindAsync(query.UserId, cancellationToken) is not { UserType: UserType.ShopUser } account)
        {
            return null;
        }

        if (query.ProfessionalId is { } professionalId
            && await barbers.FindAsync(new ProfessionalId(professionalId), cancellationToken) is { } barber)
        {
            return new ShopAccountResponse(
                account.DisplayName, account.Email, barber.MobileMasked, professionalId, barber.NameAr, barber.NameEn, barber.Version, CanRename: true, CanDelete: true);
        }

        var masked = account.ProtectedPhone is null ? null : MobileNumber.Mask(protector.Unprotect(account.ProtectedPhone, PersonalDataPurposes.MobileNumber));
        return new ShopAccountResponse(account.DisplayName, account.Email, masked, null, null, null, null, CanRename: false, CanDelete: false);
    }
}

/// <summary>
/// A barber deletes their own login (D-151, «حذف الحساب»; the user's answer: the login only). The login's name and number
/// hash are erased, it is closed for good and unlinked from the professional; every session ends. The barber stays in
/// the shop with their bookings, history and schedule; the owner is told in-app and can give them a new login from
/// «الحلاقون». The owner's account is the platform's to manage (403).
/// </summary>
internal sealed class DeleteBarberAccountHandler(
    IAccountStore accounts,
    IBarberAccountProfiles barbers,
    SessionManager sessions,
    INotificationCenter notifications,
    IAuditLog audit,
    TimeProvider clock) : ICommandHandler<DeleteBarberAccountCommand, Result>
{
    /// <summary>The owner's in-app notice: «حذف الحلاق … حسابه» with the barber's name and a link to the barber.</summary>
    public const string OwnerNoticeKind = "barber.account_deleted";

    public async Task<Result> Handle(DeleteBarberAccountCommand command, CancellationToken cancellationToken)
    {
        if (command.ProfessionalId is not { } professionalId)
        {
            return ShopAccountErrors.BarbersOnly();
        }

        if (await accounts.FindAsync(command.UserId, cancellationToken) is not { UserType: UserType.ShopUser, ProfessionalId: not null })
        {
            return IdentityErrors.UserNotFound();
        }

        var barber = await barbers.FindAsync(new ProfessionalId(professionalId), cancellationToken);
        var now = clock.GetUtcNow();
        await accounts.EraseBarberLoginAsync(command.UserId, now, cancellationToken);
        audit.Record(new AuditRecord(
            "barber_login.deleted", "Professional", professionalId.ToString(), barber?.ShopId, "Login deleted by the barber"));
        if (barber is not null)
        {
            await notifications.NotifyShopAsync(barber.ShopId, new InAppNotice(
                OwnerNoticeKind,
                string.Create(CultureInfo.InvariantCulture, $"barber-account-deleted:{command.UserId}"),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["name"] = barber.NameAr,
                    ["professionalId"] = professionalId.ToString(),
                }), cancellationToken);
        }

        // Saves the erasure, the audit entry and the owner's notice together with the revoked sessions.
        await sessions.RevokeAllAsync(command.UserId, keep: null, SessionRevocationReason.AccountDeleted, cancellationToken);
        await notifications.PushPendingAsync(cancellationToken);
        return Result.Success();
    }
}
