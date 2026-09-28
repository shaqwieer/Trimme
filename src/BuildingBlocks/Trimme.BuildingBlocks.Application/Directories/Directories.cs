using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.BuildingBlocks.Application.Directories;

/// <summary>
/// Read access to professionals for other modules (implemented by the Professionals module). It reads through the
/// caller's data scope: an admin use case opens <c>IAdminDataScope</c> first, a shop user sees only their own shop's.
/// </summary>
public interface IProfessionalDirectory
{
    Task<ProfessionalSummary?> FindAsync(ProfessionalId professionalId, CancellationToken cancellationToken);

    /// <summary>The shop's professionals the caller may see, active and disabled, in name order.</summary>
    Task<IReadOnlyList<ProfessionalSummary>> ListByShopAsync(ShopId shopId, CancellationToken cancellationToken);
}

public sealed record ProfessionalSummary(ProfessionalId Id, ShopId ShopId, string NameAr, string NameEn, bool IsActive);

/// <summary>
/// Whether something still references a shop service, so it may not be deleted (R-SVC-02). Each module that references
/// services registers one: Services (package items) now, Bookings from Phase 10. Deletion checks them all.
/// </summary>
public interface IShopServiceUsage
{
    Task<bool> IsInUseAsync(ShopId shopId, Guid serviceId, CancellationToken cancellationToken);
}
