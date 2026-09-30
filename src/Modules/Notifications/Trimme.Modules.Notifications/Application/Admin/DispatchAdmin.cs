using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Auditing;
using Trimme.BuildingBlocks.Application.Jobs;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Paging;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Notifications.Domain;

namespace Trimme.Modules.Notifications.Application.Admin;

// The WhatsApp dispatch log (DV-A13, R-AD-10, R-NTF-07, D-110): filters, the masked recipient, the template version that
// rendered each message, attempts and errors, an audited retry for failed dispatches, and a booking's messages and
// reminder jobs for the admin booking page.

/// <summary>A dispatch as the log lists it: masked recipient only, never the number.</summary>
public sealed record DispatchResponse(
    Guid Id,
    DispatchKind Kind,
    Guid? BookingId,
    Guid? ShopId,
    MessageEvent Event,
    MessageAudience Audience,
    string Locale,
    Guid TemplateId,
    int TemplateVersionNumber,
    string RecipientMasked,
    DispatchStatus Status,
    int Attempts,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? FailedAt);

/// <summary>A dispatch with the rendered text (until the retention period ends) and its hash.</summary>
public sealed record DispatchDetailResponse(
    DispatchResponse Dispatch,
    string? Body,
    IReadOnlyList<RenderedButton> Buttons,
    string ContentHash,
    DateTimeOffset? ContentPurgedAt,
    Guid TemplateVersionId,
    string? ProviderMessageId);

public sealed record DispatchCounts(int All, int Queued, int Sent, int Delivered, int Read, int Failed);

/// <summary>Messages recorded in the last 24 hours and the share delivered or read, in percent (one decimal).</summary>
public sealed record DispatchStats(int Last24Hours, double DeliveryRate);

public sealed record DispatchListResponse(IReadOnlyList<DispatchResponse> Items, int Page, int PageSize, int Total, DispatchCounts Counts, DispatchStats Stats);

public sealed record ReminderResponse(Guid Id, MessageAudience Audience, DateTimeOffset StartsAt, DateTimeOffset DueAt, ReminderStatus Status);

public sealed record BookingNotificationsResponse(IReadOnlyList<DispatchResponse> Dispatches, IReadOnlyList<ReminderResponse> Reminders);

internal sealed record ListDispatchesQuery(
    DispatchStatus? Status, MessageAudience? Audience, MessageEvent? Event, DispatchKind? Kind, Guid? ShopId, Guid? BookingId, PageRequest Page)
    : IQuery<DispatchListResponse>;

internal sealed record GetDispatchQuery(Guid DispatchId) : IQuery<Result<DispatchDetailResponse>>;

internal sealed record RetryDispatchCommand(Guid DispatchId) : ICommand<Result<DispatchResponse>>;

internal sealed record GetBookingNotificationsQuery(Guid BookingId) : IQuery<BookingNotificationsResponse>;

internal static class DispatchMapping
{
    public static DispatchResponse ToResponse(WhatsAppDispatch d) =>
        new(d.Id.Value, d.Kind, d.BookingId, d.ShopId?.Value, d.Event, d.Audience, d.Locale, d.TemplateId.Value, d.TemplateVersionNumber, d.RecipientMasked,
            d.Status, d.Attempts, d.LastError, d.CreatedAt, d.SentAt, d.DeliveredAt, d.FailedAt);
}

internal sealed class ListDispatchesHandler(TrimmeDbContext db, TimeProvider clock) : IQueryHandler<ListDispatchesQuery, DispatchListResponse>
{
    public async Task<DispatchListResponse> Handle(ListDispatchesQuery query, CancellationToken cancellationToken)
    {
        var dispatches = db.Set<WhatsAppDispatch>().AsNoTracking();
        if (query.Audience is { } audience)
        {
            dispatches = dispatches.Where(d => d.Audience == audience);
        }

        if (query.Event is { } @event)
        {
            dispatches = dispatches.Where(d => d.Event == @event);
        }

        if (query.Kind is { } kind)
        {
            dispatches = dispatches.Where(d => d.Kind == kind);
        }

        if (query.ShopId is { } shop)
        {
            var shopId = new ShopId(shop);
            dispatches = dispatches.Where(d => d.ShopId == shopId);
        }

        if (query.BookingId is { } booking)
        {
            dispatches = dispatches.Where(d => d.BookingId == booking);
        }

        var byStatus = await dispatches.GroupBy(d => d.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        int Count(DispatchStatus status) => byStatus.FirstOrDefault(s => s.Key == status)?.Count ?? 0;
        var counts = new DispatchCounts(
            byStatus.Sum(s => s.Count), Count(DispatchStatus.Queued), Count(DispatchStatus.Sent), Count(DispatchStatus.Delivered), Count(DispatchStatus.Read),
            Count(DispatchStatus.Failed));

        var since = clock.GetUtcNow().AddHours(-24);
        var recent = await db.Set<WhatsAppDispatch>().AsNoTracking().Where(d => d.CreatedAt >= since && d.Kind != DispatchKind.Test)
            .GroupBy(d => d.Status == DispatchStatus.Delivered || d.Status == DispatchStatus.Read)
            .Select(g => new { Delivered = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var recentTotal = recent.Sum(r => r.Count);
        var delivered = recent.Where(r => r.Delivered).Sum(r => r.Count);
        var stats = new DispatchStats(recentTotal, recentTotal == 0 ? 0 : Math.Round(100.0 * delivered / recentTotal, 1));

        var listed = query.Status is { } status ? dispatches.Where(d => d.Status == status) : dispatches;
        var total = await listed.CountAsync(cancellationToken);
        var page = await listed.OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.Id)
            .Skip(query.Page.Skip).Take(query.Page.PageSize).ToListAsync(cancellationToken);
        return new DispatchListResponse([.. page.Select(DispatchMapping.ToResponse)], query.Page.Page, query.Page.PageSize, total, counts, stats);
    }
}

internal sealed class GetDispatchHandler(TrimmeDbContext db) : IQueryHandler<GetDispatchQuery, Result<DispatchDetailResponse>>
{
    public async Task<Result<DispatchDetailResponse>> Handle(GetDispatchQuery query, CancellationToken cancellationToken)
    {
        var id = DispatchId.From(query.DispatchId);
        return await db.Set<WhatsAppDispatch>().AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, cancellationToken) is { } d
            ? new DispatchDetailResponse(DispatchMapping.ToResponse(d), d.Body, d.Buttons, d.ContentHash, d.ContentPurgedAt, d.TemplateVersionId.Value, d.ProviderMessageId)
            : DispatchErrors.NotFound();
    }
}

/// <summary>A failed dispatch goes back to Queued and a send job is enqueued after the commit (audited).</summary>
internal sealed class RetryDispatchHandler(TrimmeDbContext db, IAuditLog audit, IJobScheduler jobs)
    : ICommandHandler<RetryDispatchCommand, Result<DispatchResponse>>
{
    public async Task<Result<DispatchResponse>> Handle(RetryDispatchCommand command, CancellationToken cancellationToken)
    {
        var id = DispatchId.From(command.DispatchId);
        if (await db.Set<WhatsAppDispatch>().SingleOrDefaultAsync(d => d.Id == id, cancellationToken) is not { } dispatch)
        {
            return DispatchErrors.NotFound();
        }

        var retried = dispatch.Retry();
        if (retried.IsFailure)
        {
            return retried.Error!;
        }

        audit.Record(new AuditRecord(
            "whatsapp_dispatch.retried", "WhatsAppDispatch", dispatch.Id.Value.ToString(), dispatch.ShopId,
            Summary: $"{dispatch.Event} · {dispatch.Audience}; attempt {(dispatch.Attempts + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
        await db.SaveChangesAsync(cancellationToken);

        dispatch.AssignJob(Jobs.SendDispatchJob.Enqueue(jobs, dispatch.Id.Value));
        await db.SaveChangesAsync(cancellationToken);
        return DispatchMapping.ToResponse(dispatch);
    }
}

internal sealed class GetBookingNotificationsHandler(TrimmeDbContext db) : IQueryHandler<GetBookingNotificationsQuery, BookingNotificationsResponse>
{
    public async Task<BookingNotificationsResponse> Handle(GetBookingNotificationsQuery query, CancellationToken cancellationToken)
    {
        var dispatches = await db.Set<WhatsAppDispatch>().AsNoTracking().Where(d => d.BookingId == query.BookingId)
            .OrderBy(d => d.CreatedAt).ThenBy(d => d.Id).ToListAsync(cancellationToken);
        var reminders = await db.Set<ReminderSchedule>().AsNoTracking().Where(r => r.BookingId == query.BookingId)
            .OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
            .Select(r => new ReminderResponse(r.Id.Value, r.Audience, r.StartsAt, r.DueAt, r.Status))
            .ToListAsync(cancellationToken);
        return new BookingNotificationsResponse([.. dispatches.Select(DispatchMapping.ToResponse)], reminders);
    }
}
