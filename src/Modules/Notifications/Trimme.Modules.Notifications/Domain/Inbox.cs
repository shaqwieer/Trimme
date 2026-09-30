using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.Modules.Notifications.Domain;

/// <summary>
/// An in-app notification for a shop (spec §13, §17, DV-A05, D-112). Shop-owned, so the tenant filter shows a shop only its
/// own; every user of the shop shares it and its read state. It stores a kind and parameters that the web app renders in
/// the reader's language: names, item names and times, never a phone number (spec §7).
/// </summary>
public sealed class ShopNotification : IShopOwned
{
    public const int MaxKindLength = 60;
    public const int MaxDedupeKeyLength = 200;

    private ShopNotification()
    {
        Kind = DedupeKey = string.Empty;
    }

    public Guid Id { get; private set; }

    public ShopId ShopId { get; private set; }

    public string Kind { get; private set; }

    public string DedupeKey { get; private set; }

    public Dictionary<string, string> Parameters { get; private set; } = [];

    public Guid? BookingId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public Guid? ReadBy { get; private set; }

    public static ShopNotification Create(ShopId shopId, string kind, string dedupeKey, IReadOnlyDictionary<string, string> parameters, Guid? bookingId, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(now),
            ShopId = shopId,
            Kind = kind,
            DedupeKey = dedupeKey,
            Parameters = new Dictionary<string, string>(parameters, StringComparer.Ordinal),
            BookingId = bookingId,
            CreatedAt = now,
        };

    public void MarkRead(Guid userId, DateTimeOffset now)
    {
        if (ReadAt is null)
        {
            ReadAt = now;
            ReadBy = userId;
        }
    }
}

/// <summary>
/// An in-app notification for one account: a customer (their bookings) or a platform admin (operational alerts such as
/// failed messages or expiring subscriptions). Read only by that user (D-112).
/// </summary>
public sealed class UserNotification
{
    private UserNotification()
    {
        Kind = DedupeKey = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Kind { get; private set; }

    public string DedupeKey { get; private set; }

    public Dictionary<string, string> Parameters { get; private set; } = [];

    public Guid? BookingId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public static UserNotification Create(Guid userId, string kind, string dedupeKey, IReadOnlyDictionary<string, string> parameters, Guid? bookingId, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(now),
            UserId = userId,
            Kind = kind,
            DedupeKey = dedupeKey,
            Parameters = new Dictionary<string, string>(parameters, StringComparer.Ordinal),
            BookingId = bookingId,
            CreatedAt = now,
        };

    public void MarkRead(DateTimeOffset now) => ReadAt ??= now;
}

/// <summary>The in-app notification kinds (the web app has a message for each, in Arabic and English).</summary>
public static class NoticeKinds
{
    public const string BookingCreated = "booking.created";
    public const string BookingPending = "booking.pending";
    public const string BookingConfirmed = "booking.confirmed";
    public const string BookingRescheduled = "booking.rescheduled";
    public const string BookingCancelled = "booking.cancelled";
    public const string SubscriptionExpiring = "subscription.expiring";
    public const string SubscriptionExpired = "subscription.expired";
    public const string DispatchFailed = "whatsapp.dispatch_failed";
    public const string OutboxDeadLettered = "outbox.dead_lettered";
    public const string AdminMessage = "admin.message";
}
