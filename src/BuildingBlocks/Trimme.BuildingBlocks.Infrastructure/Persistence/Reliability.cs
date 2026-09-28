using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Trimme.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// A message written in the same transaction as the change it describes (R-BKG-08, D-089). A background processor
/// (Phase 15) dispatches it and sets <see cref="ProcessedAt"/>; until then the rows only accumulate. The payload holds
/// identifiers and times, never phone numbers or other contact data.
/// </summary>
public sealed class OutboxMessage
{
    public const int MaxTypeLength = 100;

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

internal static class ReliabilityModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(ConfigureOutbox);
        modelBuilder.Entity<IdempotencyRecord>(ConfigureIdempotency);
    }

    private static void ConfigureOutbox(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages", TrimmeDbContext.InfrastructureSchema);
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Type).HasMaxLength(OutboxMessage.MaxTypeLength);
        builder.Property(m => m.Payload).HasColumnType("jsonb");
        builder.Property(m => m.LastError).HasMaxLength(2000);
        builder.HasIndex(m => m.OccurredAt).HasFilter("processed_at IS NULL");
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
