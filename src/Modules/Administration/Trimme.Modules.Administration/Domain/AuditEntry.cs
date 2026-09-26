using Trimme.BuildingBlocks.Domain.Primitives;

namespace Trimme.Modules.Administration.Domain;

public readonly record struct AuditEntryId(Guid Value) : IEntityId<AuditEntryId>
{
    public static AuditEntryId From(Guid value) => new(value);
}

/// <summary>
/// One audited action (R-TEN-08). Append-only: nothing updates or deletes entries. Holds identifiers and PII-free
/// summaries only. The shop is optional (platform actions have none) and deliberately not a tenant-owned row: shops
/// never read the audit log.
/// </summary>
public sealed class AuditEntry : Entity<AuditEntryId>
{
    public AuditEntry(
        AuditEntryId id,
        DateTimeOffset occurredAt,
        Guid? actorUserId,
        string actorType,
        string action,
        string entityType,
        string entityId,
        Guid? shopId,
        string? summary,
        string? reason,
        string? correlationId)
        : base(id)
    {
        OccurredAt = occurredAt;
        ActorUserId = actorUserId;
        ActorType = actorType;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        ShopId = shopId;
        Summary = summary;
        Reason = reason;
        CorrelationId = correlationId;
    }

    private AuditEntry()
    {
        ActorType = Action = EntityType = EntityId = string.Empty;
    }

    public DateTimeOffset OccurredAt { get; private set; }

    public Guid? ActorUserId { get; private set; }

    /// <summary><c>PlatformAdmin</c>, <c>ShopUser</c>, <c>Customer</c> or <c>System</c>.</summary>
    public string ActorType { get; private set; }

    public string Action { get; private set; }

    public string EntityType { get; private set; }

    public string EntityId { get; private set; }

    public Guid? ShopId { get; private set; }

    public string? Summary { get; private set; }

    public string? Reason { get; private set; }

    public string? CorrelationId { get; private set; }
}
