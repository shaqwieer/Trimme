using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Trimme.BuildingBlocks.Application.Privacy;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Identity.Domain;

namespace Trimme.Modules.Identity.Application.Sessions;

/// <summary>
/// Server-side sessions with rotating refresh tokens (D-027, R-AUTH-04/05). Each session is one token family:
/// a refresh consumes the presented token exactly once and issues its successor; replaying a consumed token after the
/// grace window revokes the family.
/// </summary>
internal sealed class SessionManager(
    TrimmeDbContext db,
    TimeProvider clock,
    IOptions<SessionOptions> options,
    IPersonalDataProtector protector,
    IAccountStore accounts)
{
    private readonly SessionOptions _options = options.Value;

    public async Task<IssuedSession> StartAsync(Guid userId, UserType userType, ClientContext client, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var lifetime = userType == UserType.Customer ? _options.CustomerSessionLifetime : _options.StaffSessionLifetime;
        var session = new UserSession(
            EntityId.New<UserSessionId>(), userId, userType, now, now + lifetime, DeviceLabels.From(client.UserAgent), HashIp(client.IpAddress));
        var (token, tokenHash) = NewToken();

        db.Add(session);
        db.Add(new RefreshToken(EntityId.New<RefreshTokenId>(), session.Id, tokenHash, now));
        await db.SaveChangesAsync(cancellationToken);

        return new IssuedSession(session.Id.Value, userId, userType.ToString(), token, session.ExpiresAt);
    }

    public async Task<Result<IssuedSession>> RotateAsync(string refreshToken, ClientContext client, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var tokenHash = HashToken(refreshToken);

        var token = await db.Set<RefreshToken>().AsNoTracking()
            .SingleOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);
        if (token is null)
        {
            return IdentityErrors.RefreshInvalid();
        }

        var session = await db.Set<UserSession>().SingleOrDefaultAsync(s => s.Id == token.SessionId, cancellationToken);
        if (session is null || !session.IsActive(now))
        {
            return IdentityErrors.RefreshInvalid();
        }

        if (token.ConsumedAt is { } consumedAt)
        {
            if (now - consumedAt <= _options.RefreshReuseGrace)
            {
                return IdentityErrors.RefreshRace();
            }

            session.Revoke(SessionRevocationReason.RefreshTokenReuse, now);
            await db.SaveChangesAsync(cancellationToken);
            return IdentityErrors.RefreshInvalid();
        }

        var account = await accounts.FindAsync(session.UserId, cancellationToken);
        if (account is null || account.IsDisabled)
        {
            return IdentityErrors.RefreshInvalid();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Claim the token atomically: of two concurrent refreshes with the same token, exactly one wins.
        var claimed = await db.Set<RefreshToken>()
            .Where(t => t.Id == token.Id && t.ConsumedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.ConsumedAt, now), cancellationToken);
        if (claimed == 0)
        {
            return IdentityErrors.RefreshRace();
        }

        var (successor, successorHash) = NewToken();
        db.Add(new RefreshToken(EntityId.New<RefreshTokenId>(), session.Id, successorHash, now));
        session.Touch(now, HashIp(client.IpAddress));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new IssuedSession(session.Id.Value, session.UserId, session.UserType.ToString(), successor, session.ExpiresAt);
    }

    /// <summary>Finds the session a refresh token belongs to, used by sign-out when the access cookie has expired.</summary>
    public async Task<UserSessionId?> FindSessionByRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var tokenHash = HashToken(refreshToken);
        var sessionId = await db.Set<RefreshToken>().AsNoTracking()
            .Where(t => t.TokenHash == tokenHash)
            .Select(t => (UserSessionId?)t.SessionId)
            .SingleOrDefaultAsync(cancellationToken);
        return sessionId;
    }

    public async Task<bool> RevokeAsync(UserSessionId sessionId, Guid? ownerUserId, SessionRevocationReason reason, CancellationToken cancellationToken)
    {
        var session = await db.Set<UserSession>().SingleOrDefaultAsync(
            s => s.Id == sessionId && (ownerUserId == null || s.UserId == ownerUserId),
            cancellationToken);
        if (session is null)
        {
            return false;
        }

        session.Revoke(reason, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Revokes every active session of the user except <paramref name="keep"/>. Returns how many were revoked.</summary>
    public async Task<int> RevokeAllAsync(Guid userId, UserSessionId? keep, SessionRevocationReason reason, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var query = db.Set<UserSession>().Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now);
        if (keep is { } kept)
        {
            query = query.Where(s => s.Id != kept);
        }

        var sessions = await query.ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
            session.Revoke(reason, now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return sessions.Count;
    }

    public async Task<IReadOnlyList<SessionResponse>> ListActiveAsync(Guid userId, UserSessionId? current, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var sessions = await db.Set<UserSession>().AsNoTracking()
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now)
            .OrderByDescending(s => s.LastSeenAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        return sessions
            .Select(s => new SessionResponse(s.Id.Value, s.DeviceLabel, s.CreatedAt, s.LastSeenAt, s.Id == current))
            .ToArray();
    }

    internal static string HashToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static (string Token, string Hash) NewToken()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (token, HashToken(token));
    }

    private string? HashIp(string? ipAddress) =>
        string.IsNullOrWhiteSpace(ipAddress) ? null : protector.LookupHash(ipAddress, PersonalDataPurposes.IpAddress);
}
