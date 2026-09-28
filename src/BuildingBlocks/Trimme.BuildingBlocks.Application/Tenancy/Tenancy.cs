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

/// <summary>
/// Read-only view of published data for anonymous and customer pages (D-066): every shop is visible, and shop-owned rows
/// only of the one shop given (none when <see langword="null"/>). The caller's own tenant is ignored while it is open,
/// and saving changes throws. Only public use cases (<c>*.Application.Public</c> namespaces, enforced by an
/// architecture test) may depend on it. Handlers still filter what is published (for example active shops only).
/// </summary>
public interface IPublicDataScope
{
    IDisposable Begin(ShopId? shopId);
}

/// <summary>Read access to shops for other modules (implemented by the Shops module).</summary>
public interface IShopDirectory
{
    Task<ShopSummary?> FindAsync(ShopId shopId, CancellationToken cancellationToken);

    Task<ShopSummary?> FindBySlugAsync(string slug, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<ShopId, ShopSummary>> FindManyAsync(IReadOnlyCollection<ShopId> shopIds, CancellationToken cancellationToken);

    /// <summary>Shops whose Arabic or English name or slug contains <paramref name="term"/> (case-insensitive), at most <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<ShopId>> SearchIdsAsync(string term, int limit, CancellationToken cancellationToken);

    /// <summary>Every shop the caller may see (all of them for admins).</summary>
    Task<int> CountAsync(CancellationToken cancellationToken);
}

/// <summary>A shop as other modules see it.</summary>
/// <param name="Id">The shop (tenant) id.</param>
/// <param name="Slug">URL identifier of the public shop page.</param>
/// <param name="NameAr">Arabic name.</param>
/// <param name="NameEn">English name.</param>
/// <param name="Status">Lifecycle status.</param>
/// <param name="TimeZone">IANA time zone of the shop's opening hours and availability (D-030).</param>
/// <param name="OnlineBookingPausedAt">When the shop paused online booking; <see langword="null"/> while it is live (D-013).</param>
public sealed record ShopSummary(ShopId Id, string Slug, string NameAr, string NameEn, ShopStatus Status, string TimeZone, DateTimeOffset? OnlineBookingPausedAt)
{
    public bool OnlineBookingPaused => OnlineBookingPausedAt is not null;
}
