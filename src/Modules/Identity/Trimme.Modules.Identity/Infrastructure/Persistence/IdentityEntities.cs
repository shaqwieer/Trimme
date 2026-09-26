using Microsoft.AspNetCore.Identity;
using Trimme.Modules.Identity.Domain;

namespace Trimme.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// ASP.NET Core Identity user for all three user types. Customers have no email or password: they are identified by
/// a verified mobile, stored only encrypted (<see cref="ProtectedPhone"/>) plus a keyed lookup hash
/// (<see cref="PhoneLookupHash"/>). Identity's plaintext <c>PhoneNumber</c> column is deliberately not mapped.
/// </summary>
internal sealed class ApplicationUser : IdentityUser<Guid>
{
    public UserType UserType { get; set; }

    public string? DisplayName { get; set; }

    public string PreferredLocale { get; set; } = "ar";

    public string? PhoneLookupHash { get; set; }

    public string? ProtectedPhone { get; set; }

    public DateTimeOffset? TermsAcceptedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Set by an admin to block sign-in and end every session (admin UI in Phase 14).</summary>
    public DateTimeOffset? DisabledAt { get; set; }
}

/// <summary>A role belongs to exactly one user type; only permissions of that type can be granted to it.</summary>
internal sealed class ApplicationRole : IdentityRole<Guid>
{
    public UserType UserType { get; set; }
}
