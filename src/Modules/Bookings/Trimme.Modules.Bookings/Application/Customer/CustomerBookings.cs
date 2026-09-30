using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Bookings;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Paging;
using Trimme.BuildingBlocks.Application.Platform;
using Trimme.BuildingBlocks.Application.Qr;
using Trimme.BuildingBlocks.Application.Scheduling;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Bookings.Domain;

namespace Trimme.Modules.Bookings.Application.Customer;

// Customer use cases (spec §12, D-085). The data layer lets a customer read and change only their own bookings; the
// availability recheck runs inside the public scope of the booked shop so it sees every booking of the professional.

public enum BookingsTab
{
    Upcoming,
    Past,
}

/// <summary><c>QrVisitId</c> is the scan in the browser's attribution cookie, if any (R-QR-02); it never makes the booking fail.</summary>
internal sealed record CreateOnlineBookingCommand(
    string ShopSlug, Guid? ServiceId, Guid? PackageId, Guid? ProfessionalId, DateTimeOffset StartsAt, string? Note, string IdempotencyKey,
    Guid? QrVisitId = null)
    : ICommand<Result<CustomerBookingResult>>;

internal sealed record RescheduleMyBookingCommand(Guid BookingId, DateTimeOffset StartsAt, Guid? ProfessionalId, uint Version, string IdempotencyKey)
    : ICommand<Result<CustomerBookingResult>>;

internal sealed record CancelMyBookingCommand(Guid BookingId, string? Reason, uint Version) : ICommand<Result<CustomerBookingResponse>>;

internal sealed record ListMyBookingsQuery(BookingsTab Tab, PageRequest Page) : IQuery<PagedResponse<CustomerBookingResponse>>;

internal sealed record GetMyBookingQuery(Guid BookingId) : IQuery<CustomerBookingResponse?>;

internal sealed record GetMyBookingCalendarQuery(Guid BookingId) : IQuery<string?>;

/// <summary>The booking and whether it replays an earlier request with the same idempotency key.</summary>
internal sealed record CustomerBookingResult(CustomerBookingResponse Booking, bool Replayed);

/// <summary>Shared steps of the customer commands.</summary>
internal sealed class CustomerBookingSupport(
    TrimmeDbContext db,
    ICurrentCustomer customer,
    IShopDirectory shops,
    IPublicDataScope scope,
    IPlatformSettings settings,
    IReviewLookup reviews,
    TimeProvider clock)
{
    public Guid? CustomerId => customer.CustomerId;

    public DateTimeOffset Now => clock.GetUtcNow();

    public async Task<int> CutoffAsync(CancellationToken cancellationToken) => (await settings.GetAsync(cancellationToken)).CancellationCutoffMinutes;

    public async Task<ShopSummary?> ShopBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        using (scope.Begin(shopId: null))
        {
            return await shops.FindBySlugAsync(slug, cancellationToken) is { Status: ShopStatus.Active } shop ? shop : null;
        }
    }

    public async Task<ShopSummary?> ShopAsync(ShopId shopId, CancellationToken cancellationToken)
    {
        using (scope.Begin(shopId: null))
        {
            return await shops.FindAsync(shopId, cancellationToken);
        }
    }

    public Task<Booking?> MineAsync(Guid bookingId, bool track, CancellationToken cancellationToken)
    {
        var id = new BookingId(bookingId);
        var query = track ? db.Set<Booking>() : db.Set<Booking>().AsNoTracking();
        return query.SingleOrDefaultAsync(b => b.Id == id, cancellationToken);
    }

    /// <summary>The policy, the clock and the customer's own ratings for these bookings, read once per response.</summary>
    public async Task<CustomerBookingView> ViewAsync(IReadOnlyCollection<Booking> bookings, CancellationToken cancellationToken)
    {
        var platform = await settings.GetAsync(cancellationToken);
        var completed = bookings.Where(b => b.Status == BookingStatus.Completed).Select(b => b.Id.Value).ToList();
        var ratings = completed.Count == 0 ? new Dictionary<Guid, int>() : await reviews.RatingsByBookingAsync(completed, cancellationToken);
        return new CustomerBookingView(Now, platform.CancellationCutoffMinutes, platform.ReviewWindowDays, ratings);
    }

    public async Task<CustomerBookingResponse> ResponseAsync(Booking booking, CancellationToken cancellationToken) =>
        BookingMapping.ToCustomer(booking, await ShopAsync(booking.ShopId, cancellationToken), await ViewAsync([booking], cancellationToken));
}

/// <summary>
/// Chooses who takes the booking among the free candidates (D-012): fewest active bookings that local day, then the
/// stable id order. The booking always stores one concrete professional.
/// </summary>
internal sealed class ProfessionalPicker(TrimmeDbContext db)
{
    public async Task<ProfessionalId> PickAsync(ShopId shopId, IReadOnlyList<ProfessionalId> free, DateTimeOffset dayStart, DateTimeOffset dayEnd, CancellationToken cancellationToken)
    {
        if (free.Count == 1)
        {
            return free[0];
        }

        var ids = free.ToArray();
        var active = BookingRules.Active.ToArray();
        var counts = await db.Set<Booking>().AsNoTracking()
            .Where(b => b.ShopId == shopId && ids.Contains(b.ProfessionalId) && active.Contains(b.Status) && b.StartsAt >= dayStart && b.StartsAt < dayEnd)
            .GroupBy(b => b.ProfessionalId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        return free.OrderBy(p => counts.GetValueOrDefault(p)).ThenBy(p => p.Value).First();
    }
}

internal sealed class CreateOnlineBookingHandler(
    TrimmeDbContext db,
    CustomerBookingSupport support,
    IdempotencyGate idempotency,
    ICustomerDirectory customers,
    IPublicDataScope scope,
    IShopBookability bookability,
    IBookableOfferCatalog catalog,
    IProfessionalDirectory professionals,
    IAvailabilityChecker availability,
    ProfessionalPicker picker,
    IQrAttributionResolver qr)
    : ICommandHandler<CreateOnlineBookingCommand, Result<CustomerBookingResult>>
{
    private const string Scope = "bookings.create";

    public async Task<Result<CustomerBookingResult>> Handle(CreateOnlineBookingCommand command, CancellationToken cancellationToken)
    {
        if (support.CustomerId is not { } customerId)
        {
            return BookingErrors.NotFound();
        }

        if (BookingRules.TooLong(command.Note, BookingRules.MaxNoteLength, "note") is { } tooLong)
        {
            return tooLong;
        }

        // The attribution cookie is not part of the request's identity: a retry whose cookie changed still replays.
        var hash = IdempotencyRecord.Hash(command with { IdempotencyKey = string.Empty, QrVisitId = null });
        if (await ReplayAsync(customerId, command.IdempotencyKey, hash, cancellationToken) is { } replayed)
        {
            return replayed;
        }

        if (await customers.FindAsync(customerId, cancellationToken) is not { IsActive: true, DisplayName: { Length: > 0 } customerName })
        {
            return BookingErrors.ProfileIncomplete();
        }

        if (await support.ShopBySlugAsync(command.ShopSlug, cancellationToken) is not { } shop)
        {
            return Error.NotFound("shop.not_found", "The shop was not found.");
        }

        // A scan of this shop's code within the attribution window credits the booking; anything else is an ordinary booking.
        var scan = command.QrVisitId is { } visitId ? await qr.ResolveAsync(visitId, shop.Id, cancellationToken) : null;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var (claimed, record) = await idempotency.ClaimAsync(customerId, Scope, command.IdempotencyKey, hash, cancellationToken);
        if (!claimed)
        {
            await transaction.RollbackAsync(cancellationToken);
            return await ReplayAsync(customerId, command.IdempotencyKey, hash, cancellationToken) ?? BookingErrors.SlotUnavailable();
        }

        // The recheck (R-BKG-03): the same rules as the slot list, read inside this transaction.
        BookableOffer? offer;
        BookedProfessional? chosen;
        using (scope.Begin(shop.Id))
        {
            var gate = await bookability.GetAsync(shop.Id, cancellationToken);
            if (!gate.AcceptsOnlineBookings)
            {
                return BookingErrors.ShopNotAccepting(gate.BlockedReason);
            }

            offer = await catalog.FindAsync(shop.Id, command.ServiceId, command.PackageId, cancellationToken);
            if (offer is null)
            {
                return BookingErrors.OfferNotFound();
            }

            if (!offer.OnlineBookable)
            {
                return BookingErrors.OfferNotOnlineBookable();
            }

            var staff = (await professionals.ListByShopAsync(shop.Id, cancellationToken)).Where(p => p.IsActive).ToDictionary(p => p.Id);
            var eligible = offer.EligibleProfessionalIds.Where(staff.ContainsKey).Distinct().ToList();
            if (command.ProfessionalId is { } requested)
            {
                if (!staff.ContainsKey(new ProfessionalId(requested)))
                {
                    return Error.NotFound("professional.not_found", "The professional was not found.");
                }

                if (!eligible.Contains(new ProfessionalId(requested)))
                {
                    return BookingErrors.ProfessionalNotEligible();
                }

                eligible = [new ProfessionalId(requested)];
            }

            var free = await availability.FreeProfessionalsAsync(shop.Id, eligible, command.StartsAt, offer.DurationMinutes, AvailabilityCheckMode.Online, null, cancellationToken);
            if (free.Count == 0)
            {
                return BookingErrors.SlotUnavailable();
            }

            var zone = TimeZoneInfo.FindSystemTimeZoneById(shop.TimeZone);
            var localDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(command.StartsAt, zone).DateTime);
            var dayStart = new DateTimeOffset(localDay.ToDateTime(TimeOnly.MinValue), zone.GetUtcOffset(localDay.ToDateTime(TimeOnly.MinValue))).ToUniversalTime();
            var picked = staff[await picker.PickAsync(shop.Id, free, dayStart, dayStart.AddDays(1), cancellationToken)];
            chosen = new BookedProfessional(picked.Id, picked.NameAr, picked.NameEn);
        }

        var now = support.Now;
        var booking = Booking.CreateOnline(
            EntityId.New<BookingId>(), shop.Id, customerId, customerName, chosen, BookingMapping.Snapshot(offer), command.StartsAt,
            shop.RequireManualConfirmation, command.Note, now, scan is null ? null : (new QrCodeLinkId(scan.LinkId), scan.VisitId));
        db.Add(booking);
        record!.Complete(booking.Id.Value);
        BookingEvents.Add(db, BookingEvents.Created, booking, now);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (DatabaseErrors.IsLostRace(exception))
        {
            // Another request took the time between the recheck and the insert (R-BKG-04): the database decided.
            await transaction.RollbackAsync(cancellationToken);
            return BookingErrors.SlotUnavailable();
        }

        await transaction.CommitAsync(cancellationToken);
        return new CustomerBookingResult(BookingMapping.ToCustomer(booking, shop, await support.ViewAsync([booking], cancellationToken)), Replayed: false);
    }

    private async Task<Result<CustomerBookingResult>?> ReplayAsync(Guid customerId, string key, string hash, CancellationToken cancellationToken)
    {
        var replay = await idempotency.ReplayAsync(customerId, Scope, key, hash, cancellationToken);
        if (replay is null)
        {
            return null;
        }

        if (replay.IsFailure)
        {
            return replay.Error;
        }

        return await support.MineAsync(replay.Value, track: false, cancellationToken) is { } booking
            ? new CustomerBookingResult(await support.ResponseAsync(booking, cancellationToken), Replayed: true)
            : BookingErrors.NotFound();
    }
}

internal sealed class RescheduleMyBookingHandler(
    TrimmeDbContext db,
    CustomerBookingSupport support,
    IdempotencyGate idempotency,
    IPublicDataScope scope,
    IShopBookability bookability,
    IBookableOfferCatalog catalog,
    IProfessionalDirectory professionals,
    IAvailabilityChecker availability)
    : ICommandHandler<RescheduleMyBookingCommand, Result<CustomerBookingResult>>
{
    private const string Scope = "bookings.reschedule";

    public async Task<Result<CustomerBookingResult>> Handle(RescheduleMyBookingCommand command, CancellationToken cancellationToken)
    {
        if (support.CustomerId is not { } customerId)
        {
            return BookingErrors.NotFound();
        }

        var hash = IdempotencyRecord.Hash(command with { IdempotencyKey = string.Empty, Version = 0 });
        if (await ReplayAsync(customerId, command.IdempotencyKey, hash, cancellationToken) is { } replayed)
        {
            return replayed;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var (claimed, record) = await idempotency.ClaimAsync(customerId, Scope, command.IdempotencyKey, hash, cancellationToken);
        if (!claimed)
        {
            await transaction.RollbackAsync(cancellationToken);
            return await ReplayAsync(customerId, command.IdempotencyKey, hash, cancellationToken) ?? BookingErrors.SlotUnavailable();
        }

        if (await support.MineAsync(command.BookingId, track: true, cancellationToken) is not { } booking)
        {
            return BookingErrors.NotFound();
        }

        db.Entry(booking).Property(b => b.Version).OriginalValue = command.Version;
        var now = support.Now;
        var cutoff = await support.CutoffAsync(cancellationToken);
        if (!booking.CustomerCanChange(now, cutoff))
        {
            return booking.Status is BookingStatus.Pending or BookingStatus.Confirmed ? BookingErrors.CutoffPassed(cutoff) : BookingErrors.NotReschedulable(booking.Status);
        }

        BookedProfessional target;
        using (scope.Begin(booking.ShopId))
        {
            var gate = await bookability.GetAsync(booking.ShopId, cancellationToken);
            if (!gate.AcceptsOnlineBookings)
            {
                return BookingErrors.ShopNotAccepting(gate.BlockedReason);
            }

            var offer = await catalog.FindAsync(booking.ShopId, booking.ServiceId, booking.PackageId, cancellationToken);
            if (offer is null)
            {
                return BookingErrors.OfferNotFound();
            }

            var requested = new ProfessionalId(command.ProfessionalId ?? booking.ProfessionalId.Value);
            var staff = (await professionals.ListByShopAsync(booking.ShopId, cancellationToken)).Where(p => p.IsActive).ToDictionary(p => p.Id);
            if (!staff.TryGetValue(requested, out var professional))
            {
                return Error.NotFound("professional.not_found", "The professional was not found.");
            }

            if (!offer.EligibleProfessionalIds.Contains(requested))
            {
                return BookingErrors.ProfessionalNotEligible();
            }

            // The booked duration (snapshot) is kept; its own current time does not block the move.
            var free = await availability.FreeProfessionalsAsync(
                booking.ShopId, [requested], command.StartsAt, booking.DurationMinutes, AvailabilityCheckMode.Online, booking.Id.Value, cancellationToken);
            if (free.Count == 0)
            {
                return BookingErrors.SlotUnavailable();
            }

            target = new BookedProfessional(professional.Id, professional.NameAr, professional.NameEn);
        }

        var moved = booking.Reschedule(command.StartsAt, target, new BookingActor(customerId, ActorType.Customer), now, cutoff);
        if (moved.IsFailure)
        {
            return moved.Error;
        }

        record!.Complete(booking.Id.Value);
        BookingEvents.Add(db, BookingEvents.Rescheduled, booking, now);
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
        return new CustomerBookingResult(await support.ResponseAsync(booking, cancellationToken), Replayed: false);
    }

    private async Task<Result<CustomerBookingResult>?> ReplayAsync(Guid customerId, string key, string hash, CancellationToken cancellationToken)
    {
        var replay = await idempotency.ReplayAsync(customerId, Scope, key, hash, cancellationToken);
        if (replay is null)
        {
            return null;
        }

        if (replay.IsFailure)
        {
            return replay.Error;
        }

        return await support.MineAsync(replay.Value, track: false, cancellationToken) is { } booking
            ? new CustomerBookingResult(await support.ResponseAsync(booking, cancellationToken), Replayed: true)
            : BookingErrors.NotFound();
    }
}

internal sealed class CancelMyBookingHandler(TrimmeDbContext db, CustomerBookingSupport support)
    : ICommandHandler<CancelMyBookingCommand, Result<CustomerBookingResponse>>
{
    public async Task<Result<CustomerBookingResponse>> Handle(CancelMyBookingCommand command, CancellationToken cancellationToken)
    {
        if (support.CustomerId is not { } customerId || await support.MineAsync(command.BookingId, track: true, cancellationToken) is not { } booking)
        {
            return BookingErrors.NotFound();
        }

        db.Entry(booking).Property(b => b.Version).OriginalValue = command.Version;
        var now = support.Now;
        var cancelled = booking.CancelByCustomer(customerId, command.Reason, now, await support.CutoffAsync(cancellationToken));
        if (cancelled.IsFailure)
        {
            return cancelled.Error;
        }

        BookingEvents.Add(db, BookingEvents.Cancelled, booking, now);
        await db.SaveChangesAsync(cancellationToken);
        return await support.ResponseAsync(booking, cancellationToken);
    }
}

internal sealed class ListMyBookingsHandler(TrimmeDbContext db, CustomerBookingSupport support, IShopDirectory shops, IPublicDataScope scope)
    : IQueryHandler<ListMyBookingsQuery, PagedResponse<CustomerBookingResponse>>
{
    public async Task<PagedResponse<CustomerBookingResponse>> Handle(ListMyBookingsQuery query, CancellationToken cancellationToken)
    {
        if (support.CustomerId is null)
        {
            return new PagedResponse<CustomerBookingResponse>([], query.Page.Page, query.Page.PageSize, 0);
        }

        var now = support.Now;
        var active = BookingRules.Active.ToArray();
        var bookings = db.Set<Booking>().AsNoTracking();
        bookings = query.Tab == BookingsTab.Upcoming
            ? bookings.Where(b => active.Contains(b.Status) && b.EndsAt >= now).OrderBy(b => b.StartsAt)
            : bookings.Where(b => !active.Contains(b.Status) || b.EndsAt < now).OrderByDescending(b => b.StartsAt);
        var total = await bookings.CountAsync(cancellationToken);
        var page = await bookings.Skip(query.Page.Skip).Take(query.Page.PageSize).ToListAsync(cancellationToken);

        IReadOnlyDictionary<ShopId, ShopSummary> shopsById;
        using (scope.Begin(shopId: null))
        {
            shopsById = await shops.FindManyAsync([.. page.Select(b => b.ShopId)], cancellationToken);
        }

        var view = await support.ViewAsync(page, cancellationToken);
        return new PagedResponse<CustomerBookingResponse>(
            [.. page.Select(b => BookingMapping.ToCustomer(b, shopsById.GetValueOrDefault(b.ShopId), view))], query.Page.Page, query.Page.PageSize, total);
    }
}

internal sealed class GetMyBookingHandler(CustomerBookingSupport support) : IQueryHandler<GetMyBookingQuery, CustomerBookingResponse?>
{
    public async Task<CustomerBookingResponse?> Handle(GetMyBookingQuery query, CancellationToken cancellationToken) =>
        support.CustomerId is not null && await support.MineAsync(query.BookingId, track: false, cancellationToken) is { } booking
            ? await support.ResponseAsync(booking, cancellationToken)
            : null;
}

/// <summary>An iCalendar file of the customer's booking (spec §12, "add to calendar"). No contact data.</summary>
internal sealed class GetMyBookingCalendarHandler(CustomerBookingSupport support) : IQueryHandler<GetMyBookingCalendarQuery, string?>
{
    public async Task<string?> Handle(GetMyBookingCalendarQuery query, CancellationToken cancellationToken)
    {
        if (support.CustomerId is null || await support.MineAsync(query.BookingId, track: false, cancellationToken) is not { } booking)
        {
            return null;
        }

        var shop = await support.ShopAsync(booking.ShopId, cancellationToken);
        static string Stamp(DateTimeOffset value) => value.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        static string Text(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace(",", "\\,", StringComparison.Ordinal)
            .Replace(";", "\\;", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);

        var builder = new StringBuilder()
            .Append("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//TRIMME//Bookings//AR\r\nCALSCALE:GREGORIAN\r\nMETHOD:PUBLISH\r\n")
            .Append("BEGIN:VEVENT\r\n")
            .Append(CultureInfo.InvariantCulture, $"UID:{booking.Id}@trimme\r\n")
            .Append(CultureInfo.InvariantCulture, $"DTSTAMP:{Stamp(support.Now)}\r\n")
            .Append(CultureInfo.InvariantCulture, $"DTSTART:{Stamp(booking.StartsAt)}\r\n")
            .Append(CultureInfo.InvariantCulture, $"DTEND:{Stamp(booking.EndsAt)}\r\n")
            .Append(CultureInfo.InvariantCulture, $"SUMMARY:{Text(booking.ItemNameAr)} — {Text(shop?.NameAr ?? string.Empty)}\r\n")
            .Append(CultureInfo.InvariantCulture, $"DESCRIPTION:{Text($"{booking.ProfessionalNameAr} · {booking.Reference}")}\r\n")
            .Append(CultureInfo.InvariantCulture, $"STATUS:{(BookingRules.IsCancelled(booking.Status) ? "CANCELLED" : "CONFIRMED")}\r\n")
            .Append("END:VEVENT\r\nEND:VCALENDAR\r\n");
        return builder.ToString();
    }
}
