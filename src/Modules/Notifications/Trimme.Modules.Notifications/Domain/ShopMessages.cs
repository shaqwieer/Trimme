using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.Modules.Notifications.Domain;

/// <summary>
/// A WhatsApp message the platform turned off for one shop (D-142). For that shop's bookings, nobody gets that event's
/// message for that audience: the customer slots stop the customer's message, the professional slots stop every barber
/// of the shop. One row per disabled slot; no row means on, so every shop starts with all of them on.
/// <para>
/// Not <see cref="IShopOwned"/>: it is the platform's setting about a shop, never shown to the shop, and the outbox
/// consumer and reminder jobs read it without a shop claim.
/// </para>
/// </summary>
public sealed class ShopMessageOptOut
{
    private ShopMessageOptOut()
    {
    }

    public ShopId ShopId { get; private set; }

    public MessageEvent Event { get; private set; }

    public MessageAudience Audience { get; private set; }

    public DateTimeOffset DisabledAt { get; private set; }

    /// <summary>
    /// The slots a shop's switches cover, customer first, in the order of a booking's life: every event × audience that is
    /// sent at all (<see cref="WhatsAppTemplate.IsSent"/>), so 6 for the customer and 4 for the professional.
    /// </summary>
    public static IReadOnlyList<(MessageAudience Audience, MessageEvent Event)> Slots { get; } =
    [
        .. from audience in new[] { MessageAudience.Customer, MessageAudience.Professional }
           from @event in new[]
           {
               MessageEvent.BookingConfirmed, MessageEvent.BookingPending, MessageEvent.BookingRescheduled,
               MessageEvent.BookingProfessionalChanged, MessageEvent.BookingCancelled, MessageEvent.BookingReminder,
           }
           where WhatsAppTemplate.IsSent(@event, audience)
           select (audience, @event),
    ];

    public static bool IsSlot(MessageAudience audience, MessageEvent @event) => Slots.Contains((audience, @event));

    public static ShopMessageOptOut Create(ShopId shopId, MessageAudience audience, MessageEvent @event, DateTimeOffset now) =>
        new() { ShopId = shopId, Audience = audience, Event = @event, DisabledAt = now };
}
