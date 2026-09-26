using Trimme.BuildingBlocks.Domain.Primitives;

namespace Trimme.Modules.Identity.Domain;

public readonly record struct UserSessionId(Guid Value) : IEntityId<UserSessionId>
{
    public static UserSessionId From(Guid value) => new(value);
}

public readonly record struct RefreshTokenId(Guid Value) : IEntityId<RefreshTokenId>
{
    public static RefreshTokenId From(Guid value) => new(value);
}

public enum SessionRevocationReason
{
    SignedOut,
    RevokedByUser,
    RevokedAllByUser,
    RefreshTokenReuse,
    PasswordReset,
}

/// <summary>
/// A signed-in device: one refresh-token family (D-027). The short-lived access cookie names the session, and every
/// request checks that the session is still active, so revocation takes effect immediately.
/// </summary>
public sealed class UserSession : AggregateRoot<UserSessionId>
{
    public UserSession(UserSessionId id, Guid userId, UserType userType, DateTimeOffset now, DateTimeOffset expiresAt, string deviceLabel, string? ipHash)
        : base(id)
    {
        UserId = userId;
        UserType = userType;
        CreatedAt = now;
        LastSeenAt = now;
        ExpiresAt = expiresAt;
        DeviceLabel = deviceLabel;
        IpHash = ipHash;
    }

    private UserSession()
    {
        DeviceLabel = string.Empty;
    }

    public Guid UserId { get; private set; }

    public UserType UserType { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset LastSeenAt { get; private set; }

    /// <summary>Absolute end of the session; refreshing never extends it.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public SessionRevocationReason? RevocationReason { get; private set; }

    /// <summary>Coarse browser and OS label derived from the user agent, e.g. "Chrome · Windows".</summary>
    public string DeviceLabel { get; private set; }

    /// <summary>Keyed hash of the client IP address; the address itself is never stored.</summary>
    public string? IpHash { get; private set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public void Touch(DateTimeOffset now, string? ipHash)
    {
        LastSeenAt = now;
        IpHash = ipHash ?? IpHash;
    }

    public void Revoke(SessionRevocationReason reason, DateTimeOffset now)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = now;
        RevocationReason = reason;
    }
}

/// <summary>
/// One refresh token of a session. Only its SHA-256 hash is stored. A token is consumed exactly once when it is
/// rotated; presenting a consumed token again (outside a short concurrency grace window) revokes the whole family.
/// </summary>
public sealed class RefreshToken : Entity<RefreshTokenId>
{
    public RefreshToken(RefreshTokenId id, UserSessionId sessionId, string tokenHash, DateTimeOffset now)
        : base(id)
    {
        SessionId = sessionId;
        TokenHash = tokenHash;
        CreatedAt = now;
    }

    private RefreshToken()
    {
        TokenHash = string.Empty;
    }

    public UserSessionId SessionId { get; private set; }

    public string TokenHash { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ConsumedAt { get; private set; }
}
