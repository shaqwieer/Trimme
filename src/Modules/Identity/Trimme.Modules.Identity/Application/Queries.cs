using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Identity.Domain;

namespace Trimme.Modules.Identity.Application;

internal sealed record GetMeQuery(Guid UserId) : IQuery<MeResponse?>;

internal sealed record ListPermissionsQuery : IQuery<IReadOnlyList<PermissionResponse>>;

internal sealed record ListRolesQuery : IQuery<IReadOnlyList<RoleResponse>>;

public sealed record RoleSummary(Guid Id, string Name, UserType UserType);

/// <summary>Role listing (roles are ASP.NET Core Identity rows, so they are read through the port).</summary>
internal interface IRoleDirectory
{
    Task<IReadOnlyList<RoleSummary>> ListAsync(CancellationToken cancellationToken);
}

internal sealed class GetMeHandler(MeReader me) : IQueryHandler<GetMeQuery, MeResponse?>
{
    public Task<MeResponse?> Handle(GetMeQuery query, CancellationToken cancellationToken) =>
        me.ReadAsync(query.UserId, cancellationToken);
}

internal sealed class ListPermissionsHandler(TrimmeDbContext db) : IQueryHandler<ListPermissionsQuery, IReadOnlyList<PermissionResponse>>
{
    public async Task<IReadOnlyList<PermissionResponse>> Handle(ListPermissionsQuery query, CancellationToken cancellationToken)
    {
        var rows = await db.Set<Permission>().AsNoTracking().OrderBy(p => p.Code).ToListAsync(cancellationToken);
        return rows
            .Select(p => new PermissionResponse(p.Code, p.Code[..p.Code.IndexOf('.', StringComparison.Ordinal)], p.UserType.ToString()))
            .ToArray();
    }
}

internal sealed class ListRolesHandler(TrimmeDbContext db, IRoleDirectory roles) : IQueryHandler<ListRolesQuery, IReadOnlyList<RoleResponse>>
{
    public async Task<IReadOnlyList<RoleResponse>> Handle(ListRolesQuery query, CancellationToken cancellationToken)
    {
        var all = await roles.ListAsync(cancellationToken);
        var grants = (await db.Set<RolePermission>().AsNoTracking().ToListAsync(cancellationToken))
            .ToLookup(g => g.RoleId);

        return all
            .Select(r => new RoleResponse(
                r.Id,
                r.Name,
                r.UserType.ToString(),
                SystemRoles.Find(r.Name)?.Managed ?? false,
                [.. grants[r.Id].Select(g => g.PermissionCode).Order(StringComparer.Ordinal)]))
            .ToArray();
    }
}
