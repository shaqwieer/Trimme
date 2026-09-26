using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.Modules.Identity.Domain;
using Trimme.Modules.Identity.Infrastructure.Persistence;

namespace Trimme.Modules.Identity.Infrastructure.Seeding;

/// <summary>
/// Keeps <c>identity.permissions</c> equal to the code catalogue and makes sure the seed roles exist (D-051):
/// managed roles (SuperAdmin, ShopOwner, ShopStaff, Customer) always hold exactly their default grants; the editable
/// seed roles (OperationsManager, Support) receive their defaults only when first created. Grants of permissions that
/// no longer exist are removed. A grant is never made across user types.
/// </summary>
internal sealed class PermissionCatalogueSynchronizer : IReferenceDataSynchronizer
{
    public int Order => 100;

    public string Name => "identity-permission-catalogue";

    public async Task SynchronizeAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<TrimmeDbContext>();
        var catalogue = Permissions.All.ToDictionary(p => p.Code, StringComparer.Ordinal);

        var stored = await db.Set<Permission>().ToListAsync(cancellationToken);
        foreach (var obsolete in stored.Where(p => !catalogue.ContainsKey(p.Code)))
        {
            db.Remove(obsolete);
        }

        var storedCodes = stored.Select(p => p.Code).ToHashSet(StringComparer.Ordinal);
        foreach (var definition in Permissions.All.Where(p => !storedCodes.Contains(p.Code)))
        {
            db.Add(new Permission(definition.Code, definition.UserType));
        }

        await db.SaveChangesAsync(cancellationToken);

        var roles = await db.Set<ApplicationRole>().ToListAsync(cancellationToken);
        var grants = await db.Set<RolePermission>().ToListAsync(cancellationToken);
        var normalizer = services.GetRequiredService<ILookupNormalizer>();

        foreach (var definition in SystemRoles.All)
        {
            var role = roles.SingleOrDefault(r => r.Name == definition.Name);
            var created = role is null;
            if (role is null)
            {
                role = new ApplicationRole
                {
                    Id = Guid.CreateVersion7(),
                    Name = definition.Name,
                    NormalizedName = normalizer.NormalizeName(definition.Name),
                    ConcurrencyStamp = Guid.NewGuid().ToString(),
                    UserType = definition.UserType,
                };
                db.Add(role);
            }

            var current = grants.Where(g => g.RoleId == role.Id).ToList();
            var wanted = definition.DefaultPermissions.ToHashSet(StringComparer.Ordinal);

            if (definition.Managed || created)
            {
                foreach (var grant in current.Where(g => !wanted.Contains(g.PermissionCode)))
                {
                    db.Remove(grant);
                }

                foreach (var code in wanted.Where(c => current.All(g => g.PermissionCode != c)))
                {
                    db.Add(new RolePermission(role.Id, code));
                }
            }
        }

        // Editable roles keep their admin-chosen grants, but never a grant for another user type.
        foreach (var grant in grants)
        {
            var role = roles.SingleOrDefault(r => r.Id == grant.RoleId);
            if (role is not null && catalogue.TryGetValue(grant.PermissionCode, out var permission) && permission.UserType != role.UserType)
            {
                db.Remove(grant);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
