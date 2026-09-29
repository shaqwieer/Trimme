namespace Trimme.BuildingBlocks.Application.Realtime;

/// <summary>
/// A change on a shop's operations board, pushed live to that shop's dashboard and to platform admins (D-099). It carries
/// ids, times and the status only — never a customer id, name or phone number (spec §7): clients refetch what they show
/// through the API, which applies the usual permissions.
/// </summary>
/// <param name="Kind">The outbox event name, for example <c>booking.created</c>.</param>
/// <param name="ShopId">The shop the booking belongs to (selects the hub group).</param>
/// <param name="BookingId">The booking.</param>
/// <param name="ProfessionalId">Its professional.</param>
/// <param name="Status">Its status after the change.</param>
/// <param name="StartsAt">Its start after the change.</param>
/// <param name="EndsAt">Its end after the change.</param>
public sealed record OperationsEvent(
    string Kind, Guid ShopId, Guid BookingId, Guid ProfessionalId, string Status, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

/// <summary>
/// Turns an outbox message a module wrote into a live operations event, or <see langword="null"/> for messages that are
/// not operations changes. Implemented by the module that owns the payload (Bookings).
/// </summary>
public interface IOperationsEventProjector
{
    OperationsEvent? Project(string outboxType, string payload);
}

/// <summary>
/// Delivers operations events after their transaction committed (implemented by the API host over SignalR). Delivery is
/// best effort: a failure is logged and never fails the command that caused it.
/// </summary>
public interface IOperationsPublisher
{
    Task PublishAsync(IReadOnlyList<OperationsEvent> events, CancellationToken cancellationToken);
}
