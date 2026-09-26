using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Web.Errors;
using Trimme.BuildingBlocks.Web.Security;
using Trimme.Modules.Identity.Application;
using Trimme.Modules.Identity.Application.Staff;
using Trimme.Modules.Identity.Domain;
using Trimme.Modules.Identity.Infrastructure.Otp;

namespace Trimme.Modules.Identity.Api;

public sealed record InviteStaffRequest(string Email, string Role, string? Locale);

public sealed record InviteShopUserRequest(string Email, string Role, string? Locale);

public sealed record DevOtpInboxEntry(string Code, DateTimeOffset ExpiresAt);

/// <summary>Admin endpoints for the permission catalogue, roles and staff invitations (R-AUTH-02, R-AUTH-09).</summary>
internal static class AdminIdentityEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var admin = api.MapGroup("/admin").WithTags("Admin: identity");

        admin.MapGet("/permissions", ListPermissions).RequirePermission(Permissions.Admin.RolesView)
            .WithName("ListPermissions").WithSummary("The permission catalogue.");
        admin.MapGet("/roles", ListRoles).RequirePermission(Permissions.Admin.RolesView)
            .WithName("ListRoles").WithSummary("Roles with their granted permissions.");
        admin.MapPost("/staff/invitations", InviteStaff).RequirePermission(Permissions.Admin.StaffManage)
            .WithName("InviteStaff").WithSummary("Emails a platform-admin invitation for the given admin role.")
            .Produces<InvitationResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);
        admin.MapPost("/shops/{shopId:guid}/users/invitations", InviteShopUser).RequirePermission(Permissions.Admin.ShopsManageAccount)
            .WithName("InviteShopUser").WithSummary("Emails a ShopOwner or ShopStaff invitation for one shop (audited).")
            .Produces<InvitationResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<Ok<IReadOnlyList<PermissionResponse>>> ListPermissions(IDispatcher dispatcher, CancellationToken cancellationToken) =>
        TypedResults.Ok(await dispatcher.Send(new ListPermissionsQuery(), cancellationToken));

    private static async Task<Ok<IReadOnlyList<RoleResponse>>> ListRoles(IDispatcher dispatcher, CancellationToken cancellationToken) =>
        TypedResults.Ok(await dispatcher.Send(new ListRolesQuery(), cancellationToken));

    private static async Task<IResult> InviteShopUser(
        Guid shopId, InviteShopUserRequest request, ICurrentUser user, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(
            new InviteShopUserCommand(shopId, request.Email ?? string.Empty, request.Role ?? string.Empty, request.Locale ?? "ar", user.UserId!.Value),
            cancellationToken);
        return result.ToHttpResult(invitation => TypedResults.Created((string?)null, invitation));
    }

    private static async Task<IResult> InviteStaff(InviteStaffRequest request, ICurrentUser user, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(
            new InviteStaffCommand(request.Email ?? string.Empty, request.Role ?? string.Empty, request.Locale ?? "ar", user.UserId!.Value),
            cancellationToken);
        return result.ToHttpResult(invitation => TypedResults.Created((string?)null, invitation));
    }
}

/// <summary>
/// Development/Testing-only reader for the fake OTP sender, used by local sign-in and the E2E tests. It is mapped only
/// when the dev inbox sender is active (which startup validation forbids outside Development and Testing) and is
/// excluded from the OpenAPI contract.
/// </summary>
internal static class DevOtpInboxEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var environment = api.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var delivery = api.ServiceProvider.GetRequiredService<IOptions<OtpDeliveryOptions>>().Value;
        if (delivery.ResolveSender(environment) != OtpSenderKind.DevInbox)
        {
            return;
        }

        api.MapGet("/dev/otp-inbox/latest", Latest).AllowAnonymous().ExcludeFromDescription();
    }

    private static IResult Latest(string phone, DevOtpInbox inbox, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return MobileNumber.TryNormalize(phone, out var e164) && inbox.Latest(e164) is { } message
            ? TypedResults.Ok(new DevOtpInboxEntry(message.Code, message.ExpiresAt))
            : new Error("resource.not_found", "No code for this number.", ErrorKind.NotFound).ToProblem();
    }
}
