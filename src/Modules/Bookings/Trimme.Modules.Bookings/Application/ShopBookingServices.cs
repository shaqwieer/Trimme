using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Scheduling;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Bookings.Domain;

namespace Trimme.Modules.Bookings.Application;

// A booking's services at the visit (D-147: «تعديل خدمات الحجز»): the barber adds a service once the customer has arrived
// (the server refuses it before) and takes one off until «انتهت الخدمة». Their own bookings only; the owner watches.

internal sealed record AddBookingServiceCommand(Guid BookingId, Guid ServiceId, uint Version) : ICommand<Result<ShopBookingResponse>>;

internal sealed record RemoveBookingServiceCommand(Guid BookingId, Guid ServiceId, uint Version) : ICommand<Result<ShopBookingResponse>>;

internal sealed record GetBookingServiceCandidatesQuery(Guid BookingId) : IQuery<Result<BookingServiceCandidatesResponse>>;

/// <summary>
/// One service the barber could add (D-151, the client's 9-2): its price, the barber's own length, and whether it ends
/// before their next booked or held time (<c>Fits</c>; one that does not is greyed and cannot be chosen).
/// </summary>
public sealed record BookingServiceCandidateResponse(
    Guid ServiceId, string NameAr, string? NameEn, Guid? SectionId, decimal Price, string Currency, int DurationMinutes, bool Fits);

/// <summary>
/// What may be added to the booking at the visit: <c>RoomMinutes</c> is the time free after it (with its cleaning time)
/// until the barber's next booking or held offer, null when nothing follows within a day.
/// </summary>
public sealed record BookingServiceCandidatesResponse(int? RoomMinutes, IReadOnlyList<BookingServiceCandidateResponse> Services);

/// <summary>
/// The one rule for how much a booking can grow at the visit (D-147, D-151), shared by the add and its candidates so they
/// cannot drift: the minutes from the booking's blocked time (its end plus the cleaning time) to the start of the barber's
/// next active booking or live postponement hold that reaches past it. A service fits when its length is at most that.
/// </summary>
internal static class BookingRoom
{
    /// <summary>Nothing booked within this long after the booking counts as nothing following.</summary>
    private static readonly TimeSpan Horizon = TimeSpan.FromDays(1);

    public static async Task<int?> MinutesAfterAsync(IBookedTimeReader booked, Booking booking, DateTimeOffset blockedUntil, CancellationToken cancellationToken)
    {
        var busy = await booked.GetBusyAsync(booking.ShopId, [booking.ProfessionalId], blockedUntil, blockedUntil + Horizon, cancellationToken);
        var next = busy.Where(t => t.BookingId != booking.Id.Value).Select(t => (DateTimeOffset?)t.StartsAt).Min();
        return next is { } start ? (int)Math.Floor(Math.Max(0, (start - blockedUntil).TotalMinutes)) : null;
    }

    public static bool Fits(int? roomMinutes, int minutes) => roomMinutes is not { } room || minutes <= room;
}

/// <summary>
/// Adds one of the shop's services that the booking's barber does, at its own price and length. The extra time must be
/// free of the barber's other open bookings and live postponement holds (<see cref="BookingRoom"/>): checked here, and by
/// the exclusion constraint and the hold triggers when two changes race (409 <c>booking.professional_busy</c>, so the
/// barber moves the next booking first). The schedule (hours, breaks) is not checked: the customer is in the chair, and
/// the barber decides.
/// </summary>
internal sealed class AddBookingServiceHandler(
    TrimmeDbContext db, ICurrentTenant tenant, ICurrentUser user, IBookableOfferCatalog catalog, IBookedTimeReader booked, ShopBookingReader reader,
    BookingActors actors, TimeProvider clock)
    : ICommandHandler<AddBookingServiceCommand, Result<ShopBookingResponse>>
{
    public async Task<Result<ShopBookingResponse>> Handle(AddBookingServiceCommand command, CancellationToken cancellationToken)
    {
        var id = new BookingId(command.BookingId);
        if (tenant.ShopId is not { } shopId || await db.Set<Booking>().OwnOnly(user).SingleOrDefaultAsync(b => b.Id == id, cancellationToken) is not { } booking)
        {
            return BookingErrors.NotFound();
        }

        if (await catalog.FindAsync(shopId, command.ServiceId, null, cancellationToken) is not { IsPackage: false } offer)
        {
            return BookingErrors.OfferNotFound();
        }

        if (!offer.EligibleProfessionalIds.Contains(booking.ProfessionalId))
        {
            return BookingErrors.ProfessionalNotEligible();
        }

        if (offer.Currency != booking.Currency)
        {
            return BookingErrors.OfferNotFound();
        }

        db.Entry(booking).Property(b => b.Version).OriginalValue = command.Version;
        var now = clock.GetUtcNow();
        var oldEnd = booking.BlockedUntil;

        // At the barber's own length for the service (D-149); the price stays the shop's.
        var minutes = offer.DurationFor(booking.ProfessionalId);
        var added = booking.AddService(
            BookedPackageItem.Of(offer.Id, offer.NameAr, offer.NameEn, offer.Price, minutes),
            await actors.CurrentAsync(ActorType.ShopUser, cancellationToken), now);
        if (added.IsFailure)
        {
            return added.Error;
        }

        if (!BookingRoom.Fits(await BookingRoom.MinutesAfterAsync(booked, booking, oldEnd, cancellationToken), minutes))
        {
            return BookingErrors.ProfessionalBusy();
        }

        BookingEvents.Add(db, BookingEvents.ServicesChanged, booking, now);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (DatabaseErrors.IsExclusionViolation(exception))
        {
            return BookingErrors.ProfessionalBusy();
        }

        return (await reader.MapAsync(shopId, [booking], cancellationToken))[0];
    }
}

/// <summary>
/// The services the barber could add to their own booking (D-151): the shop's published services they do, not already on
/// it, in its currency, each at the barber's own length, and whether it fits before their next booked or held time by the
/// add's own rule (<see cref="BookingRoom"/>). Another barber's booking is 404, as for the add.
/// </summary>
internal sealed class GetBookingServiceCandidatesHandler(
    TrimmeDbContext db, ICurrentTenant tenant, ICurrentUser user, IBookableOfferCatalog catalog, IBookedTimeReader booked)
    : IQueryHandler<GetBookingServiceCandidatesQuery, Result<BookingServiceCandidatesResponse>>
{
    public async Task<Result<BookingServiceCandidatesResponse>> Handle(GetBookingServiceCandidatesQuery query, CancellationToken cancellationToken)
    {
        var id = new BookingId(query.BookingId);
        if (tenant.ShopId is not { } shopId
            || await db.Set<Booking>().AsNoTracking().OwnOnly(user).SingleOrDefaultAsync(b => b.Id == id, cancellationToken) is not { } booking)
        {
            return BookingErrors.NotFound();
        }

        var onIt = booking.PackageItems.Select(i => i.ServiceId).ToHashSet();
        if (booking.ServiceId is { } single)
        {
            onIt.Add(single);
        }

        var room = await BookingRoom.MinutesAfterAsync(booked, booking, booking.BlockedUntil, cancellationToken);
        var services = await catalog.ServicesForAsync(shopId, booking.ProfessionalId, cancellationToken);
        return new BookingServiceCandidatesResponse(
            room,
            [
                .. services.Where(s => s.Currency == booking.Currency && !onIt.Contains(s.ServiceId))
                    .Select(s => new BookingServiceCandidateResponse(
                        s.ServiceId, s.NameAr, s.NameEn, s.SectionId, s.Price, s.Currency, s.DurationMinutes, BookingRoom.Fits(room, s.DurationMinutes))),
            ]);
    }
}

/// <summary>Takes a service off an open booking (see <see cref="Booking.RemoveService"/>); the time it held is free at once.</summary>
internal sealed class RemoveBookingServiceHandler(
    TrimmeDbContext db, ICurrentTenant tenant, ICurrentUser user, ShopBookingReader reader, BookingActors actors, TimeProvider clock)
    : ICommandHandler<RemoveBookingServiceCommand, Result<ShopBookingResponse>>
{
    public async Task<Result<ShopBookingResponse>> Handle(RemoveBookingServiceCommand command, CancellationToken cancellationToken)
    {
        var id = new BookingId(command.BookingId);
        if (tenant.ShopId is not { } shopId || await db.Set<Booking>().OwnOnly(user).SingleOrDefaultAsync(b => b.Id == id, cancellationToken) is not { } booking)
        {
            return BookingErrors.NotFound();
        }

        db.Entry(booking).Property(b => b.Version).OriginalValue = command.Version;
        var now = clock.GetUtcNow();
        var removed = booking.RemoveService(command.ServiceId, await actors.CurrentAsync(ActorType.ShopUser, cancellationToken), now);
        if (removed.IsFailure)
        {
            return removed.Error;
        }

        BookingEvents.Add(db, BookingEvents.ServicesChanged, booking, now);
        await db.SaveChangesAsync(cancellationToken);
        return (await reader.MapAsync(shopId, [booking], cancellationToken))[0];
    }
}
