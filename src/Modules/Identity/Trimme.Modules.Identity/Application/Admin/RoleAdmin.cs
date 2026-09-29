using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Auditing;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Paging;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Identity.Domain;

namespace Trimme.Modules.Identity.Application.Admin;

// Roles, permissions and platform staff (a-roles, DV-A15, R-AD-11, D-106). Admin roles only: shop and customer roles
// are managed by the catalogue. Guards against privilege escalation:
// - managed roles (SuperAdmin, ShopOwner, ShopStaff, Customer) are never edited here;
// - SuperAdmin.* permissions are never granted to another role;
// - an admin grants only permissions they hold themselves, and assigns only roles whose permissions they hold;
// - only a SuperAdmin gives or takes the SuperAdmin role; the last enabled SuperAdmin stays;
// - nobody changes their own roles or disables themselves.
// Every change is audited; permissions are read per request, so a change applies on the next request (D-051).

public sealed record StaffAccountResponse(Guid Id, string? DisplayName, string? Email, DateTimeOffset CreatedAt, bool IsDisabled, IReadOnlyList<StaffRoleRef> Roles);

internal sealed record CreateRoleCommand(string Name) : ICommand<Result<RoleResponse>>;

internal sealed record RenameRoleCommand(Guid RoleId, string Name) : ICommand<Result<RoleResponse>>;

internal sealed record DeleteRoleCommand(Guid RoleId) : ICommand<Result>;

internal sealed record SetRolePermissionsCommand(Guid RoleId, IReadOnlyList<string> Permissions) : ICommand<Result<RoleResponse>>;

internal sealed record ListStaffQuery(string? Search, Guid? RoleId, PageRequest Page) : IQuery<PagedResponse<StaffAccountResponse>>;

internal sealed record SetStaffRolesCommand(Guid UserId, IReadOnlyList<string> Roles) : ICommand<Result<StaffAccountResponse>>;

internal sealed record SetStaffDisabledCommand(Guid UserId, bool Disabled, string? Reason) : ICommand<Result<StaffAccountResponse>>;

internal static class RoleErrors
{
    public static Error NotFound() => Error.NotFound("role.not_found", "The role was not found.");

    public static Error Managed() => Error.Conflict("role.managed", "This role is managed by the platform and cannot be changed.");

    public static Error NameTaken() =>
        Error.Validation("validation.failed", "The name is taken.", new Dictionary<string, string[]> { ["name"] = ["validation.role_name_taken"] });

    public static Error NameInvalid() =>
        Error.Validation("validation.failed", "The name must be 2–50 characters.", new Dictionary<string, string[]> { ["name"] = ["validation.invalid"] });

    public static Error InUse() => Error.Conflict("role.in_use", "The role is assigned to staff; remove it from them first.");

    public static Error PermissionNotGrantable(IEnumerable<string> codes) =>
        Error.Validation("role.permission_not_grantable", "These permissions cannot be granted to this role.").WithDetail("permissions", codes.ToArray());

    public static Error Escalation(IEnumerable<string> codes) =>
        Error.Forbidden("role.escalation", "You can grant only permissions you hold yourself.").WithDetail("permissions", codes.ToArray());

    public static Error SuperAdminOnly() => Error.Forbidden("role.superadmin_only", "Only a SuperAdmin can give or take the SuperAdmin role.");

    public static Error LastSuperAdmin() => Error.Conflict("role.last_superadmin", "The platform must keep at least one enabled SuperAdmin.");

    public static Error Self() => Error.Conflict("staff.self", "You cannot change your own roles or disable yourself.");

    public static Error RolesRequired() =>
        Error.Validation("validation.failed", "Choose at least one role.", new Dictionary<string, string[]> { ["roles"] = ["validation.required"] });

    public static Error RoleNotAssignable(IEnumerable<string> names) =>
        Error.Validation("role.not_assignable", "These roles cannot be assigned to platform staff.").WithDetail("roles", names.ToArray());
}

/// <summary>What the caller holds, read once per command.</summary>
internal sealed class CallerRights(ICurrentUser user, IPermissionResolver permissions, IAccountStore accounts)
{
    public Guid UserId => user.UserId ?? throw new InvalidOperationException("An admin command needs a signed-in user.");

    public async Task<IReadOnlySet<string>> PermissionsAsync(CancellationToken cancellationToken) =>
        await permissions.GetPermissionsAsync(UserId, cancellationToken);

    public async Task<bool> IsSuperAdminAsync(CancellationToken cancellationToken) =>
        (await accounts.FindAsync(UserId, cancellationToken))?.Roles.Contains(SystemRoles.SuperAdmin, StringComparer.Ordinal) == true;
}

/// <summary>
/// Which admin roles the caller may hand out, by assignment or by invitation (D-106): the SuperAdmin role only by a
/// SuperAdmin, and no role that carries a permission the caller does not hold.
/// </summary>
internal static class StaffGuards
{
    public static async Task<Error?> CheckGrantableAsync(
        TrimmeDbContext db, CallerRights caller, IReadOnlyCollection<RoleRow> added, CancellationToken cancellationToken)
    {
        if (added.Any(r => r.Name == SystemRoles.SuperAdmin) && !await caller.IsSuperAdminAsync(cancellationToken))
        {
            return RoleErrors.SuperAdminOnly();
        }

        var ids = added.Select(r => r.Id).ToArray();
        var codes = await db.Set<RolePermission>().AsNoTracking().Where(g => ids.Contains(g.RoleId)).Select(g => g.PermissionCode).Distinct()
            .ToListAsync(cancellationToken);
        var held = await caller.PermissionsAsync(cancellationToken);
        return codes.Where(code => !held.Contains(code)).Order(StringComparer.Ordinal).ToList() is { Count: > 0 } missing
            ? RoleErrors.Escalation(missing)
            : null;
    }
}

internal static class RoleRules
{
    public const int MinNameLength = 2;
    public const int MaxNameLength = 50;

    public static bool IsManaged(string name) => SystemRoles.Find(name)?.Managed == true;

    public static bool NameIsValid(string? name) => name?.Trim().Length is >= MinNameLength and <= MaxNameLength;

    public static async Task<RoleResponse> ResponseAsync(TrimmeDbContext db, RoleRow role, CancellationToken cancellationToken)
    {
        var codes = await db.Set<RolePermission>().AsNoTracking().Where(g => g.RoleId == role.Id).Select(g => g.PermissionCode).ToListAsync(cancellationToken);
        return new RoleResponse(role.Id, role.Name, role.UserType.ToString(), IsManaged(role.Name), [.. codes.Order(StringComparer.Ordinal)]);
    }
}

internal sealed class CreateRoleHandler(TrimmeDbContext db, IAdminAccounts accounts, IAuditLog audit) : ICommandHandler<CreateRoleCommand, Result<RoleResponse>>
{
    public async Task<Result<RoleResponse>> Handle(CreateRoleCommand command, CancellationToken cancellationToken)
    {
        if (!RoleRules.NameIsValid(command.Name))
        {
            return RoleErrors.NameInvalid();
        }

        if (await accounts.FindRoleByNameAsync(command.Name, cancellationToken) is not null)
        {
            return RoleErrors.NameTaken();
        }

        var id = accounts.AddRole(command.Name, UserType.PlatformAdmin);
        audit.Record(new AuditRecord("role.created", "Role", id.ToString(), null, $"Role \"{command.Name.Trim()}\" created (no permissions)", null));
        await db.SaveChangesAsync(cancellationToken);
        return await RoleRules.ResponseAsync(db, new RoleRow(id, command.Name.Trim(), UserType.PlatformAdmin), cancellationToken);
    }
}

internal sealed class RenameRoleHandler(TrimmeDbContext db, IAdminAccounts accounts, IAuditLog audit) : ICommandHandler<RenameRoleCommand, Result<RoleResponse>>
{
    public async Task<Result<RoleResponse>> Handle(RenameRoleCommand command, CancellationToken cancellationToken)
    {
        if (await accounts.FindRoleAsync(command.RoleId, cancellationToken) is not { UserType: UserType.PlatformAdmin } role)
        {
            return RoleErrors.NotFound();
        }

        // Every seed role keeps its name: the catalogue synchroniser finds them by name (D-051).
        if (SystemRoles.Find(role.Name) is not null)
        {
            return RoleErrors.Managed();
        }

        if (!RoleRules.NameIsValid(command.Name))
        {
            return RoleErrors.NameInvalid();
        }

        if (await accounts.FindRoleByNameAsync(command.Name, cancellationToken) is { } other && other.Id != role.Id)
        {
            return RoleErrors.NameTaken();
        }

        await accounts.RenameRoleAsync(role.Id, command.Name, cancellationToken);
        audit.Record(new AuditRecord("role.renamed", "Role", role.Id.ToString(), null, $"\"{role.Name}\" → \"{command.Name.Trim()}\"", null));
        await db.SaveChangesAsync(cancellationToken);
        return await RoleRules.ResponseAsync(db, role with { Name = command.Name.Trim() }, cancellationToken);
    }
}

internal sealed class DeleteRoleHandler(TrimmeDbContext db, IAdminAccounts accounts, IAuditLog audit) : ICommandHandler<DeleteRoleCommand, Result>
{
    public async Task<Result> Handle(DeleteRoleCommand command, CancellationToken cancellationToken)
    {
        if (await accounts.FindRoleAsync(command.RoleId, cancellationToken) is not { UserType: UserType.PlatformAdmin } role)
        {
            return RoleErrors.NotFound();
        }

        if (SystemRoles.Find(role.Name) is not null)
        {
            return RoleErrors.Managed();
        }

        if (await accounts.CountHoldersAsync(role.Id, enabledOnly: false, cancellationToken) > 0)
        {
            return RoleErrors.InUse();
        }

        await accounts.RemoveRoleAsync(role.Id, cancellationToken);
        audit.Record(new AuditRecord("role.deleted", "Role", role.Id.ToString(), null, $"Role \"{role.Name}\" deleted", null));
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class SetRolePermissionsHandler(TrimmeDbContext db, IAdminAccounts accounts, CallerRights caller, IAuditLog audit)
    : ICommandHandler<SetRolePermissionsCommand, Result<RoleResponse>>
{
    public async Task<Result<RoleResponse>> Handle(SetRolePermissionsCommand command, CancellationToken cancellationToken)
    {
        if (await accounts.FindRoleAsync(command.RoleId, cancellationToken) is not { UserType: UserType.PlatformAdmin } role)
        {
            return RoleErrors.NotFound();
        }

        if (RoleRules.IsManaged(role.Name))
        {
            return RoleErrors.Managed();
        }

        var wanted = (command.Permissions ?? []).Select(p => p.Trim()).ToHashSet(StringComparer.Ordinal);
        var catalogue = Permissions.All.ToDictionary(p => p.Code, StringComparer.Ordinal);
        var notGrantable = wanted
            .Where(code => !catalogue.TryGetValue(code, out var definition) || definition.UserType != role.UserType || definition.Scope == nameof(Permissions.SuperAdmin))
            .Order(StringComparer.Ordinal).ToList();
        if (notGrantable.Count > 0)
        {
            return RoleErrors.PermissionNotGrantable(notGrantable);
        }

        var grants = await db.Set<RolePermission>().Where(g => g.RoleId == role.Id).ToListAsync(cancellationToken);
        var current = grants.Select(g => g.PermissionCode).ToHashSet(StringComparer.Ordinal);
        var added = wanted.Except(current).Order(StringComparer.Ordinal).ToList();
        var removed = current.Except(wanted).Order(StringComparer.Ordinal).ToList();
        var held = await caller.PermissionsAsync(cancellationToken);
        if (added.Where(code => !held.Contains(code)).ToList() is { Count: > 0 } escalation)
        {
            return RoleErrors.Escalation(escalation);
        }

        if (added.Count > 0 || removed.Count > 0)
        {
            db.RemoveRange(grants.Where(g => removed.Contains(g.PermissionCode)));
            db.AddRange(added.Select(code => new RolePermission(role.Id, code)));
            audit.Record(new AuditRecord(
                "role.permissions_changed", "Role", role.Id.ToString(), null,
                Truncate($"{role.Name}: added {Join(added)}; removed {Join(removed)}"), null));
            await db.SaveChangesAsync(cancellationToken);
        }

        return await RoleRules.ResponseAsync(db, role, cancellationToken);
    }

    private static string Join(List<string> codes) => codes.Count == 0 ? "none" : string.Join(", ", codes);

    private static string Truncate(string text) => text.Length <= 500 ? text : text[..497] + "...";
}

internal sealed class ListStaffHandler(IAdminAccounts accounts) : IQueryHandler<ListStaffQuery, PagedResponse<StaffAccountResponse>>
{
    public async Task<PagedResponse<StaffAccountResponse>> Handle(ListStaffQuery query, CancellationToken cancellationToken)
    {
        var (rows, total) = await accounts.ListStaffAsync(query.Search, query.RoleId, query.Page, cancellationToken);
        return new PagedResponse<StaffAccountResponse>([.. rows.Select(StaffMapping.ToResponse)], query.Page.Page, query.Page.PageSize, total);
    }
}

internal static class StaffMapping
{
    public static StaffAccountResponse ToResponse(StaffAccountRow row) => new(row.Id, row.DisplayName, row.Email, row.CreatedAt, row.IsDisabled, row.Roles);
}

internal sealed class SetStaffRolesHandler(TrimmeDbContext db, IAdminAccounts accounts, CallerRights caller, IAuditLog audit)
    : ICommandHandler<SetStaffRolesCommand, Result<StaffAccountResponse>>
{
    public async Task<Result<StaffAccountResponse>> Handle(SetStaffRolesCommand command, CancellationToken cancellationToken)
    {
        if (await accounts.FindStaffAsync(command.UserId, cancellationToken) is not { } staff)
        {
            return IdentityErrors.UserNotFound();
        }

        if (staff.Id == caller.UserId)
        {
            return RoleErrors.Self();
        }

        var names = (command.Roles ?? []).Select(r => r.Trim()).Where(r => r.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (names.Count == 0)
        {
            return RoleErrors.RolesRequired();
        }

        var roles = await accounts.FindRolesAsync(names, cancellationToken);
        var unknown = names.Where(n => roles.All(r => !string.Equals(r.Name, n, StringComparison.OrdinalIgnoreCase))).ToList();
        if (unknown.Count > 0 || roles.Any(r => r.UserType != UserType.PlatformAdmin))
        {
            return RoleErrors.RoleNotAssignable([.. unknown, .. roles.Where(r => r.UserType != UserType.PlatformAdmin).Select(r => r.Name)]);
        }

        var before = staff.Roles.Select(r => r.Id).ToHashSet();
        var after = roles.Select(r => r.Id).ToHashSet();
        var superAdmin = await accounts.FindRoleByNameAsync(SystemRoles.SuperAdmin, cancellationToken);
        var touchesSuperAdmin = superAdmin is not null && before.Contains(superAdmin.Id) != after.Contains(superAdmin.Id);
        if (touchesSuperAdmin && !await caller.IsSuperAdminAsync(cancellationToken))
        {
            return RoleErrors.SuperAdminOnly();
        }

        if (superAdmin is not null && before.Contains(superAdmin.Id) && !after.Contains(superAdmin.Id) && !staff.IsDisabled
            && await accounts.CountHoldersAsync(superAdmin.Id, enabledOnly: true, cancellationToken) <= 1)
        {
            return RoleErrors.LastSuperAdmin();
        }

        // No escalation through a role: every permission of a role being added must already be the caller's.
        var added = roles.Where(r => !before.Contains(r.Id)).ToList();
        if (await StaffGuards.CheckGrantableAsync(db, caller, added, cancellationToken) is { } escalation)
        {
            return escalation;
        }

        await accounts.SetRolesAsync(staff.Id, after, cancellationToken);
        audit.Record(new AuditRecord(
            "staff.roles_changed", "User", staff.Id.ToString(), null,
            $"{string.Join(", ", staff.Roles.Select(r => r.Name))} → {string.Join(", ", roles.Select(r => r.Name).Order(StringComparer.Ordinal))}", null));
        await db.SaveChangesAsync(cancellationToken);
        return StaffMapping.ToResponse((await accounts.FindStaffAsync(staff.Id, cancellationToken))!);
    }
}

internal sealed class SetStaffDisabledHandler(TrimmeDbContext db, IAdminAccounts accounts, CallerRights caller, IAuditLog audit, TimeProvider clock)
    : ICommandHandler<SetStaffDisabledCommand, Result<StaffAccountResponse>>
{
    public async Task<Result<StaffAccountResponse>> Handle(SetStaffDisabledCommand command, CancellationToken cancellationToken)
    {
        if (await accounts.FindStaffAsync(command.UserId, cancellationToken) is not { } staff)
        {
            return IdentityErrors.UserNotFound();
        }

        if (staff.Id == caller.UserId)
        {
            return RoleErrors.Self();
        }

        if (staff.IsDisabled == command.Disabled)
        {
            return StaffMapping.ToResponse(staff);
        }

        var isSuperAdmin = staff.Roles.Any(r => r.Name == SystemRoles.SuperAdmin);
        if (isSuperAdmin && !await caller.IsSuperAdminAsync(cancellationToken))
        {
            return RoleErrors.SuperAdminOnly();
        }

        if (command.Disabled && isSuperAdmin
            && await accounts.FindRoleByNameAsync(SystemRoles.SuperAdmin, cancellationToken) is { } superAdmin
            && await accounts.CountHoldersAsync(superAdmin.Id, enabledOnly: true, cancellationToken) <= 1)
        {
            return RoleErrors.LastSuperAdmin();
        }

        await accounts.SetDisabledAsync(staff.Id, command.Disabled ? clock.GetUtcNow() : null, cancellationToken);
        audit.Record(new AuditRecord(command.Disabled ? "staff.disabled" : "staff.enabled", "User", staff.Id.ToString(), null, null, command.Reason?.Trim()));
        await db.SaveChangesAsync(cancellationToken);
        return StaffMapping.ToResponse((await accounts.FindStaffAsync(staff.Id, cancellationToken))!);
    }
}
