using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Trimme.BuildingBlocks.Application.Notifications;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Web.Security;

namespace Trimme.BuildingBlocks.Web.Realtime;

/// <summary>
/// "You have new notifications" for the in-app bell (spec §17, R-NTF-10, D-112). Every signed-in user joins their own
/// <c>user:{id}</c> group; a shop user of an operable shop with <c>Shop.Bookings.Read</c> also joins <c>shop:{their shop}</c>.
/// The server picks the groups from the session, and the hub has no client-callable methods. The message carries no
/// content at all: the client refetches its notifications through the API.
/// </summary>
[Authorize]
public sealed class NotificationsHub(IPermissionResolver permissions, IShopDirectory shops) : Hub
{
    public const string Path = "/hubs/notifications";
    public const string EventName = "notificationsChanged";

    public static string UserGroup(Guid userId) => $"user:{userId}";

    public static string ShopGroup(Guid shopId) => $"shop:{shopId}";

    public override async Task OnConnectedAsync()
    {
        var user = Context.User;
        if (user is not { Identity.IsAuthenticated: true } || !Guid.TryParse(user.FindFirst(TrimmeClaims.Subject)?.Value, out var userId))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));
        if (user.FindFirst(TrimmeClaims.UserType)?.Value == UserTypes.ShopUser
            && Guid.TryParse(user.FindFirst(TrimmeClaims.ShopId)?.Value, out var shopId)
            && await shops.FindAsync(new ShopId(shopId), Context.ConnectionAborted) is { Status: not ShopStatus.Suspended }
            && (await permissions.GetPermissionsAsync(userId, Context.ConnectionAborted)).Contains(OperationsHub.ShopPermission))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, ShopGroup(shopId));
        }

        await base.OnConnectedAsync();
    }
}

/// <summary><see cref="INotificationsPush"/> over the hub: an empty signal to each audience's group.</summary>
internal sealed class HubNotificationsPush(IHubContext<NotificationsHub> hub) : INotificationsPush
{
    public async Task PushAsync(IReadOnlyCollection<NotificationAudience> audiences, CancellationToken cancellationToken)
    {
        var groups = audiences
            .Select(a => a.ShopId is { } shop ? NotificationsHub.ShopGroup(shop.Value) : a.UserId is { } user ? NotificationsHub.UserGroup(user) : null)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (groups.Count > 0)
        {
            await hub.Clients.Groups(groups).SendAsync(NotificationsHub.EventName, cancellationToken);
        }
    }
}
