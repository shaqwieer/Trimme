using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Paging;
using Trimme.BuildingBlocks.Web.Errors;
using Trimme.BuildingBlocks.Web.Security;
using Trimme.Modules.Identity.Application;
using Trimme.Modules.Identity.Application.Admin;
using Trimme.Modules.Identity.Domain;

namespace Trimme.Modules.Identity.Api;

public sealed record RevealContactRequest(string? Reason);

public sealed record RoleNameRequest(string? Name);

public sealed record RolePermissionsRequest(IReadOnlyList<string>? Permissions);

public sealed record StaffRolesRequest(IReadOnlyList<string>? Roles);

public sealed record StaffStatusRequest(string? Reason);

/// <summary>The customers directory (R-AD-06), role management and platform staff (R-AD-11).</summary>
internal static class AdminAccountEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        MapCustomers(api.MapGroup("/admin/customers").WithTags("Admin: customers"));
        MapRoles(api.MapGroup("/admin/roles").WithTags("Admin: identity"));
        MapStaff(api.MapGroup("/admin/staff").WithTags("Admin: identity"));
    }

    private static void MapCustomers(RouteGroupBuilder group)
    {
        group.MapGet("/", async (string? search, int? page, int? pageSize, IDispatcher d, CancellationToken ct) =>
                TypedResults.Ok(await d.Send(new ListAdminCustomersQuery(search, new PageRequest(page, pageSize)), ct)))
            .RequirePermission(Permissions.Admin.CustomersView)
            .WithName("AdminListCustomers")
            .WithSummary("Customers, newest first, searchable by name only; booking count, upcoming, last and next booking. No contact data.")
            .Produces<PagedResponse<AdminCustomerListItem>>();
        group.MapGet("/{customerId:guid}", async (Guid customerId, IDispatcher d, CancellationToken ct) =>
                await d.Send(new GetAdminCustomerQuery(customerId), ct) is { } customer ? TypedResults.Ok(customer) : IdentityErrors.UserNotFound().ToProblem())
            .RequirePermission(Permissions.Admin.CustomersView)
            .WithName("AdminGetCustomer").WithSummary("A customer's profile with the mobile masked and their booking figures.")
            .Produces<AdminCustomerResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/{customerId:guid}/contact/reveal", async (Guid customerId, RevealContactRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new RevealCustomerContactCommand(customerId, r.Reason), ct)).ToHttpResult())
            .RequirePermission(Permissions.Admin.CustomersViewContact)
            .WithName("AdminRevealCustomerContact")
            .WithSummary("Returns the customer's full mobile once, for support; a reason of at least 5 characters is required and the view is audited (never cached).")
            .Produces<CustomerContactResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapRoles(RouteGroupBuilder group)
    {
        group.MapPost("/", async (RoleNameRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new CreateRoleCommand(r.Name ?? string.Empty), ct)).ToHttpResult(role => TypedResults.Created($"/api/v1/admin/roles/{role.Id}", role)))
            .RequirePermission(Permissions.Admin.RolesManage)
            .WithName("CreateRole").WithSummary("Creates a platform-admin role with no permissions (audited).")
            .Produces<RoleResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest);
        group.MapPut("/{roleId:guid}", async (Guid roleId, RoleNameRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new RenameRoleCommand(roleId, r.Name ?? string.Empty), ct)).ToHttpResult())
            .RequirePermission(Permissions.Admin.RolesManage)
            .WithName("RenameRole").WithSummary("Renames a role created by an admin; seed roles keep their names (audited).")
            .Produces<RoleResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapDelete("/{roleId:guid}", async (Guid roleId, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new DeleteRoleCommand(roleId), ct)).ToHttpResult(TypedResults.NoContent))
            .RequirePermission(Permissions.Admin.RolesManage)
            .WithName("DeleteRole").WithSummary("Deletes a role created by an admin that nobody holds (audited).")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPut("/{roleId:guid}/permissions", async (Guid roleId, RolePermissionsRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new SetRolePermissionsCommand(roleId, r.Permissions ?? []), ct)).ToHttpResult())
            .RequirePermission(Permissions.Admin.RolesManage)
            .WithName("SetRolePermissions")
            .WithSummary("Sets an editable admin role's permissions. Managed roles 409; SuperAdmin permissions never; only permissions you hold (403 role.escalation). Audited.")
            .Produces<RoleResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static void MapStaff(RouteGroupBuilder group)
    {
        group.MapGet("/", async (string? search, Guid? roleId, int? page, int? pageSize, IDispatcher d, CancellationToken ct) =>
                TypedResults.Ok(await d.Send(new ListStaffQuery(search, roleId, new PageRequest(page, pageSize)), ct)))
            .RequirePermission(Permissions.Admin.RolesView)
            .WithName("ListStaff").WithSummary("Platform staff with their roles (search by name or email; filter by role).")
            .Produces<PagedResponse<StaffAccountResponse>>();
        group.MapPut("/{userId:guid}/roles", async (Guid userId, StaffRolesRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new SetStaffRolesCommand(userId, r.Roles ?? []), ct)).ToHttpResult())
            .RequirePermission(Permissions.Admin.StaffManage)
            .WithName("SetStaffRoles")
            .WithSummary("Replaces a staff member's admin roles. Not your own; SuperAdmin only by a SuperAdmin; never the last SuperAdmin; no role with permissions you lack. Audited.")
            .Produces<StaffAccountResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPost("/{userId:guid}/disable", async (Guid userId, StaffStatusRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new SetStaffDisabledCommand(userId, true, r.Reason), ct)).ToHttpResult())
            .RequirePermission(Permissions.Admin.StaffManage)
            .WithName("DisableStaff").WithSummary("Blocks a staff account; its sessions stop on the next request (audited).")
            .Produces<StaffAccountResponse>().ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPost("/{userId:guid}/enable", async (Guid userId, StaffStatusRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new SetStaffDisabledCommand(userId, false, r.Reason), ct)).ToHttpResult())
            .RequirePermission(Permissions.Admin.StaffManage)
            .WithName("EnableStaff").WithSummary("Unblocks a staff account (audited).")
            .Produces<StaffAccountResponse>().ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound);
    }
}
