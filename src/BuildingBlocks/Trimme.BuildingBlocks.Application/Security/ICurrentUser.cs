namespace Trimme.BuildingBlocks.Application.Security;

/// <summary>
/// The authenticated caller, resolved from the request claims and never from client-supplied identifiers.
/// Implemented from the session cookie (Phase 04). Shop tenancy (<c>ShopId</c>) is added in Phase 05.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid? UserId { get; }

    /// <summary><c>Customer</c>, <c>ShopUser</c> or <c>PlatformAdmin</c>; <see langword="null"/> when anonymous.</summary>
    string? UserType { get; }

    /// <summary>The server-side session (refresh-token family) behind the current access cookie.</summary>
    Guid? SessionId { get; }

    /// <summary>
    /// The shop a shop user belongs to, whatever its status. Use <c>ICurrentTenant</c> for data access: it is empty while
    /// the shop is suspended.
    /// </summary>
    Guid? ShopId { get; }
}
