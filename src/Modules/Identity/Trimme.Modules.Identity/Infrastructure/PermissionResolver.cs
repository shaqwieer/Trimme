using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Identity.Domain;
using Trimme.Modules.Identity.Infrastructure.Persistence;

namespace Trimme.Modules.Identity.Infrastructure;

/// <summary>
/// Reads a user's permissions from the database once per request (scoped cache), so role and grant changes apply to
/// the next request rather than waiting for the access cookie to expire.
/// </summary>
internal sealed class PermissionResolver(TrimmeDbContext db) : IPermissionResolver
{
    private readonly Dictionary<Guid, IReadOnlySet<string>> _cache = [];

    public async Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(userId, out var cached))
        {
            return cached;
        }

        var codes = await (from userRole in db.Set<IdentityUserRole<Guid>>()
                           join grant in db.Set<RolePermission>() on userRole.RoleId equals grant.RoleId
                           join user in db.Set<ApplicationUser>() on userRole.UserId equals user.Id
                           where userRole.UserId == userId && user.DisabledAt == null
                           select grant.PermissionCode)
            .Distinct()
            .ToListAsync(cancellationToken);

        var permissions = codes.ToHashSet(StringComparer.Ordinal);
        _cache[userId] = permissions;
        return permissions;
    }
}

/// <summary>Checked on every authenticated request: the session must be active and the account enabled (R-AUTH-05).</summary>
internal sealed class SessionValidator(TrimmeDbContext db, TimeProvider clock)
{
    public Task<bool> IsActiveAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var id = new UserSessionId(sessionId);
        return (from session in db.Set<UserSession>()
                join user in db.Set<ApplicationUser>() on session.UserId equals user.Id
                where session.Id == id && session.UserId == userId && session.RevokedAt == null
                      && session.ExpiresAt > now && user.DisabledAt == null
                select session.Id).AnyAsync(cancellationToken);
    }
}
