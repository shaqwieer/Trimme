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

    /// <summary>
    /// The active professionals of these shops with their public profile, in name order (discovery, D-091). Opened with
    /// <c>IPublicDataScope.BeginMany</c> for the shops. Never carries a phone or WhatsApp number (R-PRO-02).
    /// </summary>
    Task<IReadOnlyList<PublicProfessionalCard>> ListActiveProfilesAsync(IReadOnlyCollection<ShopId> shopIds, CancellationToken cancellationToken);
}

public sealed record ProfessionalSummary(ProfessionalId Id, ShopId ShopId, string NameAr, string NameEn, bool IsActive);

/// <summary>An active professional's public profile, as discovery lists them.</summary>
public sealed record PublicProfessionalCard(
    ProfessionalId Id, ShopId ShopId, string Slug, string NameAr, string NameEn, string? SpecialtyAr, string? SpecialtyEn, string? AvatarUrl);

/// <summary>
/// Customers for other modules (implemented by the Identity module). It exposes the display name only: the customer's
/// mobile number never leaves the Identity module except to authorized admin commands and notifications (spec §7).
/// </summary>
public interface ICustomerDirectory
{
    Task<CustomerSummary?> FindAsync(Guid customerId, CancellationToken cancellationToken);
}

/// <summary>A customer as other modules see it.</summary>
/// <param name="Id">The customer's user id.</param>
/// <param name="DisplayName">Null until the customer completes their profile.</param>
/// <param name="PreferredLocale"><c>ar</c> or <c>en</c>.</param>
/// <param name="IsActive">False once the account is disabled.</param>
public sealed record CustomerSummary(Guid Id, string? DisplayName, string PreferredLocale, bool IsActive);

/// <summary>
/// Whether something still references a shop service, so it may not be deleted (R-SVC-02). Each module that references
/// services registers one: Services (package items) now, Bookings from Phase 10. Deletion checks them all.
/// </summary>
public interface IShopServiceUsage
{
    Task<bool> IsInUseAsync(ShopId shopId, Guid serviceId, CancellationToken cancellationToken);
}

/// <summary>
/// Display names of accounts by id, for admin views such as the audit log (implemented by the Identity module). Names
/// and user types only: never an email address or a phone number.
/// </summary>
public interface IUserNameLookup
{
    Task<IReadOnlyDictionary<Guid, UserNameEntry>> FindAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);
}

/// <summary>An account's display name and user type (<c>Customer</c>, <c>ShopUser</c> or <c>PlatformAdmin</c>).</summary>
public sealed record UserNameEntry(Guid Id, string? DisplayName, string UserType);
