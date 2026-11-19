using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Loyalty;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Paging;
using Trimme.BuildingBlocks.Application.Privacy;
using Trimme.BuildingBlocks.Application.Scheduling;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Privacy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Bookings.Domain;

namespace Trimme.Modules.Bookings.Application;

// The shop's own bookings (spec §13). The shop comes from ICurrentTenant; the tenant filter scopes every read, so another
// shop's booking id is 404. Responses carry the customer's name only, never a phone number (R-NEG-04): the mobile the
// desk takes for a walk-in goes one way, into an encrypted row only the platform team reads (D-134).

internal sealed record ListShopBookingsQuery(
    DateOnly? From, DateOnly? To, IReadOnlyCollection<BookingStatus>? Statuses, Guid? ProfessionalId, string? Search, PageRequest Page)
    : IQuery<ShopBookingListResponse?>;

internal sealed record GetShopBookingQuery(Guid BookingId) : IQuery<ShopBookingDetailResponse?>;

/// <summary>The shop's bookings that need any barber (D-147): pending ones to accept or reject, and red ones to close.</summary>
internal sealed record GetShopAttentionQuery : IQuery<ShopAttentionResponse?>;

/// <summary>
/// Pending bookings (oldest start first), red bookings («متأخر»: open 15 minutes past their end) and those closed while
/// red («انقفل متأخر», with who closed them), all started within the last 3 days, whoever's barber they are (D-147).
/// Every barber of the shop sees them and may act on the open ones; the owner sees them read-only.
/// </summary>
public sealed record ShopAttentionResponse(
    IReadOnlyList<ShopBookingResponse> Pending, IReadOnlyList<ShopBookingResponse> Overdue, IReadOnlyList<ShopBookingResponse> ClosedLate);

internal sealed record CreateWalkInCommand(
    Guid? ServiceId, Guid? PackageId, Guid ProfessionalId, DateTimeOffset? StartsAt, string CustomerName, string? CustomerPhone, string? Note, string? IdempotencyKey,
    IReadOnlyList<Guid> ServiceIds, BookingChannel Channel = BookingChannel.WalkIn, bool UseLoyaltyReward = false)
    : ICommand<Result<ShopBookingResponse>>
{
    /// <summary>Never print the number (records list every member by default).</summary>
    public override string ToString() => $"{nameof(CreateWalkInCommand)} {{ ProfessionalId = {ProfessionalId}, StartsAt = {StartsAt}, Channel = {Channel} }}";
}

internal sealed record TransitionShopBookingCommand(Guid BookingId, BookingStatus To, string? Reason, uint Version) : ICommand<Result<ShopBookingResponse>>;

internal sealed record AddBookingNoteCommand(Guid BookingId, string Text) : ICommand<Result<BookingNoteResponse>>;

/// <summary>Shared queries of the shop's booking views.</summary>
internal sealed class ShopBookingReader(
    TrimmeDbContext db, IAvailabilityChecker availability, CustomerVisits visits, ICurrentUser user, IPersonalDataProtector protector, TimeProvider clock)
{
    /// <summary>
    /// Maps bookings, flagging the active ones that no longer fit the schedule (DV-S22); for the owner, new or returning
    /// customers (D-135); for a barber, the mobile of an open walk-in or phone booking (D-147).
    /// </summary>
    public async Task<IReadOnlyList<ShopBookingResponse>> MapAsync(ShopId shopId, IReadOnlyList<Booking> bookings, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var active = bookings.Where(b => b.IsActive && b.EndsAt > now).Select(b => new ScheduledTime(b.Id.Value, b.ProfessionalId, b.StartsAt, b.EndsAt)).ToList();
        var outside = await availability.OutsideScheduleAsync(shopId, active, cancellationToken);
        var labels = await visits.LabelsAsync(bookings, cancellationToken);
        var phones = await PhonesForBarberAsync(bookings, cancellationToken);
        var offers = await LatestOffersAsync(bookings, cancellationToken);
        return [.. bookings.Select(b => BookingMapping.ToShop(
                b, now, outside.Contains(b.Id.Value), labels.TryGetValue(b.Id, out var visit) ? visit : null, b.AllowedFor(user, now))
            with
            {
                CustomerPhone = phones.GetValueOrDefault(b.Id),
                Postponement = offers.TryGetValue(b.Id, out var offer) ? BookingEvents.Offer(offer, b, now) : null,
            })];
    }

    /// <summary>Each online booking's latest postponement offer (D-147), in one query.</summary>
    private async Task<IReadOnlyDictionary<BookingId, PostponementOffer>> LatestOffersAsync(IReadOnlyList<Booking> bookings, CancellationToken cancellationToken)
    {
        var ids = bookings.Where(b => b.Channel == BookingChannel.Online).Select(b => b.Id).ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<BookingId, PostponementOffer>();
        }

        var offers = await db.Set<PostponementOffer>().AsNoTracking().Where(o => ids.Contains(o.BookingId)).ToListAsync(cancellationToken);
        return offers.GroupBy(o => o.BookingId).ToDictionary(g => g.Key, g => g.OrderByDescending(o => o.CreatedAt).First());
    }

    /// <summary>
    /// The one reviewed way a customer's number reaches a shop (D-147, the client's table, overriding D-134 and spec §7 for
    /// shop-recorded bookings only): a barber's login reads the mobile typed into a walk-in or phone booking while that
    /// booking is open (Pending, Confirmed or Arrived). Never for the owner, never for an online customer, never once the
    /// booking is completed, missed or cancelled; never in an event, a notification or a log.
    /// </summary>
    private async Task<IReadOnlyDictionary<BookingId, string>> PhonesForBarberAsync(IReadOnlyList<Booking> bookings, CancellationToken cancellationToken)
    {
        if (user.Own() is null)
        {
            return new Dictionary<BookingId, string>();
        }

        var ids = bookings.Where(b => b.IsShopRecorded && b.IsActive).Select(b => b.Id).ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<BookingId, string>();
        }

        var contacts = await db.Set<WalkInContact>().AsNoTracking().Where(c => ids.Contains(c.BookingId))
            .Select(c => new { c.BookingId, c.ProtectedPhone }).ToListAsync(cancellationToken);
        return contacts.ToDictionary(c => c.BookingId, c => protector.Unprotect(c.ProtectedPhone, PersonalDataPurposes.WalkInPhone));
    }

    public async Task<ShopBookingDetailResponse> DetailAsync(Booking booking, CancellationToken cancellationToken)
    {
        var notes = await db.Set<BookingNote>().AsNoTracking().Where(n => n.BookingId == booking.Id).OrderBy(n => n.CreatedAt)
            .Select(n => new BookingNoteResponse(n.Id, n.Text, n.CreatedAt)).ToListAsync(cancellationToken);
        var mapped = (await MapAsync(booking.ShopId, [booking], cancellationToken))[0];
        return new ShopBookingDetailResponse(mapped, BookingMapping.History(booking), notes);
    }

    /// <summary>Filters shared by the shop and admin lists: local-date range, statuses (any of), professional, name or reference.</summary>
    public static IQueryable<Booking> Filter(
        IQueryable<Booking> bookings, DateTimeOffset? from, DateTimeOffset? to, IReadOnlyCollection<BookingStatus>? statuses, Guid? professionalId, string? search)
    {
        if (from?.ToUniversalTime() is { } start)
        {
            bookings = bookings.Where(b => b.EndsAt > start);
        }

        if (to?.ToUniversalTime() is { } end)
        {
            bookings = bookings.Where(b => b.StartsAt < end);
        }

        if (statuses is { Count: > 0 })
        {
            var wanted = statuses.Distinct().ToArray();
            bookings = bookings.Where(b => wanted.Contains(b.Status));
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

/// <summary>
/// The appointments list (s-appointments, DV-S08/DV-S18): local-date range, any of several statuses, professional, name
/// or reference search, paged by start. The status chips' counts use every filter except the status, so each chip shows
/// what it would list; the cancelled chip counts both cancellation statuses.
/// </summary>
internal sealed class ListShopBookingsHandler(TrimmeDbContext db, ICurrentTenant tenant, ICurrentUser user, IShopDirectory shops, ShopBookingReader reader)
    : IQueryHandler<ListShopBookingsQuery, ShopBookingListResponse?>
{
    public async Task<ShopBookingListResponse?> Handle(ListShopBookingsQuery query, CancellationToken cancellationToken)
    {
        if (tenant.ShopId is not { } shopId || await shops.FindAsync(shopId, cancellationToken) is not { } shop)
        {
            return null;
        }

        var zone = TimeZoneInfo.FindSystemTimeZoneById(shop.TimeZone);
        DateTimeOffset? Start(DateOnly? date) =>
            date is { } d ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), zone.GetUtcOffset(d.ToDateTime(TimeOnly.MinValue))).ToUniversalTime() : null;
        var unfiltered = ShopBookingReader.Filter(
            db.Set<Booking>().AsNoTracking().OwnOnly(user), Start(query.From), Start(query.To?.AddDays(1)), null, query.ProfessionalId, query.Search);
        var byStatus = await unfiltered.GroupBy(b => b.Status).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        int Count(params BookingStatus[] statuses) => statuses.Sum(s => byStatus.GetValueOrDefault(s));
        var counts = new ShopBookingCounts(
            byStatus.Values.Sum(),
            Count(BookingStatus.Pending),
            Count(BookingStatus.Confirmed),
            Count(BookingStatus.Arrived),
            Count(BookingStatus.Completed),
            Count(BookingStatus.CancelledByCustomer, BookingStatus.CancelledByShop),
            Count(BookingStatus.NoShow));

        var bookings = ShopBookingReader.Filter(unfiltered, null, null, query.Statuses, null, null).OrderBy(b => b.StartsAt).ThenBy(b => b.Id);
        var total = await bookings.CountAsync(cancellationToken);
        var page = await bookings.Skip(query.Page.Skip).Take(query.Page.PageSize).ToListAsync(cancellationToken);
        return new ShopBookingListResponse(await reader.MapAsync(shopId, page, cancellationToken), query.Page.Page, query.Page.PageSize, total, counts);
    }
}

internal sealed class GetShopBookingHandler(TrimmeDbContext db, ICurrentTenant tenant, ICurrentUser user, ShopBookingReader reader, TimeProvider clock)
    : IQueryHandler<GetShopBookingQuery, ShopBookingDetailResponse?>
{
    public async Task<ShopBookingDetailResponse?> Handle(GetShopBookingQuery query, CancellationToken cancellationToken)
    {
        var id = new BookingId(query.BookingId);
        return tenant.ShopId is not null
            && await db.Set<Booking>().AsNoTracking().Reachable(user, clock.GetUtcNow()).SingleOrDefaultAsync(b => b.Id == id, cancellationToken) is { } booking
            ? await reader.DetailAsync(booking, cancellationToken)
            : null;
    }
}

internal sealed class GetShopAttentionHandler(TrimmeDbContext db, ICurrentTenant tenant, ShopBookingReader reader, TimeProvider clock)
    : IQueryHandler<GetShopAttentionQuery, ShopAttentionResponse?>
{
    /// <summary>Enough for any shop's three days; a list this long is a shop that stopped closing its bookings.</summary>
    private const int Limit = 200;

    public async Task<ShopAttentionResponse?> Handle(GetShopAttentionQuery query, CancellationToken cancellationToken)
    {
        if (tenant.ShopId is not { } shopId)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        var bookings = await db.Set<Booking>().AsNoTracking().NeedingAttention(now)
            .OrderBy(b => b.StartsAt).ThenBy(b => b.Id).Take(Limit).ToListAsync(cancellationToken);

        // Closed after they turned red: the last change (the closing one) came after the end plus the grace. The exact
        // rule is the domain's (ClosedLate), applied after loading.
        var visibleFrom = now - BookingRules.OverdueVisibleFor;
        var open = BookingRules.Active.ToArray();
        var grace = (int)BookingRules.OverdueAfter.TotalMinutes;
        var closed = await db.Set<Booking>().AsNoTracking()
            .Where(b => b.StartsAt >= visibleFrom && !open.Contains(b.Status) && b.UpdatedAt > b.EndsAt.AddMinutes(grace))
            .OrderByDescending(b => b.UpdatedAt).Take(Limit).ToListAsync(cancellationToken);
        var mapped = await reader.MapAsync(shopId, [.. bookings, .. closed.Where(b => b.ClosedLate)], cancellationToken);
        return new ShopAttentionResponse(
            [.. mapped.Where(b => b.Status == BookingStatus.Pending && !b.Overdue)],
            [.. mapped.Where(b => b.Overdue)],
            [.. mapped.Where(b => b.ClosedLate)]);
    }
}

/// <summary>
/// A walk-in or phone booking (spec §11, R-BKG-07, D-147): the same collision checks as an online booking (hours, breaks,
/// time off, closures, bookings, and the exclusion constraint), at any minute and starting now if no start is given
/// (D-035, D-088; a phone booking always names its start). The subscription and pause gate does not apply (D-014). A
/// service, several services or a package; for the barber themselves or a free colleague (the availability check is what
/// makes them free). The customer's name, and their mobile if they give it (optional, D-134): the mobile is stored
/// encrypted in <see cref="WalkInContact"/> and is not in the outbox event or any notification; barbers read it on the
/// booking until it is closed (D-147). The request hash for the idempotency key uses the number's keyed hash, so no plain
/// hash of it is stored either.
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
    IPersonalDataProtector protector,
    BookingActors actors,
    ShopRulesReader rules,
    ILoyaltyRewardHolder loyalty,
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

        PhoneNumber? phone = null;
        if (!string.IsNullOrWhiteSpace(command.CustomerPhone) && !PhoneNumber.TryParseMobile(command.CustomerPhone, out phone))
        {
            return Error.Validation("validation.failed", "The customer's mobile number is invalid.",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["customerPhone"] = ["validation.phone_invalid"] });
        }

        if (BookingRules.TooLong(command.Note, BookingRules.MaxNoteLength, "note") is { } tooLong)
        {
            return tooLong;
        }

        if (command.Channel is not (BookingChannel.WalkIn or BookingChannel.Phone))
        {
            return Error.Validation("validation.failed", "A shop booking is a walk-in or a phone booking.",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["channel"] = ["validation.invalid"] });
        }

        if (command.Channel == BookingChannel.Phone && command.StartsAt is null)
        {
            return Error.Validation("validation.failed", "A phone booking needs its start.",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["startsAt"] = ["validation.required"] });
        }

        if (BookableOfferRequest.Services(command.ServiceId, command.ServiceIds, command.PackageId) is not { } services)
        {
            return Error.Validation("validation.failed", "Choose up to 10 services, or one package.",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["serviceIds"] = ["validation.invalid"] });
        }

        // A reward for the typed number (D-149): one per booking, never on a package.
        if (command.UseLoyaltyReward && phone is null)
        {
            return LoyaltyRewardErrors.PhoneRequired();
        }

        if (command.UseLoyaltyReward && command.PackageId is not null)
        {
            return LoyaltyRewardErrors.NotWithPackage();
        }

        var now = clock.GetUtcNow();
        var startsNow = command.StartsAt is null;
        var start = command.StartsAt ?? now;
        if (start < now - Grace)
        {
            return BookingErrors.StartInPast();
        }

        var phoneHash = phone is null ? null : protector.LookupHash(phone.E164, PersonalDataPurposes.WalkInPhone);
        var hash = IdempotencyRecord.Hash(command with { IdempotencyKey = null, StartsAt = command.StartsAt, CustomerPhone = phoneHash });
        if (command.IdempotencyKey is { } earlierKey && await idempotency.ReplayAsync(userId, Scope, earlierKey, hash, cancellationToken) is { } replay)
        {
            return replay.IsFailure ? replay.Error : await ExistingAsync(shopId, replay.Value, cancellationToken);
        }

        if (await catalog.OverlapsPackageAsync(shopId, services, command.PackageId, cancellationToken))
        {
            return BookableOfferRequest.ServiceInPackage();
        }

        var offer = await catalog.FindAsync(shopId, services, command.PackageId, cancellationToken);
        if (offer is null)
        {
            return BookingErrors.OfferNotFound();
        }

        // A barber may book themselves or a colleague of the same shop (D-147); the availability check below is what
        // makes the colleague "free".
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

        // The barber's own length (D-149), and the shop's cleaning time after it.
        var free = await availability.FreeProfessionalsAsync(
            shopId, [professional.Id], start, offer.DurationFor(professional.Id), AvailabilityCheckMode.WalkIn, null, cancellationToken);
        if (free.Count == 0)
        {
            return BookingErrors.SlotUnavailable();
        }

        var bookingId = EntityId.New<BookingId>();
        HeldPointsReward? pointsReward = null;
        if (command.UseLoyaltyReward)
        {
            var booked = offer.For(professional.Id);
            var held = await loyalty.HoldPointsRewardAsync(
                new PointsRewardHold(shopId, bookingId.Value, null, phone!.E164, booked.Price, BookingMapping.ServicePrices(booked), now), cancellationToken);
            if (held.IsFailure)
            {
                return held.Error;
            }

            pointsReward = held.Value;
        }

        var booking = Booking.CreateWalkIn(
            bookingId, shopId, command.CustomerName, new BookedProfessional(professional.Id, professional.NameAr, professional.NameEn),
            BookingMapping.Snapshot(offer, professional.Id), start, startsNow, command.Note, await actors.CurrentAsync(ActorType.ShopUser, cancellationToken), now,
            command.Channel, (await rules.GetAsync(shopId, cancellationToken)).BufferMinutes);
        if (pointsReward is not null)
        {
            booking.UsePointsReward(pointsReward.RedemptionId, pointsReward.TitleAr, pointsReward.TitleEn, pointsReward.Discount);
        }

        db.Add(booking);
        if (phone is not null)
        {
            db.Add(new WalkInContact(booking.Id, shopId, protector.Protect(phone.E164, PersonalDataPurposes.WalkInPhone), phoneHash!, phone.Masked, now));
        }

        record?.Complete(booking.Id.Value);
        BookingEvents.Add(db, BookingEvents.Created, booking, now);
        try
        {
            await BookingReferences.SaveNewAsync(db, booking, cancellationToken);
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

/// <summary>
/// Confirm, arrived, completed, no-show or cancel (with a reason), per the state machine and its time rules (D-087,
/// D-147). A barber acts on their own bookings, and on a colleague's only to accept or reject it while pending or to close
/// it once red (<see cref="BarberScope.AllowedFor"/>); the actor's name goes into the history.
/// </summary>
internal sealed class TransitionShopBookingHandler(
    TrimmeDbContext db, ICurrentTenant tenant, ICurrentUser user, ShopBookingReader reader, BookingActors actors, TimeProvider clock)
    : ICommandHandler<TransitionShopBookingCommand, Result<ShopBookingResponse>>
{
    public async Task<Result<ShopBookingResponse>> Handle(TransitionShopBookingCommand command, CancellationToken cancellationToken)
    {
        var id = new BookingId(command.BookingId);
        var now = clock.GetUtcNow();
        if (tenant.ShopId is not { } shopId || await db.Set<Booking>().Reachable(user, now).SingleOrDefaultAsync(b => b.Id == id, cancellationToken) is not { } booking)
        {
            return BookingErrors.NotFound();
        }

        if (user.Own() is { } own && booking.ProfessionalId != own && !booking.AllowedFor(user, now).Contains(command.To))
        {
            return BookingErrors.InvalidTransition(booking.Status, command.To);
        }

        db.Entry(booking).Property(b => b.Version).OriginalValue = command.Version;
        var moved = booking.ApplyShopTransition(command.To, await actors.CurrentAsync(ActorType.ShopUser, cancellationToken), command.Reason, now);
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
            || !await db.Set<Booking>().OwnOnly(user).AnyAsync(b => b.Id == id, cancellationToken))
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
