using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.BuildingBlocks.Application.Tenancy;

/// <summary>
/// The tenant of the current operation, resolved server-side from the authenticated shop user's claims and the shop's
/// current status — never from a route, query or body value (R-TEN-01). Read lazily, at query time.
/// </summary>
public interface ICurrentTenant
{
    /// <summary>The shop whose rows the caller may see, or <see langword="null"/> (anonymous, customer, admin, suspended shop).</summary>
    ShopId? ShopId { get; }
}

/// <summary>
/// Explicit, isolated bypass of the tenant filter for platform administrators (R-TEN-05). Only admin use cases
/// (<c>*.Application.Admin</c> namespaces, enforced by an architecture test) may depend on it, and it throws unless the
/// caller is a PlatformAdmin. Dispose the returned scope to restore isolation.
/// </summary>
public interface IAdminDataScope
{
    IDisposable Begin();
}

/// <summary>
/// Tenant bypass for work with no user: host commands (<c>migrate</c>, <c>seed</c>) and background jobs. It throws when
/// called inside an HTTP request, and only seeding, hosting and job code may depend on it (architecture test).
/// </summary>
public interface ISystemDataScope
{
    IDisposable Begin();
}

/// <summary>Read access to shops for other modules (implemented by the Shops module).</summary>
public interface IShopDirectory
{
    Task<ShopSummary?> FindAsync(ShopId shopId, CancellationToken cancellationToken);
}

public sealed record ShopSummary(ShopId Id, string Slug, string NameAr, string NameEn, ShopStatus Status);
