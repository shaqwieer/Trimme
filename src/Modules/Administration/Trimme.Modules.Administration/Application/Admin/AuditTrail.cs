using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Paging;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Administration.Domain;

namespace Trimme.Modules.Administration.Application.Admin;

// The audit log (a-roles timeline, DV-A16, R-AD-12, D-104). Read-only: entries are never edited or deleted. Newest
// first, with a keyset cursor on the insertion sequence so a long trail pages in constant time.

/// <summary>
/// <c>ActorName</c>: The acting account's display name (never an email or phone); null for the system.
/// <c>ShopNameAr</c>: The shop the action concerns, when it concerns one.
/// </summary>
public sealed record AuditEntryResponse(
    Guid Id,
    DateTimeOffset OccurredAt,
    Guid? ActorUserId,
    string? ActorName,
    string ActorType,
    string Action,
    string EntityType,
    string EntityId,
    Guid? ShopId,
    string? ShopNameAr,
    string? ShopNameEn,
    string? Summary,
    string? Reason,
    string? CorrelationId);

/// <summary>
/// <c>NextCursor</c>: Pass it back as <c>cursor</c> for the next (older) page; null on the last page.
/// </summary>
public sealed record AuditPageResponse(IReadOnlyList<AuditEntryResponse> Items, string? NextCursor);

/// <summary>The values the filters can take: every action code and entity type recorded so far.</summary>
public sealed record AuditFacetsResponse(IReadOnlyList<string> Actions, IReadOnlyList<string> EntityTypes);

internal sealed record ListAuditQuery(
    Guid? ActorUserId, string? Action, string? EntityType, string? EntityId, Guid? ShopId, DateTimeOffset? From, DateTimeOffset? To, string? Cursor, int? PageSize)
    : IQuery<AuditPageResponse>;

internal sealed record GetAuditFacetsQuery : IQuery<AuditFacetsResponse>;

internal sealed class ListAuditHandler(TrimmeDbContext db, IUserNameLookup users, IShopDirectory shops)
    : IQueryHandler<ListAuditQuery, AuditPageResponse>
{
    public async Task<AuditPageResponse> Handle(ListAuditQuery query, CancellationToken cancellationToken)
    {
        var size = new PageRequest(1, query.PageSize ?? 50).PageSize;
        var entries = db.Set<AuditEntry>().AsNoTracking();
        if (query.ActorUserId is { } actor)
        {
            entries = entries.Where(e => e.ActorUserId == actor);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            entries = entries.Where(e => e.Action == query.Action.Trim());
        }

        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            entries = entries.Where(e => e.EntityType == query.EntityType.Trim());
        }

        if (!string.IsNullOrWhiteSpace(query.EntityId))
        {
            entries = entries.Where(e => e.EntityId == query.EntityId.Trim());
        }

        if (query.ShopId is { } shop)
        {
            entries = entries.Where(e => e.ShopId == shop);
        }

        if (query.From is { } from)
        {
            entries = entries.Where(e => e.OccurredAt >= from);
        }

        if (query.To is { } to)
        {
            entries = entries.Where(e => e.OccurredAt < to);
        }

        if (AuditCursor.TryParse(query.Cursor, out var before))
        {
            entries = entries.Where(e => e.Sequence < before);
        }

        var page = await entries.OrderByDescending(e => e.Sequence).Take(size + 1).ToListAsync(cancellationToken);
        var more = page.Count > size;
        if (more)
        {
            page.RemoveAt(page.Count - 1);
        }

        var names = await users.FindAsync([.. page.Where(e => e.ActorUserId is not null).Select(e => e.ActorUserId!.Value)], cancellationToken);
        var shopNames = await shops.FindManyAsync([.. page.Where(e => e.ShopId is not null).Select(e => new ShopId(e.ShopId!.Value))], cancellationToken);

        var items = page.Select(e =>
        {
            var shopName = e.ShopId is { } id ? shopNames.GetValueOrDefault(new ShopId(id)) : null;
            return new AuditEntryResponse(
                e.Id.Value, e.OccurredAt, e.ActorUserId, e.ActorUserId is { } a ? names.GetValueOrDefault(a)?.DisplayName : null, e.ActorType, e.Action,
                e.EntityType, e.EntityId, e.ShopId, shopName?.NameAr, shopName?.NameEn, e.Summary, e.Reason, e.CorrelationId);
        }).ToList();
        return new AuditPageResponse(items, more ? AuditCursor.Format(page[^1].Sequence) : null);
    }
}

internal sealed class GetAuditFacetsHandler(TrimmeDbContext db) : IQueryHandler<GetAuditFacetsQuery, AuditFacetsResponse>
{
    public async Task<AuditFacetsResponse> Handle(GetAuditFacetsQuery query, CancellationToken cancellationToken)
    {
        var entries = db.Set<AuditEntry>().AsNoTracking();
        return new AuditFacetsResponse(
            await entries.Select(e => e.Action).Distinct().OrderBy(a => a).ToListAsync(cancellationToken),
            await entries.Select(e => e.EntityType).Distinct().OrderBy(t => t).ToListAsync(cancellationToken));
    }
}

/// <summary>An opaque cursor: the sequence of the last entry shown.</summary>
internal static class AuditCursor
{
    public static string Format(long sequence) => sequence.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static bool TryParse(string? cursor, out long sequence) =>
        long.TryParse(cursor, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out sequence) && sequence > 0;
}
