using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Discovery;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Scheduling;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Bookings.Application.Customer;
using Trimme.Modules.Bookings.Domain;

namespace Trimme.Modules.Bookings.Application;

// The shop changes a booking for its customer (D-145): a new time with the same barber, or the same time with another
// barber of the shop. The owner may change any booking; a barber only their own (BarberScope). The desk's collision rules
// apply (any minute, every booking, break and time off), the start may not be in the past, and the customer's
// cancellation window does not bind the shop. Same transaction, idempotency key, version check and exclusion constraint
// as the admin's reschedule (D-103). The customer is told through the outbox (rescheduled / professional changed).

/// <summary>A barber who may take the booking at its time: they do the booked item; <c>Free</c> when nothing else holds them then.</summary>
public sealed record BookingProfessionalOption(Guid Id, string NameAr, string NameEn, bool Free);

public sealed record BookingProfessionalOptionsResponse(DateTimeOffset StartsAt, IReadOnlyList<BookingProfessionalOption> Professionals);

/// <summary>A changed booking; <c>Replayed</c> when the same idempotency key answered from the first request.</summary>
internal sealed record ShopBookingChangeResult(ShopBookingResponse Booking, bool Replayed);

internal sealed record GetShopRescheduleDatesQuery(Guid BookingId, DateOnly? From, DateOnly? To) : IQuery<Result<RescheduleDatesResponse>>;

internal sealed record GetShopRescheduleSlotsQuery(Guid BookingId, DateOnly Date) : IQuery<Result<RescheduleSlotsResponse>>;

internal sealed record ShopRescheduleBookingCommand(Guid BookingId, DateTimeOffset StartsAt, uint Version, string IdempotencyKey)
    : ICommand<Result<ShopBookingChangeResult>>;

internal sealed record GetBookingProfessionalOptionsQuery(Guid BookingId) : IQuery<Result<BookingProfessionalOptionsResponse>>;

internal sealed record ChangeBookingProfessionalCommand(Guid BookingId, Guid ProfessionalId, uint Version, string IdempotencyKey)
    : ICommand<Result<ShopBookingChangeResult>>;

/// <summary>The free starts for the booking's barber and duration (its own time does not block it), on the desk's rules.</summary>
internal sealed class ShopRescheduleAvailability(TrimmeDbContext db, ICurrentUser user, ICurrentTenant tenant, IShopDirectory shops, ISlotProbe probe, TimeProvider clock)
{
    private const int MaxSlots = 10_000;

    public async Task<Result<RescheduleRun>> RunAsync(Guid bookingId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var id = new BookingId(bookingId);
        if (tenant.ShopId is not { } shopId
            || await db.Set<Booking>().AsNoTracking().OwnOnly(user).SingleOrDefaultAsync(b => b.Id == id, cancellationToken) is not { } booking
            || await shops.FindAsync(shopId, cancellationToken) is not { } shop)
        {
            return BookingErrors.NotFound();
        }

        if (booking.Status is not (BookingStatus.Pending or BookingStatus.Confirmed))
        {
            return BookingErrors.NotReschedulable(booking.Status);
        }

        var start = from ?? probe.Today(shop.TimeZone, clock.GetUtcNow());
        var end = to ?? start.AddDays(RescheduleAvailability.DefaultDays - 1);
        if (end < start || end.DayNumber - start.DayNumber >= RescheduleAvailability.MaxDays)
        {
            return Error.Validation("validation.failed", "The date range is invalid.", new Dictionary<string, string[]>(StringComparer.Ordinal) { ["to"] = ["validation.out_of_range"] });
        }

        var slots = await probe.ProbeAsync(
            shop.Id, shop.TimeZone, booking.DurationMinutes, [booking.ProfessionalId], start, end, MaxSlots, cancellationToken, ignoreBookingId: booking.Id.Value);
        return new RescheduleRun(true, null, shop.TimeZone, start, end, slots);
    }
}

internal sealed class GetShopRescheduleDatesHandler(ShopRescheduleAvailability availability) : IQueryHandler<GetShopRescheduleDatesQuery, Result<RescheduleDatesResponse>>
{
    public async Task<Result<RescheduleDatesResponse>> Handle(GetShopRescheduleDatesQuery query, CancellationToken cancellationToken)
    {
        var run = await availability.RunAsync(query.BookingId, query.From, query.To, cancellationToken);
        if (run.IsFailure)
        {
            return run.Error;
        }

        var result = run.Value;
        var counts = result.Slots.GroupBy(s => s.Date).ToDictionary(g => g.Key, g => g.Count());
        return new RescheduleDatesResponse(
            true, null, result.TimeZone,
            [.. Enumerable.Range(0, result.To.DayNumber - result.From.DayNumber + 1)
                .Select(result.From.AddDays)
                .Select(d => new RescheduleDateResponse(d, counts.GetValueOrDefault(d)))]);
    }
}

internal sealed class GetShopRescheduleSlotsHandler(ShopRescheduleAvailability availability) : IQueryHandler<GetShopRescheduleSlotsQuery, Result<RescheduleSlotsResponse>>
{
    public async Task<Result<RescheduleSlotsResponse>> Handle(GetShopRescheduleSlotsQuery query, CancellationToken cancellationToken)
    {
        var run = await availability.RunAsync(query.BookingId, query.Date, query.Date, cancellationToken);
        if (run.IsFailure)
        {
            return run.Error;
        }

        var result = run.Value;
        return new RescheduleSlotsResponse(
            true, null, result.TimeZone, query.Date,
            [.. result.Slots.Select(s => new RescheduleSlotResponse(s.StartsAt, s.LocalTime, s.Period))]);
    }
}

/// <summary>The barbers who may take the booking at its time (D-145), whether each is free then, for the «نقل إلى حلاق آخر» list.</summary>
internal sealed class GetBookingProfessionalOptionsHandler(
    TrimmeDbContext db,
    ICurrentUser user,
    ICurrentTenant tenant,
    IBookableOfferCatalog catalog,
    IProfessionalDirectory professionals,
    IAvailabilityChecker availability) : IQueryHandler<GetBookingProfessionalOptionsQuery, Result<BookingProfessionalOptionsResponse>>
{
    public async Task<Result<BookingProfessionalOptionsResponse>> Handle(GetBookingProfessionalOptionsQuery query, CancellationToken cancellationToken)
    {
        var id = new BookingId(query.BookingId);
        if (tenant.ShopId is null
            || await db.Set<Booking>().AsNoTracking().OwnOnly(user).SingleOrDefaultAsync(b => b.Id == id, cancellationToken) is not { } booking)
        {
            return BookingErrors.NotFound();
        }

        if (booking.Status is not (BookingStatus.Pending or BookingStatus.Confirmed))
        {
            return BookingErrors.NotReschedulable(booking.Status);
        }

        var others = (await RescheduleTargets.CandidatesAsync(booking, catalog, professionals, cancellationToken))
            .Where(p => p.IsActive && p.Id != booking.ProfessionalId).ToList();
        var free = others.Count == 0
            ? []
            : (await availability.FreeProfessionalsAsync(
                booking.ShopId, [.. others.Select(p => p.Id)], booking.StartsAt, booking.DurationMinutes, AvailabilityCheckMode.WalkIn, booking.Id.Value, cancellationToken))
                .ToHashSet();
        return new BookingProfessionalOptionsResponse(
            booking.StartsAt, [.. others.Select(p => new BookingProfessionalOption(p.Id.Value, p.NameAr, p.NameEn, free.Contains(p.Id)))]);
    }
}

/// <summary>The shared steps of a shop change: idempotency, the booking (the barber's own), the version, the save.</summary>
internal sealed class ShopBookingChanger(
    TrimmeDbContext db, ICurrentUser user, ICurrentTenant tenant, IdempotencyGate idempotency, ShopBookingReader reader, TimeProvider clock)
{
    public async Task<Result<ShopBookingChangeResult>> ChangeAsync(
        string scope,
        string idempotencyKey,
        string hash,
        Guid bookingId,
        uint version,
        Func<Booking, Task<Result<string>>> change,
        CancellationToken cancellationToken)
    {
        if (tenant.ShopId is not { } shopId || user.UserId is not { } userId)
        {
            return BookingErrors.NotFound();
        }

        if (await ReplayAsync(userId, scope, idempotencyKey, hash, shopId, cancellationToken) is { } replayed)
        {
            return replayed;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var (claimed, record) = await idempotency.ClaimAsync(userId, scope, idempotencyKey, hash, cancellationToken);
        if (!claimed)
        {
            await transaction.RollbackAsync(cancellationToken);
            return await ReplayAsync(userId, scope, idempotencyKey, hash, shopId, cancellationToken) ?? BookingErrors.SlotUnavailable();
        }

        var id = new BookingId(bookingId);
        if (await db.Set<Booking>().OwnOnly(user).SingleOrDefaultAsync(b => b.Id == id, cancellationToken) is not { } booking)
        {
            return BookingErrors.NotFound();
        }

        db.Entry(booking).Property(b => b.Version).OriginalValue = version;
        var changed = await change(booking);
        if (changed.IsFailure)
        {
            return changed.Error;
        }

        record!.Complete(booking.Id.Value);
        BookingEvents.Add(db, changed.Value, booking, clock.GetUtcNow());
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
        return new ShopBookingChangeResult((await reader.MapAsync(shopId, [booking], cancellationToken))[0], Replayed: false);
    }

    private async Task<Result<ShopBookingChangeResult>?> ReplayAsync(
        Guid userId, string scope, string key, string hash, ShopId shopId, CancellationToken cancellationToken)
    {
        var replay = await idempotency.ReplayAsync(userId, scope, key, hash, cancellationToken);
        if (replay is null)
        {
            return null;
        }

        if (replay.IsFailure)
        {
            return replay.Error;
        }

        var id = new BookingId(replay.Value);
        return await db.Set<Booking>().AsNoTracking().OwnOnly(user).SingleOrDefaultAsync(b => b.Id == id, cancellationToken) is { } booking
            ? new ShopBookingChangeResult((await reader.MapAsync(shopId, [booking], cancellationToken))[0], Replayed: true)
            : BookingErrors.NotFound();
    }
}

/// <summary>A new time with the same barber (D-145): «تعديل الموعد». The customer gets «تم تغيير موعد حجزك».</summary>
internal sealed class ShopRescheduleBookingHandler(ShopBookingChanger changer, ICurrentUser user, IAvailabilityChecker availability, TimeProvider clock)
    : ICommandHandler<ShopRescheduleBookingCommand, Result<ShopBookingChangeResult>>
{
    private const string Scope = "bookings.shop_reschedule";

    public Task<Result<ShopBookingChangeResult>> Handle(ShopRescheduleBookingCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        if (command.StartsAt <= now)
        {
            return Task.FromResult<Result<ShopBookingChangeResult>>(BookingErrors.StartInPast());
        }

        var hash = IdempotencyRecord.Hash(command with { IdempotencyKey = string.Empty, Version = 0 });
        return changer.ChangeAsync(Scope, command.IdempotencyKey, hash, command.BookingId, command.Version, async booking =>
        {
            var free = await availability.FreeProfessionalsAsync(
                booking.ShopId, [booking.ProfessionalId], command.StartsAt, booking.DurationMinutes, AvailabilityCheckMode.WalkIn, booking.Id.Value, cancellationToken);
            if (free.Count == 0)
            {
                return BookingErrors.SlotUnavailable();
            }

            var moved = booking.Reschedule(command.StartsAt, booking.CurrentProfessional, new BookingActor(user.UserId, ActorType.ShopUser), now, cutoffMinutes: null);
            if (moved.IsFailure)
            {
                return moved.Error;
            }

            return BookingEvents.Rescheduled;
        }, cancellationToken);
    }
}

/// <summary>
/// The same time with another barber of the shop (D-145): «نقل إلى حلاق آخر». The barber must do the booked item and be
/// free then. The customer is told and may cancel or change it until it starts; the old barber is told it is off their
/// day, the new one that it is on theirs.
/// </summary>
internal sealed class ChangeBookingProfessionalHandler(
    ShopBookingChanger changer,
    ICurrentUser user,
    IBookableOfferCatalog catalog,
    IProfessionalDirectory professionals,
    IAvailabilityChecker availability,
    TimeProvider clock) : ICommandHandler<ChangeBookingProfessionalCommand, Result<ShopBookingChangeResult>>
{
    private const string Scope = "bookings.change_professional";

    public Task<Result<ShopBookingChangeResult>> Handle(ChangeBookingProfessionalCommand command, CancellationToken cancellationToken)
    {
        var hash = IdempotencyRecord.Hash(command with { IdempotencyKey = string.Empty, Version = 0 });
        return changer.ChangeAsync(Scope, command.IdempotencyKey, hash, command.BookingId, command.Version, async booking =>
        {
            if (command.ProfessionalId == booking.ProfessionalId.Value)
            {
                return BookingErrors.SameProfessional();
            }

            var target = await RescheduleTargets.ResolveAsync(booking, command.ProfessionalId, catalog, professionals, cancellationToken);
            if (target.IsFailure)
            {
                return target.Error;
            }

            var free = await availability.FreeProfessionalsAsync(
                booking.ShopId, [target.Value.Id], booking.StartsAt, booking.DurationMinutes, AvailabilityCheckMode.WalkIn, booking.Id.Value, cancellationToken);
            if (free.Count == 0)
            {
                return BookingErrors.ProfessionalBusy();
            }

            var changed = booking.ChangeProfessional(target.Value, new BookingActor(user.UserId, ActorType.ShopUser), clock.GetUtcNow());
            if (changed.IsFailure)
            {
                return changed.Error;
            }

            return BookingEvents.ProfessionalChanged;
        }, cancellationToken);
    }
}
