using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Web.Errors;
using Trimme.BuildingBlocks.Web.Security;
using Trimme.Modules.Identity.Application.Admin;
using Trimme.Modules.Identity.Application.Barbers;
using Trimme.Modules.Identity.Domain;

namespace Trimme.Modules.Identity.Api;

/// <summary>The temporary password a barber replaces at their first sign-in (D-145).</summary>
public sealed record EnableBarberLoginRequest(string Password);

/// <summary>Turning a barber's login on and off (D-145): the shop owner for their barbers, the admin for any.</summary>
internal static class BarberLoginEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var shop = api.MapGroup("/shop/professionals/{professionalId:guid}/login").WithTags("Shop")
            .RequirePermission(Permissions.Shop.BarbersManageLogin);
        shop.MapGet("/", async (Guid professionalId, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new GetShopBarberLoginQuery(professionalId), ct)).ToHttpResult())
            .WithName("GetShopBarberLogin")
            .WithSummary("Whether the barber can sign in (with their WhatsApp number) and must still replace a temporary password.")
            .Produces<BarberLoginResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        shop.MapPut("/", async (Guid professionalId, EnableBarberLoginRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new EnableShopBarberLoginCommand(professionalId, r.Password ?? string.Empty), ct)).ToHttpResult())
            .WithName("EnableShopBarberLogin")
            .WithSummary("Turns the barber's login on (or on again) with a temporary password they replace at the first sign-in; ends their sessions.")
            .Produces<BarberLoginResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        shop.MapDelete("/", async (Guid professionalId, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new DisableShopBarberLoginCommand(professionalId), ct)).ToHttpResult())
            .WithName("DisableShopBarberLogin").WithSummary("Turns the barber's login off and ends every session.")
            .Produces<BarberLoginResponse>().ProducesProblem(StatusCodes.Status404NotFound);

        var admin = api.MapGroup("/admin/professionals/{professionalId:guid}/login").WithTags("Admin: shops")
            .RequirePermission(Permissions.Admin.ShopsManageAccount);
        admin.MapGet("/", async (Guid professionalId, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new GetAdminBarberLoginQuery(professionalId), ct)).ToHttpResult())
            .WithName("GetAdminBarberLogin").WithSummary("Whether the barber can sign in and must still replace a temporary password.")
            .Produces<BarberLoginResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        admin.MapPut("/", async (Guid professionalId, EnableBarberLoginRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new EnableAdminBarberLoginCommand(professionalId, r.Password ?? string.Empty), ct)).ToHttpResult())
            .WithName("EnableAdminBarberLogin").WithSummary("Turns the barber's login on (or on again) with a temporary password; ends their sessions.")
            .Produces<BarberLoginResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        admin.MapDelete("/", async (Guid professionalId, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new DisableAdminBarberLoginCommand(professionalId), ct)).ToHttpResult())
            .WithName("DisableAdminBarberLogin").WithSummary("Turns the barber's login off and ends every session.")
            .Produces<BarberLoginResponse>().ProducesProblem(StatusCodes.Status404NotFound);
    }
}
