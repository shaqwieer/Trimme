namespace Trimme.Modules.Identity.Application;

/// <summary>Session lifetimes (D-027). Bound from <c>Identity:Sessions</c>.</summary>
public sealed class SessionOptions
{
    public const string SectionName = "Identity:Sessions";

    /// <summary>Lifetime of the access cookie. Short, because it is only re-issued by a refresh.</summary>
    public TimeSpan AccessLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// A customer signs in with a code sent to their phone, and each code is a paid message, so a customer stays signed in
    /// for a year (D-142; was 30 days in D-052). Absolute: a refresh never extends it.
    /// </summary>
    public TimeSpan CustomerSessionLifetime { get; set; } = TimeSpan.FromDays(365);

    /// <summary>Shop owners and desk staff stay signed in for a year too, at the user's request (D-142; was 7 days).</summary>
    public TimeSpan ShopUserSessionLifetime { get; set; } = TimeSpan.FromDays(365);

    /// <summary>The platform's own admins: kept short, because their accounts reach every shop.</summary>
    public TimeSpan StaffSessionLifetime { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// A consumed refresh token presented again within this window is treated as a concurrent refresh from another
    /// tab (409, nothing revoked). After it, reuse revokes the whole session family.
    /// </summary>
    public TimeSpan RefreshReuseGrace { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// Whether a customer must confirm their mobile number with a one-time code (D-143). Bound from <c>Identity:Otp</c>,
/// the section that also holds the code's sender (<c>Identity:Otp:Sender</c>).
/// </summary>
public sealed class CustomerSignInOptions
{
    public const string SectionName = "Identity:Otp";

    /// <summary>
    /// Off by default in every environment (the user's choice, D-143): customers sign up and sign in with their number
    /// alone, and the code endpoints refuse. Turn it on (<c>Identity__Otp__Enabled=true</c>) only together with a
    /// working sender: the WhatsApp authentication template once Meta approves it, or an SMS provider.
    /// </summary>
    public bool Enabled { get; set; }
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
