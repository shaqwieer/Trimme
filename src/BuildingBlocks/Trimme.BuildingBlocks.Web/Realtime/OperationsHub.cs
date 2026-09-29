using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trimme.BuildingBlocks.Application.Realtime;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Web.Security;

namespace Trimme.BuildingBlocks.Web.Realtime;

/// <summary>What a dashboard receives (<see cref="OperationsHub.EventName"/>): ids, times and status only (D-099).</summary>
public sealed record OperationsMessage(
    string Kind, Guid ShopId, Guid BookingId, Guid ProfessionalId, string Status, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

/// <summary>
/// Live operations updates (spec §13, R-SD-10, R-TEN-06). The server picks the only group a connection joins, from the
/// session: a shop user of an operable shop with <c>Shop.Bookings.Read</c> joins <c>shop:{their shop}</c>; a platform
/// admin with <c>Admin.Bookings.View</c> joins <c>admins</c>. Every other connection (customers, anonymous, a suspended
/// shop, no permission) is closed at once. The hub has no client-callable methods, so no one can ask to join a group.
/// The connection closes when the access cookie expires; the client refreshes the session and reconnects.
/// </summary>
[Authorize]
public sealed class OperationsHub(IPermissionResolver permissions, IShopDirectory shops) : Hub
{
    public const string Path = "/hubs/operations";
    public const string EventName = "bookingChanged";
    public const string AdminGroup = "admins";
    public const string ShopPermission = "Shop.Bookings.Read";
    public const string AdminPermission = "Admin.Bookings.View";

    public static string ShopGroup(Guid shopId) => $"shop:{shopId}";

    public override async Task OnConnectedAsync()
    {
        var group = await GroupForAsync(Context.User);
        if (group is null)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, group);
        await base.OnConnectedAsync();
    }

    /// <remarks>
    /// The shop's status and the permissions are read here rather than through the request (tenant flags, the endpoint
    /// permission handler): with long polling the hub runs after the request that started it has ended.
    /// </remarks>
    private async Task<string?> GroupForAsync(System.Security.Claims.ClaimsPrincipal? user)
    {
        if (user is not { Identity.IsAuthenticated: true })
        {
            return null;
        }

        switch (user.FindFirst(TrimmeClaims.UserType)?.Value)
        {
            case UserTypes.ShopUser
                when Guid.TryParse(user.FindFirst(TrimmeClaims.ShopId)?.Value, out var shopId)
                     && await OperableAsync(new ShopId(shopId))
                     && await AllowedAsync(user, ShopPermission):
                return ShopGroup(shopId);
            case UserTypes.PlatformAdmin when await AllowedAsync(user, AdminPermission):
                return AdminGroup;
            default:
                return null;
        }
    }

    private async Task<bool> OperableAsync(ShopId shopId) =>
        await shops.FindAsync(shopId, Context.ConnectionAborted) is { Status: not ShopStatus.Suspended };

    /// <summary>The same data-driven permissions as the endpoints, resolved from this connection's own user (not the HTTP context).</summary>
    private async Task<bool> AllowedAsync(System.Security.Claims.ClaimsPrincipal user, string permission) =>
        Guid.TryParse(user.FindFirst(TrimmeClaims.Subject)?.Value, out var userId)
        && (await permissions.GetPermissionsAsync(userId, Context.ConnectionAborted)).Contains(permission);
}

/// <summary><see cref="IOperationsPublisher"/> over the hub: each event to its shop's group and to the admins.</summary>
internal sealed class HubOperationsPublisher(IHubContext<OperationsHub> hub) : IOperationsPublisher
{
    public async Task PublishAsync(IReadOnlyList<OperationsEvent> events, CancellationToken cancellationToken)
    {
        foreach (var e in events)
        {
            var message = new OperationsMessage(e.Kind, e.ShopId, e.BookingId, e.ProfessionalId, e.Status, e.StartsAt, e.EndsAt);
            await hub.Clients.Groups(OperationsHub.ShopGroup(e.ShopId), OperationsHub.AdminGroup)
                .SendAsync(OperationsHub.EventName, message, cancellationToken);
        }
    }
}

/// <summary>The web app's origins, the only ones a hub request may come from.</summary>
internal sealed record HubOrigins(IReadOnlyList<string> Allowed);

public static class RealtimeSetup
{
    /// <summary>
    /// SignalR and the operations publisher. Hub requests are accepted only from the web app's origins (the configured
    /// CORS origins and the public web address; see <see cref="UseOperationsHubOrigins"/>).
    /// </summary>
    public static IServiceCollection AddTrimmeRealtime(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSignalR(options => options.EnableDetailedErrors = false);
        services.AddSingleton<IOperationsPublisher, HubOperationsPublisher>();

        services.AddSingleton(new HubOrigins(
            [.. (configuration.GetSection(CorsSetup.AllowedOriginsKey).Get<string[]>() ?? [])
                .Append(configuration["Web:PublicBaseUrl"])
                .Where(o => !string.IsNullOrWhiteSpace(o))
                .Select(o => o!.TrimEnd('/'))
                .Distinct(StringComparer.OrdinalIgnoreCase)]));
        return services;
    }

    /// <summary>
    /// Refuses hub requests (negotiate, WebSocket, SSE and long polling) that carry an <c>Origin</c> other than the web
    /// app's (403). CORS does not cover WebSockets, and a cross-site page must not open a live connection with the user's
    /// cookies. Requests without an <c>Origin</c> are not from a browser page and carry no ambient cookies of a victim.
    /// </summary>
    public static IApplicationBuilder UseOperationsHubOrigins(this IApplicationBuilder app) =>
        app.UseWhen(
            context => context.Request.Path.StartsWithSegments(OperationsHub.Path),
            branch => branch.Use(async (context, next) =>
            {
                var origins = context.RequestServices.GetRequiredService<HubOrigins>();
                var origin = context.Request.Headers.Origin.ToString();
                if (origin.Length > 0 && origins.Allowed.Count > 0 && !origins.Allowed.Contains(origin.TrimEnd('/'), StringComparer.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }

                await next(context);
            }));

    public static IEndpointConventionBuilder MapOperationsHub(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapHub<OperationsHub>(OperationsHub.Path, options => options.CloseOnAuthenticationExpiration = true)
            .RequireAuthorization();
}
