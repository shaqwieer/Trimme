using Trimme.BuildingBlocks.Application.Paging;
using Trimme.Modules.Identity.Domain;

namespace Trimme.Modules.Identity.Application.Admin;

/// <summary>A customer account as the admin directory reads it; <c>ProtectedPhone</c> is the encrypted mobile, never sent as is.</summary>
internal sealed record CustomerAccountRow(Guid Id, string? DisplayName, string PreferredLocale, DateTimeOffset CreatedAt, bool IsDisabled, string? ProtectedPhone);

public sealed record StaffRoleRef(Guid Id, string Name);

internal sealed record StaffAccountRow(Guid Id, string? DisplayName, string? Email, DateTimeOffset CreatedAt, bool IsDisabled, IReadOnlyList<StaffRoleRef> Roles);

internal sealed record RoleRow(Guid Id, string Name, UserType UserType);

/// <summary>
/// Account and role persistence for the admin use cases (implemented over ASP.NET Core Identity in Infrastructure).
/// Writes are staged on the shared unit of work; the handler saves them together with its audit entry.
/// </summary>
internal interface IAdminAccounts
{
    Task<(IReadOnlyList<CustomerAccountRow> Items, int Total)> SearchCustomersAsync(string? term, PageRequest page, CancellationToken cancellationToken);

    Task<CustomerAccountRow?> FindCustomerAsync(Guid customerId, CancellationToken cancellationToken);

    Task<(IReadOnlyList<StaffAccountRow> Items, int Total)> ListStaffAsync(string? term, Guid? roleId, PageRequest page, CancellationToken cancellationToken);

    Task<StaffAccountRow?> FindStaffAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Replaces the account's roles (staged).</summary>
    Task SetRolesAsync(Guid userId, IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken);

    /// <summary>Disables (with the time) or enables the account (staged). A disabled account's sessions stop on the next request.</summary>
    Task SetDisabledAsync(Guid userId, DateTimeOffset? disabledAt, CancellationToken cancellationToken);

    /// <summary>Accounts holding the role; with <paramref name="enabledOnly"/>, only those not disabled.</summary>
    Task<int> CountHoldersAsync(Guid roleId, bool enabledOnly, CancellationToken cancellationToken);

    Task<RoleRow?> FindRoleAsync(Guid roleId, CancellationToken cancellationToken);

    Task<RoleRow?> FindRoleByNameAsync(string name, CancellationToken cancellationToken);

    Task<IReadOnlyList<RoleRow>> FindRolesAsync(IReadOnlyCollection<string> names, CancellationToken cancellationToken);

    /// <summary>Stages a new role and returns its id.</summary>
    Guid AddRole(string name, UserType userType);

    Task RenameRoleAsync(Guid roleId, string name, CancellationToken cancellationToken);

    Task RemoveRoleAsync(Guid roleId, CancellationToken cancellationToken);
}
