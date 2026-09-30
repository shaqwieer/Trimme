namespace Trimme.BuildingBlocks.Application.Messaging;

/// <summary>
/// A committed outbox message handed to a consumer (R-NTF-05, D-108). The payload is the JSON the writer stored:
/// identifiers, times and statuses only, never contact data.
/// </summary>
/// <param name="Id">The message id; with the consumer name it is the idempotency key of one delivery.</param>
/// <param name="Type">Stable event name, for example <c>booking.created</c>.</param>
/// <param name="Payload">JSON (camelCase).</param>
/// <param name="OccurredAt">When the change was committed.</param>
public sealed record OutboxEnvelope(Guid Id, string Type, string Payload, DateTimeOffset OccurredAt);

/// <summary>
/// Handles committed outbox messages at least once (D-108). The processor runs each consumer in its own transaction
/// and records <c>(message, consumer)</c> in the processed-message table inside it, so a consumer that succeeded is never
/// run again for that message, even when another consumer of the same message fails and the message is retried.
/// Consumers still write idempotently (unique keys), because a crash can happen after their work and before the commit.
/// </summary>
public interface IOutboxConsumer
{
    /// <summary>Stable name (part of the processed-message key); never rename a consumer that has run.</summary>
    string Name { get; }

    bool Handles(string messageType);

    /// <summary>
    /// Does the work inside the processor's transaction (the processor saves and commits). Work that must happen only
    /// after the commit, such as enqueuing a job that reads the new rows, is registered on <paramref name="context"/>.
    /// </summary>
    Task HandleAsync(OutboxEnvelope message, OutboxConsumerContext context, CancellationToken cancellationToken);
}

/// <summary>Per-delivery context: actions to run once the consumer's transaction has committed.</summary>
public sealed class OutboxConsumerContext
{
    private readonly List<Func<CancellationToken, Task>> _afterCommit = [];

    public IReadOnlyList<Func<CancellationToken, Task>> AfterCommitActions => _afterCommit;

    public void AfterCommit(Func<CancellationToken, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _afterCommit.Add(action);
    }
}
