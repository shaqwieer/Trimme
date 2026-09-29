using System.Text.Json;
using Trimme.BuildingBlocks.Application.Realtime;
using Trimme.Modules.Bookings.Application;

namespace Trimme.Modules.Bookings.Infrastructure;

/// <summary>
/// <see cref="IOperationsEventProjector"/> for booking outbox messages (D-099): keeps the ids, times and status, and
/// drops everything else the outbox payload carries (the customer id, the channel, the actor).
/// </summary>
internal sealed class BookingOperationsProjector : IOperationsEventProjector
{
    public OperationsEvent? Project(string outboxType, string payload)
    {
        if (outboxType is not (BookingEvents.Created or BookingEvents.Rescheduled or BookingEvents.Cancelled or BookingEvents.StatusChanged))
        {
            return null;
        }

        var booking = JsonSerializer.Deserialize<BookingEvent>(payload, JsonSerializerOptions.Web);
        return booking is null
            ? null
            : new OperationsEvent(outboxType, booking.ShopId, booking.BookingId, booking.ProfessionalId, booking.Status, booking.StartsAt, booking.EndsAt);
    }
}
