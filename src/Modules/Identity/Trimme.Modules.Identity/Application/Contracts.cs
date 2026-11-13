namespace Trimme.Modules.Identity.Application;

/// <summary>
/// The signed-in user (<c>GET /api/v1/me</c>). <c>UserType</c> is <c>Customer</c>, <c>ShopUser</c> or
/// <c>PlatformAdmin</c>. A customer sees only their own number, masked. <c>ShopId</c> is set for shop users from
/// Phase 05. <c>ProfileComplete</c> is false until a customer has given a name and accepted the terms.
/// </summary>
public sealed record MeResponse(
    Guid Id,
    string UserType,
    string? DisplayName,
    string? Email,
    string? PhoneMasked,
    string PreferredLocale,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    Guid? ShopId,
    bool ProfileComplete);

/// <summary>
/// How customers sign in (D-143). <c>OtpRequired</c> true: a code to the number (<c>otp/request</c>, <c>otp/verify</c>);
/// false (the default): the number alone (<c>phone/sign-in</c>).
/// </summary>
public sealed record CustomerSignInOptionsResponse(bool OtpRequired);

/// <summary>Response to an OTP request. The code is never returned.</summary>
public sealed record OtpChallengeResponse(Guid ChallengeId, int CodeLength, DateTimeOffset ExpiresAt, DateTimeOffset ResendAvailableAt);

/// <summary>
/// Result of a successful sign-in; the session cookies are set on the response. <c>IsNewUser</c> is true when the OTP
/// verification (or an invitation) created the account.
/// </summary>
public sealed record SignInResponse(bool IsNewUser, MeResponse User);

public sealed record SessionResponse(Guid Id, string DeviceLabel, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt, bool IsCurrent);

public sealed record PermissionResponse(string Code, string Scope, string UserType);

public sealed record RoleResponse(Guid Id, string Name, string UserType, bool Managed, IReadOnlyList<string> Permissions);

public sealed record InvitationResponse(Guid Id, string Email, string Role, DateTimeOffset ExpiresAt);

/// <summary>A shop account an admin created with a temporary password (D-139). The password is never returned.</summary>
public sealed record ShopUserResponse(Guid Id, string Email, string DisplayName, string Role);

/// <summary>A new session: the plaintext refresh token goes into the refresh cookie and is never stored.</summary>
/// <remarks><c>ShopId</c> comes from the account row (never the request) and becomes the <c>shop_id</c> claim.</remarks>
public sealed record IssuedSession(Guid SessionId, Guid UserId, string UserType, string RefreshToken, DateTimeOffset ExpiresAt, Guid? ShopId = null);

/// <summary>What a sign-in use case hands to the API layer, which writes the cookies.</summary>
public sealed record SignInOutcome(IssuedSession Session, SignInResponse Response);
