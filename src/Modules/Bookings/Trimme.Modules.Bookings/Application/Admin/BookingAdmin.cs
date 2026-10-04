using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Auditing;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Discovery;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Paging;
using Trimme.BuildingBlocks.Application.Scheduling;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Bookings.Domain;

namespace Trimme.Modules.Bookings.Application.Admin;

// Platform admins read bookings across shops and intervene on the shop's behalf (a-appointments, spec §14, R-AD-05,
// D-103): any state-machine transition or a reschedule, always with a reason, audited, through the same outbox as the
// shop's own changes. The customer's phone is never part of a booking response.

/// <summary>A page of bookings across shops, with the status chips' counts (every filter but the status applied).</summary>
public sealed record AdminBookingListResponse(IReadOnlyList<AdminBookingResponse> Items, int Page, int PageSize, int Total, ShopBookingCounts Counts);

/// <summary>One booking for an admin: the shop's view, its history and internal notes (read-only here).</summary>
public sealed record AdminBookingDetailResponse(AdminBookingResponse Booking, IReadOnlyList<BookingNoteResponse> Notes);

public sealed record AdminRescheduleProfessional(Guid Id, string NameAr, string NameEn);

public sealed record AdminRescheduleSlot(DateTimeOffset StartsAt, string LocalTime, string Period);

/// <summary>
/// Free starts on one shop-local date for the booking's duration and the chosen professional (the booking's own time does
/// not block it), and the professionals it may move to: the active ones assigned to the booked item, or only its own
/// professional when the item is no longer offered.
/// </summary>
public sealed record AdminRescheduleOptionsResponse(
    string TimeZone, DateOnly Date, Guid ProfessionalId, IReadOnlyList<AdminRescheduleProfessional> Professionals, IReadOnlyList<AdminRescheduleSlot> Slots);

internal sealed record ListAdminBookingsQuery(
    Guid? ShopId, IReadOnlyCollection<BookingStatus>? Statuses, DateTimeOffset? From, DateTimeOffset? To, BookingChannel? Channel, Guid? CustomerId, Guid? ProfessionalId,
    string? Search, bool Descending, PageRequest Page)
    : IQuery<AdminBookingListResponse>;

internal sealed record GetAdminBookingQuery(Guid BookingId) : IQuery<AdminBookingDetailResponse?>;

internal sealed record AdminTransitionBookingCommand(Guid BookingId, BookingStatus To, string? Reason, uint Version) : ICommand<Result<AdminBookingResponse>>;

internal sealed record AdminRescheduleBookingCommand(Guid BookingId, DateTimeOffset StartsAt, Guid? ProfessionalId, string? Reason, uint Version, string IdempotencyKey)
    : ICommand<Result<AdminRescheduleResult>>;

internal sealed record AdminRescheduleResult(AdminBookingResponse Booking, bool Replayed);

internal sealed record GetAdminRescheduleOptionsQuery(Guid BookingId, DateOnly? Date, Guid? ProfessionalId) : IQuery<Result<AdminRescheduleOptionsResponse>>;

internal sealed class AdminBookingMapper(IShopDirectory shops, TimeProvider clock)
{
    public async Task<IReadOnlyList<AdminBookingResponse>> MapAsync(IReadOnlyList<Booking> bookings, bool withHistory, CancellationToken cancellationToken)
    {
        var byId = await shops.FindManyAsync([.. bookings.Select(b => b.ShopId).Distinct()], cancellationToken);
        var now = clock.GetUtcNow();
        return
        [
            .. bookings.Select(b => new AdminBookingResponse(
                BookingMapping.Shop(byId.GetValueOrDefault(b.ShopId), b.ShopId), b.CustomerId, BookingMapping.ToShop(b, now, outsideSchedule: false),
                withHistory ? BookingMapping.History(b) : [])),
        ];
    }

    public async Task<AdminBookingResponse> MapOneAsync(Booking booking, CancellationToken cancellationToken) =>
        (await MapAsync([booking], withHistory: true, cancellationToken))[0];
}

internal static class AdminBookingRules
{
    public const int MinReasonLength = 5;

    public static Error? ReasonError(string? reason) =>
        (reason?.Trim().Length ?? 0) < MinReasonLength
            ? BookingErrors.ReasonRequired()
            : BookingRules.TooLong(reason, BookingRules.MaxReasonLength, "reason");
}

internal sealed class ListAdminBookingsHandler(TrimmeDbContext db, IAdminDataScope scope, AdminBookingMapper mapper)
    : IQueryHandler<ListAdminBookingsQuery, AdminBookingListResponse>
{
    public async Task<AdminBookingListResponse> Handle(ListAdminBookingsQuery query, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var bookings = db.Set<Booking>().AsNoTracking();
        if (query.ShopId is { } shop)
        {
            var shopId = new ShopId(shop);
            bookings = bookings.Where(b => b.ShopId == shopId);
        }

        if (query.Channel is { } channel)
        {
            bookings = bookings.Where(b => b.Channel == channel);
        }

        if (query.CustomerId is { } customer)
        {
            bookings = bookings.Where(b => b.CustomerId == customer);
        }

        var unfiltered = ShopBookingReader.Filter(bookings, query.From, query.To, null, query.ProfessionalId, query.Search);
        var byStatus = await unfiltered.GroupBy(b => b.Status).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        int Count(params BookingStatus[] statuses) => statuses.Sum(s => byStatus.GetValueOrDefault(s));
        var counts = new ShopBookingCounts(
            byStatus.Values.Sum(), Count(BookingStatus.Pending), Count(BookingStatus.Confirmed), Count(BookingStatus.Arrived), Count(BookingStatus.Completed),
            Count(BookingStatus.CancelledByCustomer, BookingStatus.CancelledByShop), Count(BookingStatus.NoShow));

        var filtered = ShopBookingReader.Filter(unfiltered, null, null, query.Statuses, null, null);
        var ordered = query.Descending
            ? filtered.OrderByDescending(b => b.StartsAt).ThenBy(b => b.Id)
            : filtered.OrderBy(b => b.StartsAt).ThenBy(b => b.Id);
        var total = await filtered.CountAsync(cancellationToken);
        var page = await ordered.Skip(query.Page.Skip).Take(query.Page.PageSize).ToListAsync(cancellationToken);
        return new AdminBookingListResponse(await mapper.MapAsync(page, withHistory: false, cancellationToken), query.Page.Page, query.Page.PageSize, total, counts);
    }
}

internal sealed class GetAdminBookingHandler(TrimmeDbContext db, IAdminDataScope scope, AdminBookingMapper mapper)
    : IQueryHandler<GetAdminBookingQuery, AdminBookingDetailResponse?>
{
    public async Task<AdminBookingDetailResponse?> Handle(GetAdminBookingQuery query, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var id = new BookingId(query.BookingId);
        if (await db.Set<Booking>().AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, cancellationToken) is not { } booking)
        {
            return null;
        }

        var notes = await db.Set<BookingNote>().AsNoTracking().Where(n => n.BookingId == booking.Id).OrderBy(n => n.CreatedAt)
            .Select(n => new BookingNoteResponse(n.Id, n.Text, n.CreatedAt)).ToListAsync(cancellationToken);
        return new AdminBookingDetailResponse(await mapper.MapOneAsync(booking, cancellationToken), notes);
    }
}

/// <summary>
/// An admin moves a booking along the state machine on the shop's behalf (D-016, D-103): the shop's own rules (Arrived
/// from an hour before, NoShow once started, never "cancelled by the customer"), a reason for every intervention, the
/// PlatformAdmin actor in the history, an audit entry and the outbox event.
/// </summary>
internal sealed class AdminTransitionBookingHandler(TrimmeDbContext db, IAdminDataScope scope, ICurrentUser user, IAuditLog audit, AdminBookingMapper mapper, TimeProvider clock)
    : ICommandHandler<AdminTransitionBookingCommand, Result<AdminBookingResponse>>
{
    public async Task<Result<AdminBookingResponse>> Handle(AdminTransitionBookingCommand command, CancellationToken cancellationToken)
    {
        if (AdminBookingRules.ReasonError(command.Reason) is { } reasonError)
        {
            return reasonError;
        }

        using var _ = scope.Begin();
        var id = new BookingId(command.BookingId);
        if (await db.Set<Booking>().SingleOrDefaultAsync(b => b.Id == id, cancellationToken) is not { } booking)
        {
            return BookingErrors.NotFound();
        }

        db.Entry(booking).Property(b => b.Version).OriginalValue = command.Version;
        var now = clock.GetUtcNow();
        var from = booking.Status;
        var moved = booking.ApplyShopTransition(command.To, new BookingActor(user.UserId, ActorType.PlatformAdmin), command.Reason, now);
        if (moved.IsFailure)
        {
            return moved.Error;
        }

        BookingEvents.Add(db, BookingEvents.ForStatus(booking.Status), booking, now);
        audit.Record(new AuditRecord(
            BookingRules.IsCancelled(booking.Status) ? "booking.cancelled_by_admin" : "booking.status_changed_by_admin",
            nameof(Booking), booking.Id.ToString(), booking.ShopId, $"{booking.Reference}: {from} → {booking.Status}", command.Reason!.Trim()));
        await db.SaveChangesAsync(cancellationToken);
        return await mapper.MapOneAsync(booking, cancellationToken);
    }
}

/// <summary>
/// An admin moves a Pending or Confirmed booking to another start, optionally with another professional (D-103). The
/// collision rules of the shop's desk apply (hours, breaks, time off, closures, other bookings, at any minute) but not
/// the customer's cutoff or the online-only gates; the start may not be in the past. Same transaction, idempotency,
/// exclusion constraint and outbox as the customer's reschedule (D-089): a lost race is 409 booking.slot_unavailable.
/// </summary>
internal sealed class AdminRescheduleBookingHandler(
    TrimmeDbContext db,
    IAdminDataScope scope,
    ICurrentUser user,
    IAuditLog audit,
    IdempotencyGate idempotency,
    IBookableOfferCatalog catalog,
    IProfessionalDirectory professionals,
    IAvailabilityChecker availability,
    AdminBookingMapper mapper,
    TimeProvider clock)
    : ICommandHandler<AdminRescheduleBookingCommand, Result<AdminRescheduleResult>>
{
    private const string Scope = "bookings.admin_reschedule";

    public async Task<Result<AdminRescheduleResult>> Handle(AdminRescheduleBookingCommand command, CancellationToken cancellationToken)
    {
        if (user.UserId is not { } adminId)
        {
            return BookingErrors.NotFound();
        }

        if (AdminBookingRules.ReasonError(command.Reason) is { } reasonError)
        {
            return reasonError;
        }

        using var _ = scope.Begin();
        var hash = IdempotencyRecord.Hash(command with { IdempotencyKey = string.Empty, Version = 0 });
        if (await ReplayAsync(adminId, command.IdempotencyKey, hash, cancellationToken) is { } replayed)
        {
            return replayed;
        }

        var now = clock.GetUtcNow();
        if (command.StartsAt <= now)
        {
            return BookingErrors.StartInPast();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var (claimed, record) = await idempotency.ClaimAsync(adminId, Scope, command.IdempotencyKey, hash, cancellationToken);
        if (!claimed)
        {
            await transaction.RollbackAsync(cancellationToken);
            return await ReplayAsync(adminId, command.IdempotencyKey, hash, cancellationToken) ?? BookingErrors.SlotUnavailable();
        }

        var id = new BookingId(command.BookingId);
        if (await db.Set<Booking>().SingleOrDefaultAsync(b => b.Id == id, cancellationToken) is not { } booking)
        {
            return BookingErrors.NotFound();
        }

        db.Entry(booking).Property(b => b.Version).OriginalValue = command.Version;
        var target = await AdminRescheduleTargets.ResolveAsync(booking, command.ProfessionalId, catalog, professionals, cancellationToken);
        if (target.IsFailure)
        {
            return target.Error;
        }

        var free = await availability.FreeProfessionalsAsync(
            booking.ShopId, [target.Value.Id], command.StartsAt, booking.DurationMinutes, AvailabilityCheckMode.WalkIn, booking.Id.Value, cancellationToken);
        if (free.Count == 0)
        {
            return BookingErrors.SlotUnavailable();
        }

        var previous = booking.StartsAt;
        var moved = booking.Reschedule(command.StartsAt, target.Value, new BookingActor(adminId, ActorType.PlatformAdmin), now, cutoffMinutes: null, command.Reason);
        if (moved.IsFailure)
        {
            return moved.Error;
        }

        record!.Complete(booking.Id.Value);
        BookingEvents.Add(db, BookingEvents.Rescheduled, booking, now);
        audit.Record(new AuditRecord(
            "booking.rescheduled_by_admin", nameof(Booking), booking.Id.ToString(), booking.ShopId,
            $"{booking.Reference}: {previous:yyyy-MM-dd'T'HH:mm'Z'} → {booking.StartsAt:yyyy-MM-dd'T'HH:mm'Z'}", command.Reason!.Trim()));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (DatabaseErrors.IsLostRace(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            return BookingErrors.SlotUnavailable();
        }

        await transaction.CommitAsync(cancellationToken);
        return new AdminRescheduleResult(await mapper.MapOneAsync(booking, cancellationToken), Replayed: false);
    }

    private async Task<Result<AdminRescheduleResult>?> ReplayAsync(Guid adminId, string key, string hash, CancellationToken cancellationToken)
    {
        var replay = await idempotency.ReplayAsync(adminId, Scope, key, hash, cancellationToken);
        if (replay is null)
        {
            return null;
        }

        if (replay.IsFailure)
        {
            return replay.Error;
        }

        var id = new BookingId(replay.Value);
        return await db.Set<Booking>().AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, cancellationToken) is { } booking
            ? new AdminRescheduleResult(await mapper.MapOneAsync(booking, cancellationToken), Replayed: true)
            : BookingErrors.NotFound();
    }
}

/// <summary>Who may take a rescheduled booking: its own professional, or another active one assigned to the booked item.</summary>
internal static class AdminRescheduleTargets
{
    public static async Task<IReadOnlyList<ProfessionalSummary>> CandidatesAsync(
        Booking booking, IBookableOfferCatalog catalog, IProfessionalDirectory professionals, CancellationToken cancellationToken)
    {
        var staff = await professionals.ListByShopAsync(booking.ShopId, cancellationToken);
        var offer = await catalog.FindBookedAsync(
            booking.ShopId, booking.ServiceId, booking.PackageId, [.. booking.PackageItems.Select(i => i.ServiceId)], cancellationToken);
        var eligible = offer?.EligibleProfessionalIds.ToHashSet() ?? [];
        return [.. staff.Where(p => p.Id == booking.ProfessionalId || (p.IsActive && eligible.Contains(p.Id)))];
    }

    public static async Task<Result<BookedProfessional>> ResolveAsync(
        Booking booking, Guid? professionalId, IBookableOfferCatalog catalog, IProfessionalDirectory professionals, CancellationToken cancellationToken)
    {
        var requested = new ProfessionalId(professionalId ?? booking.ProfessionalId.Value);
        var candidates = await CandidatesAsync(booking, catalog, professionals, cancellationToken);
        if (candidates.SingleOrDefault(p => p.Id == requested) is not { } professional)
        {
            return requested == booking.ProfessionalId
                ? Error.NotFound("professional.not_found", "The professional was not found.")
                : BookingErrors.ProfessionalNotEligible();
        }

        if (!professional.IsActive)
        {
            return BookingErrors.ProfessionalNotEligible();
        }

        return new BookedProfessional(professional.Id, professional.NameAr, professional.NameEn);
    }
}

/// <summary>The reschedule dialog's choices: professionals it may move to and the free starts on one date (online grid, the booking ignored).</summary>
internal sealed class GetAdminRescheduleOptionsHandler(
    TrimmeDbContext db,
    IAdminDataScope scope,
    IShopDirectory shops,
    IBookableOfferCatalog catalog,
    IProfessionalDirectory professionals,
    ISlotProbe probe,
    TimeProvider clock)
    : IQueryHandler<GetAdminRescheduleOptionsQuery, Result<AdminRescheduleOptionsResponse>>
{
    private const int MaxSlots = 1000;

    public async Task<Result<AdminRescheduleOptionsResponse>> Handle(GetAdminRescheduleOptionsQuery query, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var id = new BookingId(query.BookingId);
        if (await db.Set<Booking>().AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, cancellationToken) is not { } booking
            || await shops.FindAsync(booking.ShopId, cancellationToken) is not { } shop)
        {
            return BookingErrors.NotFound();
        }

        if (booking.Status is not (BookingStatus.Pending or BookingStatus.Confirmed))
        {
            return BookingErrors.NotReschedulable(booking.Status);
        }

        var candidates = (await AdminRescheduleTargets.CandidatesAsync(booking, catalog, professionals, cancellationToken)).Where(p => p.IsActive).ToList();
        var chosen = new ProfessionalId(query.ProfessionalId ?? booking.ProfessionalId.Value);
        if (candidates.All(p => p.Id != chosen))
        {
            return BookingErrors.ProfessionalNotEligible();
        }

        var today = probe.Today(shop.TimeZone, clock.GetUtcNow());
        var date = query.Date ?? today;
        if (date < today)
        {
            return BookingErrors.StartInPast();
        }

        var slots = await probe.ProbeAsync(shop.Id, shop.TimeZone, booking.DurationMinutes, [chosen], date, date, MaxSlots, cancellationToken, booking.Id.Value);
        return new AdminRescheduleOptionsResponse(
            shop.TimeZone, date, chosen.Value, [.. candidates.Select(p => new AdminRescheduleProfessional(p.Id.Value, p.NameAr, p.NameEn))],
            [.. slots.Select(s => new AdminRescheduleSlot(s.StartsAt, s.LocalTime, s.Period))]);
    }
}
