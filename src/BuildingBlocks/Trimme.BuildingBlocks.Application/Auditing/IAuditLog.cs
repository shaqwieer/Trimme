using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.BuildingBlocks.Application.Auditing;

/// <summary>
/// Append-only audit trail of admin and sensitive actions (spec §7, §14; R-TEN-08). <see cref="Record"/> adds the entry
/// to the current unit of work, so it is committed atomically with the change it describes (or not at all).
/// Entries never contain personal data: no phone numbers, emails or names — identifiers only.
/// </summary>
public interface IAuditLog
{
    void Record(AuditRecord record);
}

/// <param name="Action">Stable code, e.g. <c>shop.suspended</c>.</param>
/// <param name="EntityType">e.g. <c>Shop</c>.</param>
/// <param name="EntityId">The affected entity's identifier.</param>
/// <param name="ShopId">The shop concerned, when there is one.</param>
/// <param name="Summary">Short, PII-free description of what changed (e.g. <c>Draft → Active</c>).</param>
/// <param name="Reason">Optional operator-supplied reason (for example for a suspension).</param>
public sealed record AuditRecord(
    string Action,
    string EntityType,
    string EntityId,
    ShopId? ShopId = null,
    string? Summary = null,
    string? Reason = null);
