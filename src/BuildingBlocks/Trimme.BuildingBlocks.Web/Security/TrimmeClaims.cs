namespace Trimme.BuildingBlocks.Web.Security;

/// <summary>Claim types carried by the access cookie. Kept minimal: roles and permissions are resolved per request.</summary>
public static class TrimmeClaims
{
    public const string Subject = "sub";
    public const string SessionId = "sid";
    public const string UserType = "user_type";

    /// <summary>The shop of a shop user, written from the database at sign-in (never client-supplied).</summary>
    public const string ShopId = "shop_id";

    /// <summary>Authentication scheme of the short-lived access cookie (D-027).</summary>
    public const string AuthenticationScheme = "trimme-session";
}

/// <summary>The three authenticated user types (spec §2). SuperAdmin is a role inside <see cref="PlatformAdmin"/>.</summary>
public static class UserTypes
{
    public const string Customer = "Customer";
    public const string ShopUser = "ShopUser";
    public const string PlatformAdmin = "PlatformAdmin";
}
