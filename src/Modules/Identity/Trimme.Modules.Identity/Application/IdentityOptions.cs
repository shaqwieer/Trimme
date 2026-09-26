namespace Trimme.Modules.Identity.Application;

/// <summary>Session lifetimes (D-027). Bound from <c>Identity:Sessions</c>.</summary>
public sealed class SessionOptions
{
    public const string SectionName = "Identity:Sessions";

    /// <summary>Lifetime of the access cookie. Short, because it is only re-issued by a refresh.</summary>
    public TimeSpan AccessLifetime { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan CustomerSessionLifetime { get; set; } = TimeSpan.FromDays(30);

    public TimeSpan StaffSessionLifetime { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// A consumed refresh token presented again within this window is treated as a concurrent refresh from another
    /// tab (409, nothing revoked). After it, reuse revokes the whole session family.
    /// </summary>
    public TimeSpan RefreshReuseGrace { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>Staff invitation settings. Bound from <c>Identity:Invitations</c>.</summary>
public sealed class InvitationOptions
{
    public const string SectionName = "Identity:Invitations";

    public TimeSpan Lifetime { get; set; } = TimeSpan.FromDays(7);
}

/// <summary>Public web origin used in emailed links. Bound from <c>Web</c>.</summary>
public sealed class WebLinkOptions
{
    public const string SectionName = "Web";

    public string PublicBaseUrl { get; set; } = "http://localhost:3000";
}
