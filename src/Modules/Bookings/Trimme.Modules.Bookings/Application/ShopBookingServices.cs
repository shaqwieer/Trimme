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

/// <summary>
/// Adds one of the shop's services that the booking's barber does, at its own price and length. The extra time must be
/// free of the barber's other open bookings: checked here, and by the exclusion constraint when two changes race (409
/// <c>booking.professional_busy</c>, so the barber moves the next booking first). The schedule (hours, breaks) is not
/// checked: the customer is in the chair, and the barber decides.
/// </summary>
internal sealed class AddBookingServiceHandler(
    TrimmeDbContext db, ICurrentTenant tenant, ICurrentUser user, IBookableOfferCatalog catalog, ShopBookingReader reader, BookingActors actors, TimeProvider clock)
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
        var added = booking.AddService(
            BookedPackageItem.Of(offer.Id, offer.NameAr, offer.NameEn, offer.Price, offer.DurationFor(booking.ProfessionalId)),
            await actors.CurrentAsync(ActorType.ShopUser, cancellationToken), now);
        if (added.IsFailure)
        {
            return added.Error;
        }

        var open = BookingRules.Active.ToArray();
        var newEnd = booking.BlockedUntil;
        if (await db.Set<Booking>().AsNoTracking().AnyAsync(
                b => b.Id != booking.Id && b.ProfessionalId == booking.ProfessionalId && open.Contains(b.Status) && b.StartsAt < newEnd && b.BlockedUntil > oldEnd,
                cancellationToken))
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
