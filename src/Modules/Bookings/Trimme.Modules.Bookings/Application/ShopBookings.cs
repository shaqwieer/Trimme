using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Paging;
using Trimme.BuildingBlocks.Application.Scheduling;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Bookings.Domain;

namespace Trimme.Modules.Bookings.Application;

// The shop's own bookings (spec §13). The shop comes from ICurrentTenant; the tenant filter scopes every read, so another
// shop's booking id is 404. Responses carry the customer's name only, never a phone number (R-NEG-04).

internal sealed record ListShopBookingsQuery(DateOnly? From, DateOnly? To, BookingStatus? Status, Guid? ProfessionalId, string? Search, PageRequest Page)
    : IQuery<PagedResponse<ShopBookingResponse>?>;

internal sealed record GetShopBookingQuery(Guid BookingId) : IQuery<ShopBookingDetailResponse?>;

internal sealed record CreateWalkInCommand(
    Guid? ServiceId, Guid? PackageId, Guid ProfessionalId, DateTimeOffset? StartsAt, string CustomerName, string? Note, string? IdempotencyKey)
    : ICommand<Result<ShopBookingResponse>>;

internal sealed record TransitionShopBookingCommand(Guid BookingId, BookingStatus To, string? Reason, uint Version) : ICommand<Result<ShopBookingResponse>>;

internal sealed record AddBookingNoteCommand(Guid BookingId, string Text) : ICommand<Result<BookingNoteResponse>>;

/// <summary>Shared queries of the shop's booking views.</summary>
internal sealed class ShopBookingReader(TrimmeDbContext db, IAvailabilityChecker availability, TimeProvider clock)
{
    /// <summary>Maps bookings, flagging the active ones that no longer fit the schedule (DV-S22).</summary>
    public async Task<IReadOnlyList<ShopBookingResponse>> MapAsync(ShopId shopId, IReadOnlyList<Booking> bookings, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var active = bookings.Where(b => b.IsActive && b.EndsAt > now).Select(b => new ScheduledTime(b.Id.Value, b.ProfessionalId, b.StartsAt, b.EndsAt)).ToList();
        var outside = await availability.OutsideScheduleAsync(shopId, active, cancellationToken);
        return [.. bookings.Select(b => BookingMapping.ToShop(b, now, outside.Contains(b.Id.Value)))];
    }

    public async Task<ShopBookingDetailResponse> DetailAsync(Booking booking, CancellationToken cancellationToken)
    {
        var notes = await db.Set<BookingNote>().AsNoTracking().Where(n => n.BookingId == booking.Id).OrderBy(n => n.CreatedAt)
            .Select(n => new BookingNoteResponse(n.Id, n.Text, n.CreatedAt)).ToListAsync(cancellationToken);
        var mapped = (await MapAsync(booking.ShopId, [booking], cancellationToken))[0];
        return new ShopBookingDetailResponse(mapped, BookingMapping.History(booking), notes);
    }

    /// <summary>Filters shared by the shop and admin lists: local-date range, status, professional, name or reference.</summary>
    public static IQueryable<Booking> Filter(
        IQueryable<Booking> bookings, DateTimeOffset? from, DateTimeOffset? to, BookingStatus? status, Guid? professionalId, string? search)
    {
        if (from?.ToUniversalTime() is { } start)
        {
            bookings = bookings.Where(b => b.EndsAt > start);
        }

        if (to?.ToUniversalTime() is { } end)
        {
            bookings = bookings.Where(b => b.StartsAt < end);
        }

        if (status is { } s)
        {
            bookings = bookings.Where(b => b.Status == s);
        }

        if (professionalId is { } p)
        {
            var id = new ProfessionalId(p);
            bookings = bookings.Where(b => b.ProfessionalId == id);
        }

        // Search by the customer's name or the booking reference only — never by phone (DV-S18).
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var pattern = $"%{term}%";
            var reference = term.ToUpperInvariant();
            bookings = bookings.Where(b => EF.Functions.ILike(b.CustomerName, pattern) || b.Reference == reference);
        }

        return bookings;
    }
}

internal sealed class ListShopBookingsHandler(TrimmeDbContext db, ICurrentTenant tenant, IShopDirectory shops, ShopBookingReader reader)
    : IQueryHandler<ListShopBookingsQuery, PagedResponse<ShopBookingResponse>?>
{
    public async Task<PagedResponse<ShopBookingResponse>?> Handle(ListShopBookingsQuery query, CancellationToken cancellationToken)
    {
        if (tenant.ShopId is not { } shopId || await shops.FindAsync(shopId, cancellationToken) is not { } shop)
        {
            return null;
        }

        var zone = TimeZoneInfo.FindSystemTimeZoneById(shop.TimeZone);
        DateTimeOffset? Start(DateOnly? date) =>
            date is { } d ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), zone.GetUtcOffset(d.ToDateTime(TimeOnly.MinValue))).ToUniversalTime() : null;
        var bookings = ShopBookingReader.Filter(db.Set<Booking>().AsNoTracking(), Start(query.From), Start(query.To?.AddDays(1)), query.Status, query.ProfessionalId, query.Search)
            .OrderBy(b => b.StartsAt).ThenBy(b => b.Id);
        var total = await bookings.CountAsync(cancellationToken);
        var page = await bookings.Skip(query.Page.Skip).Take(query.Page.PageSize).ToListAsync(cancellationToken);
        return new PagedResponse<ShopBookingResponse>(await reader.MapAsync(shopId, page, cancellationToken), query.Page.Page, query.Page.PageSize, total);
    }
}

internal sealed class GetShopBookingHandler(TrimmeDbContext db, ICurrentTenant tenant, ShopBookingReader reader) : IQueryHandler<GetShopBookingQuery, ShopBookingDetailResponse?>
{
    public async Task<ShopBookingDetailResponse?> Handle(GetShopBookingQuery query, CancellationToken cancellationToken)
    {
        var id = new BookingId(query.BookingId);
        return tenant.ShopId is not null && await db.Set<Booking>().AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, cancellationToken) is { } booking
            ? await reader.DetailAsync(booking, cancellationToken)
            : null;
    }
}

/// <summary>
/// A walk-in (spec §11, R-BKG-07): the same collision checks as an online booking (hours, breaks, time off, closures,
/// bookings, and the exclusion constraint), at any minute and starting now if no start is given (D-035, D-088). The
/// subscription and pause gate does not apply (D-014). The customer's name only; no phone field in v1.
/// </summary>
internal sealed class CreateWalkInHandler(
    TrimmeDbContext db,
    ICurrentTenant tenant,
    ICurrentUser user,
    IdempotencyGate idempotency,
    IBookableOfferCatalog catalog,
    IProfessionalDirectory professionals,
    IAvailabilityChecker availability,
    ShopBookingReader reader,
    TimeProvider clock)
    : ICommandHandler<CreateWalkInCommand, Result<ShopBookingResponse>>
{
    private const string Scope = "bookings.walk_in";

    /// <summary>A start this far in the past is still "now" (the form was open for a moment).</summary>
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(5);

    public async Task<Result<ShopBookingResponse>> Handle(CreateWalkInCommand command, CancellationToken cancellationToken)
    {
        if (tenant.ShopId is not { } shopId || user.UserId is not { } userId)
        {
            return BookingErrors.NotFound();
        }

        if (string.IsNullOrWhiteSpace(command.CustomerName) || command.CustomerName.Trim().Length > BookingRules.MaxNameLength)
        {
            return Error.Validation("validation.failed", "The customer's name is required.",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["customerName"] = ["validation.required"] });
        }

        if (BookingRules.TooLong(command.Note, BookingRules.MaxNoteLength, "note") is { } tooLong)
        {
            return tooLong;
        }

        var now = clock.GetUtcNow();
        var startsNow = command.StartsAt is null;
        var start = command.StartsAt ?? now;
        if (start < now - Grace)
        {
            return BookingErrors.StartInPast();
        }

        var hash = IdempotencyRecord.Hash(command with { IdempotencyKey = null, StartsAt = command.StartsAt });
        if (command.IdempotencyKey is { } earlierKey && await idempotency.ReplayAsync(userId, Scope, earlierKey, hash, cancellationToken) is { } replay)
        {
            return replay.IsFailure ? replay.Error : await ExistingAsync(shopId, replay.Value, cancellationToken);
        }

        var offer = await catalog.FindAsync(shopId, command.ServiceId, command.PackageId, cancellationToken);
        if (offer is null)
        {
            return BookingErrors.OfferNotFound();
        }

        var professional = await professionals.FindAsync(new ProfessionalId(command.ProfessionalId), cancellationToken);
        if (professional is not { IsActive: true } || professional.ShopId != shopId)
        {
            return Error.NotFound("professional.not_found", "The professional was not found.");
        }

        if (!offer.EligibleProfessionalIds.Contains(professional.Id))
        {
            return BookingErrors.ProfessionalNotEligible();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        IdempotencyRecord? record = null;
        if (command.IdempotencyKey is { } key)
        {
            var claim = await idempotency.ClaimAsync(userId, Scope, key, hash, cancellationToken);
            if (!claim.Claimed)
            {
                await transaction.RollbackAsync(cancellationToken);
                var again = await idempotency.ReplayAsync(userId, Scope, key, hash, cancellationToken);
                return again is { IsSuccess: true } ? await ExistingAsync(shopId, again.Value, cancellationToken) : again?.Error ?? BookingErrors.SlotUnavailable();
            }

            record = claim.Record;
        }

        var free = await availability.FreeProfessionalsAsync(shopId, [professional.Id], start, offer.DurationMinutes, AvailabilityCheckMode.WalkIn, null, cancellationToken);
        if (free.Count == 0)
        {
            return BookingErrors.SlotUnavailable();
        }

        var booking = Booking.CreateWalkIn(
            EntityId.New<BookingId>(), shopId, command.CustomerName, new BookedProfessional(professional.Id, professional.NameAr, professional.NameEn),
            BookingMapping.Snapshot(offer), start, startsNow, command.Note, new BookingActor(userId, ActorType.ShopUser), now);
        db.Add(booking);
        record?.Complete(booking.Id.Value);
        BookingEvents.Add(db, BookingEvents.Created, booking, now);
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
        return (await reader.MapAsync(shopId, [booking], cancellationToken))[0];
    }

    private async Task<Result<ShopBookingResponse>> ExistingAsync(ShopId shopId, Guid bookingId, CancellationToken cancellationToken)
    {
        var id = new BookingId(bookingId);
        return await db.Set<Booking>().AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, cancellationToken) is { } booking
            ? (await reader.MapAsync(shopId, [booking], cancellationToken))[0]
            : BookingErrors.NotFound();
    }
}

/// <summary>Confirm, arrived, completed, no-show or cancel (with a reason), per the state machine and its time rules (D-087).</summary>
internal sealed class TransitionShopBookingHandler(TrimmeDbContext db, ICurrentTenant tenant, ICurrentUser user, ShopBookingReader reader, TimeProvider clock)
    : ICommandHandler<TransitionShopBookingCommand, Result<ShopBookingResponse>>
{
    public async Task<Result<ShopBookingResponse>> Handle(TransitionShopBookingCommand command, CancellationToken cancellationToken)
    {
        var id = new BookingId(command.BookingId);
        if (tenant.ShopId is not { } shopId || await db.Set<Booking>().SingleOrDefaultAsync(b => b.Id == id, cancellationToken) is not { } booking)
        {
            return BookingErrors.NotFound();
        }

        db.Entry(booking).Property(b => b.Version).OriginalValue = command.Version;
        var now = clock.GetUtcNow();
        var moved = booking.ApplyShopTransition(command.To, new BookingActor(user.UserId, ActorType.ShopUser), command.Reason, now);
        if (moved.IsFailure)
        {
            return moved.Error;
        }

        BookingEvents.Add(db, BookingEvents.ForStatus(booking.Status), booking, now);
        await db.SaveChangesAsync(cancellationToken);
        return (await reader.MapAsync(shopId, [booking], cancellationToken))[0];
    }
}

internal sealed class AddBookingNoteHandler(TrimmeDbContext db, ICurrentTenant tenant, ICurrentUser user, TimeProvider clock)
    : ICommandHandler<AddBookingNoteCommand, Result<BookingNoteResponse>>
{
    public async Task<Result<BookingNoteResponse>> Handle(AddBookingNoteCommand command, CancellationToken cancellationToken)
    {
        var id = new BookingId(command.BookingId);
        if (tenant.ShopId is not { } shopId || user.UserId is not { } userId
            || !await db.Set<Booking>().AnyAsync(b => b.Id == id, cancellationToken))
        {
            return BookingErrors.NotFound();
        }

        if (string.IsNullOrWhiteSpace(command.Text) || command.Text.Trim().Length > BookingRules.MaxNoteLength)
        {
            return Error.Validation("validation.failed", "The note is required (at most 500 characters).",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["text"] = ["validation.required"] });
        }

        var note = new BookingNote(Guid.CreateVersion7(), id, shopId, command.Text, userId, clock.GetUtcNow());
        db.Add(note);
        await db.SaveChangesAsync(cancellationToken);
        return new BookingNoteResponse(note.Id, note.Text, note.CreatedAt);
    }
}
