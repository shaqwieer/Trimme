using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Scheduling;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Bookings.Application.Customer;
using Trimme.Modules.Bookings.Domain;

namespace Trimme.Modules.Bookings.Application;

// Emergency postponement (D-147, the client's second step: «يؤجّل بموافقة الزبون، والعرض يظل محجوز لين قبل الموعد بساعة»).
// The barber offers a customer's online booking a new time with them; it is held for the customer until an hour before the
// original start; the customer accepts (the booking moves) or declines (it stays). A walk-in or phone booking is changed
// directly (the shop reschedule), once the customer agreed in person or by phone.

internal sealed record PostponeBookingCommand(Guid BookingId, DateTimeOffset StartsAt, uint Version) : ICommand<Result<ShopBookingResponse>>;

internal sealed record AnswerPostponementCommand(Guid BookingId, bool Accept, uint Version) : ICommand<Result<CustomerBookingResponse>>;

/// <summary>
/// The barber's offer: their own Pending or Confirmed online booking, more than an hour before it starts, to a time
/// they are free (hours, breaks, bookings and other offers). A newer offer replaces the open one. The time is held by the
/// database from the moment the offer is saved; a race with a booking of the same time answers 409
/// <c>booking.slot_unavailable</c>.
/// </summary>
internal sealed class PostponeBookingHandler(
    TrimmeDbContext db,
    ICurrentTenant tenant,
    ICurrentUser user,
    IAvailabilityChecker availability,
    ShopBookingReader reader,
    BookingActors actors,
    TimeProvider clock) : ICommandHandler<PostponeBookingCommand, Result<ShopBookingResponse>>
{
    public async Task<Result<ShopBookingResponse>> Handle(PostponeBookingCommand command, CancellationToken cancellationToken)
    {
        var id = new BookingId(command.BookingId);
        if (tenant.ShopId is not { } shopId || await db.Set<Booking>().OwnOnly(user).SingleOrDefaultAsync(b => b.Id == id, cancellationToken) is not { } booking)
        {
            return BookingErrors.NotFound();
        }

        var now = clock.GetUtcNow();
        if (command.StartsAt <= now)
        {
            return BookingErrors.StartInPast();
        }

        db.Entry(booking).Property(b => b.Version).OriginalValue = command.Version;
        var actor = await actors.CurrentAsync(ActorType.ShopUser, cancellationToken);
        var offered = booking.OfferPostponement(command.StartsAt, actor, now);
        if (offered.IsFailure)
        {
            return offered.Error;
        }

        // The booking's own time and its own earlier offer do not count against the new one.
        var free = await availability.FreeProfessionalsAsync(
            shopId, [booking.ProfessionalId], command.StartsAt, booking.DurationMinutes, AvailabilityCheckMode.WalkIn, booking.Id.Value, cancellationToken);
        if (free.Count == 0)
        {
            return BookingErrors.SlotUnavailable();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var earlier in await db.Set<PostponementOffer>().Where(o => o.BookingId == booking.Id && o.Status == PostponementStatus.Open).ToListAsync(cancellationToken))
        {
            earlier.Withdraw(now);
        }

        try
        {
            // The withdrawal is saved first, so the new offer's hold check no longer sees the old one.
            await db.SaveChangesAsync(cancellationToken);
            db.Add(new PostponementOffer(Guid.CreateVersion7(), booking, command.StartsAt, actor, now));
            BookingEvents.Add(db, BookingEvents.PostponementOffered, booking, now, command.StartsAt.ToUniversalTime());
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
}

/// <summary>
/// The customer's answer on their booking page (D-147). Accepting moves the booking to the held time, whatever their own
/// change window (the shop asked); the barber hears about it like any change of time. Declining keeps the booking and
/// frees the held time; the barber and the owner get an in-app notice, and the barber then moves or cancels it.
/// </summary>
internal sealed class AnswerPostponementHandler(TrimmeDbContext db, CustomerBookingSupport support)
    : ICommandHandler<AnswerPostponementCommand, Result<CustomerBookingResponse>>
{
    public async Task<Result<CustomerBookingResponse>> Handle(AnswerPostponementCommand command, CancellationToken cancellationToken)
    {
        if (support.CustomerId is not { } customerId || await support.MineAsync(command.BookingId, track: true, cancellationToken) is not { } booking)
        {
            return BookingErrors.NotFound();
        }

        var now = support.Now;
        var offer = await db.Set<PostponementOffer>()
            .Where(o => o.BookingId == booking.Id && o.Status == PostponementStatus.Open)
            .OrderByDescending(o => o.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (offer is null)
        {
            return PostponementErrors.Gone();
        }

        db.Entry(booking).Property(b => b.Version).OriginalValue = command.Version;
        var answered = command.Accept ? booking.AcceptPostponement(offer, customerId, now) : booking.DeclinePostponement(offer, customerId, now);
        if (answered.IsFailure)
        {
            return answered.Error;
        }

        BookingEvents.Add(db, command.Accept ? BookingEvents.Rescheduled : BookingEvents.PostponementDeclined, booking, now);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (DatabaseErrors.IsLostRace(exception))
        {
            return BookingErrors.SlotUnavailable();
        }

        return await support.ResponseAsync(booking, cancellationToken);
    }
}
