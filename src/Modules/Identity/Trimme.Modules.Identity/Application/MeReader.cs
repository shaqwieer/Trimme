using Trimme.BuildingBlocks.Application.Privacy;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.Modules.Identity.Domain;

namespace Trimme.Modules.Identity.Application;

/// <summary>Builds <see cref="MeResponse"/>: account, roles and the permissions those roles grant.</summary>
internal sealed class MeReader(IAccountStore accounts, IPermissionResolver permissions, IPersonalDataProtector protector)
{
    public async Task<MeResponse?> ReadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var account = await accounts.FindAsync(userId, cancellationToken);
        return account is null ? null : await BuildAsync(account, cancellationToken);
    }

    public async Task<MeResponse> BuildAsync(AccountSummary account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        var granted = await permissions.GetPermissionsAsync(account.UserId, cancellationToken);
        var phoneMasked = account.ProtectedPhone is null
            ? null
            : MobileNumber.Mask(protector.Unprotect(account.ProtectedPhone, PersonalDataPurposes.MobileNumber));

        return new MeResponse(
            account.UserId,
            account.UserType.ToString(),
            account.DisplayName,
            account.Email,
            phoneMasked,
            account.PreferredLocale,
            account.Roles,
            [.. granted.Order(StringComparer.Ordinal)],
            ShopId: null,
            ProfileComplete: IsProfileComplete(account));
    }

    public static bool IsProfileComplete(AccountSummary account) =>
        account.UserType != UserType.Customer
        || (!string.IsNullOrWhiteSpace(account.DisplayName) && account.TermsAcceptedAt is not null);
}
