using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Paging;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Identity.Application.Admin;
using Trimme.Modules.Identity.Domain;
using Trimme.Modules.Identity.Infrastructure.Persistence;

namespace Trimme.Modules.Identity.Infrastructure;

/// <summary><see cref="IAdminAccounts"/> over the Identity tables of the shared context; writes are staged, never saved here.</summary>
internal sealed class AdminAccounts(TrimmeDbContext db, ILookupNormalizer normalizer) : IAdminAccounts
{
    public async Task<(IReadOnlyList<CustomerAccountRow> Items, int Total)> SearchCustomersAsync(string? term, PageRequest page, CancellationToken cancellationToken)
    {
        var customers = db.Set<ApplicationUser>().AsNoTracking().Where(u => u.UserType == UserType.Customer);
        if (!string.IsNullOrWhiteSpace(term))
        {
            // By name only: a phone number is never a search key here (an exact-number lookup would be a contact oracle).
            var pattern = $"%{term.Trim()}%";
            customers = customers.Where(u => u.DisplayName != null && EF.Functions.ILike(u.DisplayName, pattern));
        }

        var total = await customers.CountAsync(cancellationToken);
        var rows = await customers.OrderByDescending(u => u.CreatedAt).ThenBy(u => u.Id).Skip(page.Skip).Take(page.PageSize)
            .Select(u => new CustomerAccountRow(u.Id, u.DisplayName, u.PreferredLocale, u.CreatedAt, u.DisabledAt != null, null))
            .ToListAsync(cancellationToken);
        return (rows, total);
    }

    public Task<CustomerAccountRow?> FindCustomerAsync(Guid customerId, CancellationToken cancellationToken) =>
        db.Set<ApplicationUser>().AsNoTracking()
            .Where(u => u.Id == customerId && u.UserType == UserType.Customer)
            .Select(u => new CustomerAccountRow(u.Id, u.DisplayName, u.PreferredLocale, u.CreatedAt, u.DisabledAt != null, u.ProtectedPhone))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<(IReadOnlyList<StaffAccountRow> Items, int Total)> ListStaffAsync(string? term, Guid? roleId, PageRequest page, CancellationToken cancellationToken)
    {
        var staff = db.Set<ApplicationUser>().AsNoTracking().Where(u => u.UserType == UserType.PlatformAdmin);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var pattern = $"%{term.Trim()}%";
            staff = staff.Where(u => (u.DisplayName != null && EF.Functions.ILike(u.DisplayName, pattern)) || (u.Email != null && EF.Functions.ILike(u.Email, pattern)));
        }

        if (roleId is { } role)
        {
            staff = staff.Where(u => db.Set<IdentityUserRole<Guid>>().Any(ur => ur.UserId == u.Id && ur.RoleId == role));
        }

        var total = await staff.CountAsync(cancellationToken);
        var users = await staff.OrderBy(u => u.DisplayName).ThenBy(u => u.Id).Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken);
        var roles = await RolesOfAsync([.. users.Select(u => u.Id)], cancellationToken);
        return ([.. users.Select(u => Staff(u, roles))], total);
    }

    public async Task<StaffAccountRow?> FindStaffAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Set<ApplicationUser>().AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId && u.UserType == UserType.PlatformAdmin, cancellationToken);
        return user is null ? null : Staff(user, await RolesOfAsync([user.Id], cancellationToken));
    }

    public async Task SetRolesAsync(Guid userId, IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken)
    {
        var current = await db.Set<IdentityUserRole<Guid>>().Where(ur => ur.UserId == userId).ToListAsync(cancellationToken);
        foreach (var stale in current.Where(ur => !roleIds.Contains(ur.RoleId)))
        {
            db.Remove(stale);
        }

        foreach (var roleId in roleIds.Where(id => current.All(ur => ur.RoleId != id)))
        {
            db.Add(new IdentityUserRole<Guid> { UserId = userId, RoleId = roleId });
        }
    }

    public async Task SetDisabledAsync(Guid userId, DateTimeOffset? disabledAt, CancellationToken cancellationToken)
    {
        var user = await db.Set<ApplicationUser>().SingleAsync(u => u.Id == userId, cancellationToken);
        user.DisabledAt = disabledAt;
    }

    public Task<int> CountHoldersAsync(Guid roleId, bool enabledOnly, CancellationToken cancellationToken) =>
        (from userRole in db.Set<IdentityUserRole<Guid>>()
         join user in db.Set<ApplicationUser>() on userRole.UserId equals user.Id
         where userRole.RoleId == roleId && (!enabledOnly || user.DisabledAt == null)
         select userRole.UserId).CountAsync(cancellationToken);

    public Task<RoleRow?> FindRoleAsync(Guid roleId, CancellationToken cancellationToken) =>
        db.Set<ApplicationRole>().AsNoTracking().Where(r => r.Id == roleId).Select(r => new RoleRow(r.Id, r.Name!, r.UserType)).SingleOrDefaultAsync(cancellationToken);

    public Task<RoleRow?> FindRoleByNameAsync(string name, CancellationToken cancellationToken)
    {
        var normalized = normalizer.NormalizeName(name.Trim());
        return db.Set<ApplicationRole>().AsNoTracking().Where(r => r.NormalizedName == normalized)
            .Select(r => new RoleRow(r.Id, r.Name!, r.UserType)).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RoleRow>> FindRolesAsync(IReadOnlyCollection<string> names, CancellationToken cancellationToken)
    {
        var wanted = names.Select(n => normalizer.NormalizeName(n.Trim())).ToArray();
        return await db.Set<ApplicationRole>().AsNoTracking().Where(r => wanted.Contains(r.NormalizedName!))
            .Select(r => new RoleRow(r.Id, r.Name!, r.UserType)).ToListAsync(cancellationToken);
    }

    public Guid AddRole(string name, UserType userType)
    {
        var id = Guid.CreateVersion7();
        db.Add(new ApplicationRole
        {
            Id = id,
            Name = name.Trim(),
            NormalizedName = normalizer.NormalizeName(name.Trim()),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            UserType = userType,
        });
        return id;
    }

    public async Task RenameRoleAsync(Guid roleId, string name, CancellationToken cancellationToken)
    {
        var role = await db.Set<ApplicationRole>().SingleAsync(r => r.Id == roleId, cancellationToken);
        role.Name = name.Trim();
        role.NormalizedName = normalizer.NormalizeName(name.Trim());
        role.ConcurrencyStamp = Guid.NewGuid().ToString();
    }

    public async Task RemoveRoleAsync(Guid roleId, CancellationToken cancellationToken)
    {
        db.RemoveRange(await db.Set<RolePermission>().Where(g => g.RoleId == roleId).ToListAsync(cancellationToken));
        db.Remove(await db.Set<ApplicationRole>().SingleAsync(r => r.Id == roleId, cancellationToken));
    }

    private async Task<ILookup<Guid, StaffRoleRef>> RolesOfAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.ToArray();
        var rows = await (from userRole in db.Set<IdentityUserRole<Guid>>()
                          join role in db.Set<ApplicationRole>() on userRole.RoleId equals role.Id
                          where ids.Contains(userRole.UserId)
                          orderby role.Name
                          select new { userRole.UserId, role.Id, role.Name }).ToListAsync(cancellationToken);
        return rows.ToLookup(r => r.UserId, r => new StaffRoleRef(r.Id, r.Name!));
    }

    private static StaffAccountRow Staff(ApplicationUser user, ILookup<Guid, StaffRoleRef> roles) =>
        new(user.Id, user.DisplayName, user.Email, user.CreatedAt, user.DisabledAt is not null, [.. roles[user.Id]]);
}
