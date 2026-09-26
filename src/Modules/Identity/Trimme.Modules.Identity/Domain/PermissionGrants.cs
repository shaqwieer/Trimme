namespace Trimme.Modules.Identity.Domain;

/// <summary>A catalogue entry persisted in <c>identity.permissions</c> (synchronised from <see cref="Permissions"/>).</summary>
public sealed class Permission
{
    public Permission(string code, UserType userType)
    {
        Code = code;
        UserType = userType;
    }

    public string Code { get; private set; }

    public UserType UserType { get; private set; }
}

/// <summary>Grants a catalogue permission to a role (<c>identity.role_permissions</c>).</summary>
public sealed class RolePermission
{
    public RolePermission(Guid roleId, string permissionCode)
    {
        RoleId = roleId;
        PermissionCode = permissionCode;
    }

    public Guid RoleId { get; private set; }

    public string PermissionCode { get; private set; }
}
