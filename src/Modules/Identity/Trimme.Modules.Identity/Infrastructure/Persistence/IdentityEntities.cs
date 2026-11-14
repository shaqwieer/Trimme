using Microsoft.AspNetCore.Identity;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.Modules.Identity.Domain;

namespace Trimme.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// ASP.NET Core Identity user for all three user types. Customers have no email or password: they are identified by
/// a verified mobile, stored only encrypted (<see cref="ProtectedPhone"/>) plus a keyed lookup hash
/// (<see cref="PhoneLookupHash"/>). Identity's plaintext <c>PhoneNumber</c> column is deliberately not mapped.
/// </summary>
internal sealed class ApplicationUser : IdentityUser<Guid>, ITenantMember
{
    /// <summary>
    /// The one shop a shop user belongs to (D-059). Set when the account is created from a shop invitation and never
    /// changed afterwards (the context rejects changes); a foreign key to the shop is added by convention.
    /// </summary>
    public ShopId? ShopId { get; set; }

    public UserType UserType { get; set; }

    public string? DisplayName { get; set; }

    public string PreferredLocale { get; set; } = "ar";

    public string? PhoneLookupHash { get; set; }

    public string? ProtectedPhone { get; set; }

    public DateTimeOffset? TermsAcceptedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Set by an admin to block sign-in and end every session (admin UI in Phase 14).</summary>
    public DateTimeOffset? DisabledAt { get; set; }

    /// <summary>
    /// The password is a temporary one an admin set (D-139): sign-in starts no session until the user replaces it.
    /// Cleared by the change or by an emailed password reset.
    /// </summary>
    public bool MustChangePassword { get; set; }

    /// <summary>
    /// A customer's own email (D-144), for their records only: kept apart from Identity's <c>Email</c>, which staff
    /// sign-in, password resets and invitations key on, so a customer can never claim a staff address.
    /// </summary>
    public string? ContactEmail { get; set; }

    /// <summary>
    /// The customer deleted their account (D-144): name, number and email are erased, sign-in is closed for good and
    /// the number can register again as a new account. Bookings and reviews keep their own records.
    /// </summary>
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>A role belongs to exactly one user type; only permissions of that type can be granted to it.</summary>
internal sealed class ApplicationRole : IdentityRole<Guid>
{
    public UserType UserType { get; set; }
}
