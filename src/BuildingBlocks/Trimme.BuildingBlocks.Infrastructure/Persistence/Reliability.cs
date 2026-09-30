using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Trimme.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// A message written in the same transaction as the change it describes (R-BKG-08, D-089). The outbox processor (D-108)
/// hands it to every consumer and sets <see cref="ProcessedAt"/>; a failure is retried with backoff and, after
/// <see cref="MaxAttempts"/>, the message is dead-lettered. The payload holds identifiers and times, never phone numbers
/// or other contact data.
/// </summary>
public sealed class OutboxMessage
{
    public const int MaxTypeLength = 100;
    public const int MaxErrorLength = 2000;

    /// <summary>Failed deliveries before the message is dead-lettered (then only an operator can replay it).</summary>
    public const int MaxAttempts = 8;

    private OutboxMessage(Guid id, string type, string payload, DateTimeOffset occurredAt)
    {
        Id = id;
        Type = type;
        Payload = payload;
        OccurredAt = occurredAt;
    }

    private OutboxMessage()
    {
        Type = Payload = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary>Stable event name, for example <c>booking.created</c>.</summary>
    public string Type { get; private set; }

    /// <summary>JSON (camelCase).</summary>
    public string Payload { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    /// <summary>Not before this instant (the backoff after a failure); null means now.</summary>
    public DateTimeOffset? NextAttemptAt { get; private set; }

    /// <summary>Set when the retries ran out; the processor skips it from then on.</summary>
    public DateTimeOffset? DeadLetteredAt { get; private set; }

    public void MarkProcessed(DateTimeOffset now)
    {
        ProcessedAt = now;
        NextAttemptAt = null;
    }

    /// <summary>
    /// Records a failed delivery: exponential backoff (30 s, 1 min, 2 min … capped at 1 h), dead-lettered after
    /// <see cref="MaxAttempts"/>. Returns true when the message is now dead-lettered.
    /// </summary>
    public bool RecordFailure(string error, DateTimeOffset now)
    {
        Attempts++;
        LastError = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
        if (Attempts >= MaxAttempts)
        {
            DeadLetteredAt = now;
            NextAttemptAt = null;
            return true;
        }

        NextAttemptAt = now + Backoff(Attempts);
        return false;
    }

    public static TimeSpan Backoff(int attempts) =>
        TimeSpan.FromSeconds(Math.Min(3600, 30 * Math.Pow(2, Math.Max(0, attempts - 1))));

    public static OutboxMessage Create(string type, object payload, DateTimeOffset occurredAt) =>
        new(Guid.CreateVersion7(occurredAt), type, JsonSerializer.Serialize(payload, payload.GetType(), JsonSerializerOptions.Web), occurredAt);
}

/// <summary>
/// An idempotency key already used by a user for one operation (R-BKG-05, D-089). The key is claimed (inserted and
/// saved) inside the command's transaction before the work, so a concurrent request with the same key waits on the
/// primary key and then replays the stored result. A failed command rolls the claim back, so the key can be retried.
/// </summary>
public sealed class IdempotencyRecord
{
    public const int MaxKeyLength = 100;
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    public IdempotencyRecord(Guid userId, string scope, string key, string requestHash, DateTimeOffset now)
    {
        UserId = userId;
        Scope = scope;
        Key = key;
        RequestHash = requestHash;
        CreatedAt = now;
        ExpiresAt = now + Lifetime;
    }

    private IdempotencyRecord()
    {
        Scope = Key = RequestHash = string.Empty;
    }

    public Guid UserId { get; private set; }

    /// <summary>The operation, for example <c>bookings.create</c>.</summary>
    public string Scope { get; private set; }

    public string Key { get; private set; }

    /// <summary>SHA-256 of the request; the same key with a different request is refused.</summary>
    public string RequestHash { get; private set; }

    /// <summary>The resource the command produced (the booking), set before the transaction commits.</summary>
    public Guid? ResourceId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public void Complete(Guid resourceId) => ResourceId = resourceId;

    /// <summary>A stable hash of a request object (its JSON).</summary>
    public static string Hash(object request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, request.GetType(), JsonSerializerOptions.Web))));
}

/// <summary>
/// One consumer's completed delivery of one outbox message (D-108): written in the consumer's transaction, so a consumer
/// that succeeded is never run again for that message.
/// </summary>
public sealed class ProcessedMessage
{
    public ProcessedMessage(Guid messageId, string consumer, DateTimeOffset processedAt)
    {
        MessageId = messageId;
        Consumer = consumer;
        ProcessedAt = processedAt;
    }

    private ProcessedMessage()
    {
        Consumer = string.Empty;
    }

    public Guid MessageId { get; private set; }

    public string Consumer { get; private set; }

    public DateTimeOffset ProcessedAt { get; private set; }
}

internal static class ReliabilityModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(ConfigureOutbox);
        modelBuilder.Entity<IdempotencyRecord>(ConfigureIdempotency);
        modelBuilder.Entity<ProcessedMessage>(builder =>
        {
            builder.ToTable("processed_messages", TrimmeDbContext.InfrastructureSchema);
            builder.HasKey(m => new { m.MessageId, m.Consumer });
            builder.Property(m => m.Consumer).HasMaxLength(100);
            builder.HasIndex(m => m.ProcessedAt);
        });
    }

    private static void ConfigureOutbox(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages", TrimmeDbContext.InfrastructureSchema);
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Type).HasMaxLength(OutboxMessage.MaxTypeLength);
        builder.Property(m => m.Payload).HasColumnType("jsonb");
        builder.Property(m => m.LastError).HasMaxLength(OutboxMessage.MaxErrorLength);
        builder.HasIndex(m => m.OccurredAt).HasFilter("processed_at IS NULL");
        builder.HasIndex(m => m.NextAttemptAt).HasFilter("processed_at IS NULL AND dead_lettered_at IS NULL");
        builder.HasIndex(m => m.ProcessedAt).HasFilter("processed_at IS NOT NULL");
    }

    private static void ConfigureIdempotency(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_records", TrimmeDbContext.InfrastructureSchema);
        builder.HasKey(r => new { r.UserId, r.Scope, r.Key });
        builder.Property(r => r.Scope).HasMaxLength(60);
        builder.Property(r => r.Key).HasMaxLength(IdempotencyRecord.MaxKeyLength);
        builder.Property(r => r.RequestHash).HasMaxLength(64);
        builder.HasIndex(r => r.ExpiresAt);
    }
}
