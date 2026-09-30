using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Notifications;
using Trimme.BuildingBlocks.Application.Paging;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Notifications.Domain;

namespace Trimme.Modules.Notifications.Application;

// In-app notification centres (spec §12, §13, §17; R-CUS-11, R-SD-08, R-NTF-10; D-112): the shop's (tenant-filtered,
// shared by its users) and each account's own (customers and admins). Mark-read and mark-all; no deletion.

/// <summary>A notification as the web app renders it: the kind and its parameters (names, ISO times, counts), never a phone number.</summary>
public sealed record NotificationResponse(Guid Id, string Kind, IReadOnlyDictionary<string, string> Parameters, Guid? BookingId, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

public sealed record NotificationListResponse(IReadOnlyList<NotificationResponse> Items, int Page, int PageSize, int Total, int Unread);

public sealed record UnreadCountResponse(int Unread);

internal sealed record ListShopNotificationsQuery(bool UnreadOnly, PageRequest Page) : IQuery<Result<NotificationListResponse>>;

internal sealed record ShopUnreadCountQuery : IQuery<Result<UnreadCountResponse>>;

internal sealed record MarkShopNotificationsReadCommand(Guid? NotificationId) : ICommand<Result>;

internal sealed record ListMyNotificationsQuery(bool UnreadOnly, PageRequest Page) : IQuery<Result<NotificationListResponse>>;

internal sealed record MyUnreadCountQuery : IQuery<Result<UnreadCountResponse>>;

internal sealed record MarkMyNotificationsReadCommand(Guid? NotificationId) : ICommand<Result>;

internal static class InboxErrors
{
    public static Error NotFound() => Error.NotFound("notification.not_found", "The notification was not found.");

    public static Error NoShop() => Error.NotFound("shop.not_found", "The shop was not found.");

    public static Error NotSignedIn() => Error.Forbidden("auth.forbidden", "Sign in to see notifications.");
}

internal sealed class ListShopNotificationsHandler(TrimmeDbContext db, ICurrentTenant tenant)
    : IQueryHandler<ListShopNotificationsQuery, Result<NotificationListResponse>>
{
    public async Task<Result<NotificationListResponse>> Handle(ListShopNotificationsQuery query, CancellationToken cancellationToken)
    {
        if (tenant.ShopId is null)
        {
            return InboxErrors.NoShop();
        }

        // The tenant filter limits the rows to the caller's shop.
        var all = db.Set<ShopNotification>().AsNoTracking();
        var listed = query.UnreadOnly ? all.Where(n => n.ReadAt == null) : all;
        var total = await listed.CountAsync(cancellationToken);
        var unread = await all.CountAsync(n => n.ReadAt == null, cancellationToken);
        var items = await listed.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Skip(query.Page.Skip).Take(query.Page.PageSize)
            .Select(n => new NotificationResponse(n.Id, n.Kind, n.Parameters, n.BookingId, n.CreatedAt, n.ReadAt))
            .ToListAsync(cancellationToken);
        return new NotificationListResponse(items, query.Page.Page, query.Page.PageSize, total, unread);
    }
}

internal sealed class ShopUnreadCountHandler(TrimmeDbContext db, ICurrentTenant tenant) : IQueryHandler<ShopUnreadCountQuery, Result<UnreadCountResponse>>
{
    public async Task<Result<UnreadCountResponse>> Handle(ShopUnreadCountQuery query, CancellationToken cancellationToken) =>
        tenant.ShopId is null
            ? InboxErrors.NoShop()
            : new UnreadCountResponse(await db.Set<ShopNotification>().CountAsync(n => n.ReadAt == null, cancellationToken));
}

internal sealed class MarkShopNotificationsReadHandler(TrimmeDbContext db, ICurrentTenant tenant, ICurrentUser user, TimeProvider clock)
    : ICommandHandler<MarkShopNotificationsReadCommand, Result>
{
    public async Task<Result> Handle(MarkShopNotificationsReadCommand command, CancellationToken cancellationToken)
    {
        if (tenant.ShopId is null || user.UserId is not { } userId)
        {
            return InboxErrors.NoShop();
        }

        var now = clock.GetUtcNow();
        if (command.NotificationId is { } id)
        {
            var notification = await db.Set<ShopNotification>().SingleOrDefaultAsync(n => n.Id == id, cancellationToken);
            if (notification is null)
            {
                return InboxErrors.NotFound();
            }

            notification.MarkRead(userId, now);
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }

        var shopId = tenant.ShopId.Value;
        await db.Set<ShopNotification>().Where(n => n.ShopId == shopId && n.ReadAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(n => n.ReadAt, now).SetProperty(n => n.ReadBy, userId), cancellationToken);
        return Result.Success();
    }
}

internal sealed class ListMyNotificationsHandler(TrimmeDbContext db, ICurrentUser user)
    : IQueryHandler<ListMyNotificationsQuery, Result<NotificationListResponse>>
{
    public async Task<Result<NotificationListResponse>> Handle(ListMyNotificationsQuery query, CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
        {
            return InboxErrors.NotSignedIn();
        }

        var all = db.Set<UserNotification>().AsNoTracking().Where(n => n.UserId == userId);
        var listed = query.UnreadOnly ? all.Where(n => n.ReadAt == null) : all;
        var total = await listed.CountAsync(cancellationToken);
        var unread = await all.CountAsync(n => n.ReadAt == null, cancellationToken);
        var items = await listed.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Skip(query.Page.Skip).Take(query.Page.PageSize)
            .Select(n => new NotificationResponse(n.Id, n.Kind, n.Parameters, n.BookingId, n.CreatedAt, n.ReadAt))
            .ToListAsync(cancellationToken);
        return new NotificationListResponse(items, query.Page.Page, query.Page.PageSize, total, unread);
    }
}

internal sealed class MyUnreadCountHandler(TrimmeDbContext db, ICurrentUser user) : IQueryHandler<MyUnreadCountQuery, Result<UnreadCountResponse>>
{
    public async Task<Result<UnreadCountResponse>> Handle(MyUnreadCountQuery query, CancellationToken cancellationToken) =>
        user.UserId is not { } userId
            ? InboxErrors.NotSignedIn()
            : new UnreadCountResponse(await db.Set<UserNotification>().CountAsync(n => n.UserId == userId && n.ReadAt == null, cancellationToken));
}

internal sealed class MarkMyNotificationsReadHandler(TrimmeDbContext db, ICurrentUser user, TimeProvider clock)
    : ICommandHandler<MarkMyNotificationsReadCommand, Result>
{
    public async Task<Result> Handle(MarkMyNotificationsReadCommand command, CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
        {
            return InboxErrors.NotSignedIn();
        }

        var now = clock.GetUtcNow();
        if (command.NotificationId is { } id)
        {
            var notification = await db.Set<UserNotification>().SingleOrDefaultAsync(n => n.Id == id && n.UserId == userId, cancellationToken);
            if (notification is null)
            {
                return InboxErrors.NotFound();
            }

            notification.MarkRead(now);
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }

        await db.Set<UserNotification>().Where(n => n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(n => n.ReadAt, now), cancellationToken);
        return Result.Success();
    }
}

/// <summary>
/// <see cref="INotificationCenter"/>: adds notification rows to the caller's unit of work (skipping duplicates by dedupe
/// key) and remembers the audiences, so <see cref="PushPendingAsync"/> can signal them after the commit. Shop rows are
/// shop-owned, so the caller runs inside the shop's tenant or the system scope (background jobs).
/// </summary>
internal sealed partial class NotificationCenter(
    TrimmeDbContext db, IStaffDirectory staff, INotificationsPush push, TimeProvider clock, ILogger<NotificationCenter> logger) : INotificationCenter
{
    private readonly List<NotificationAudience> _pending = [];

    public async Task NotifyShopAsync(ShopId shopId, InAppNotice notice, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notice);
        var exists = db.Set<ShopNotification>().Local.Any(n => n.ShopId == shopId && n.DedupeKey == notice.DedupeKey)
                     || await db.Set<ShopNotification>().AnyAsync(n => n.ShopId == shopId && n.DedupeKey == notice.DedupeKey, cancellationToken);
        if (!exists)
        {
            db.Add(ShopNotification.Create(shopId, notice.Kind, notice.DedupeKey, notice.Parameters, notice.BookingId, clock.GetUtcNow()));
            _pending.Add(new NotificationAudience(null, shopId));
        }
    }

    public async Task NotifyUserAsync(Guid userId, InAppNotice notice, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notice);
        var exists = db.Set<UserNotification>().Local.Any(n => n.UserId == userId && n.DedupeKey == notice.DedupeKey)
                     || await db.Set<UserNotification>().AnyAsync(n => n.UserId == userId && n.DedupeKey == notice.DedupeKey, cancellationToken);
        if (!exists)
        {
            db.Add(UserNotification.Create(userId, notice.Kind, notice.DedupeKey, notice.Parameters, notice.BookingId, clock.GetUtcNow()));
            _pending.Add(new NotificationAudience(userId, null));
        }
    }

    public async Task NotifyAdminsAsync(string permission, InAppNotice notice, CancellationToken cancellationToken)
    {
        foreach (var admin in await staff.ActiveAdminsWithPermissionAsync(permission, cancellationToken))
        {
            await NotifyUserAsync(admin, notice, cancellationToken);
        }
    }

    public async Task PushPendingAsync(CancellationToken cancellationToken)
    {
        if (_pending.Count == 0)
        {
            return;
        }

        var audiences = _pending.Distinct().ToList();
        _pending.Clear();
        try
        {
            await push.PushAsync(audiences, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogPushFailed(logger, exception, audiences.Count);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Signalling {Count} notification audiences failed")]
    private static partial void LogPushFailed(ILogger logger, Exception exception, int count);
}
