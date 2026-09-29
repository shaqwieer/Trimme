using Trimme.BuildingBlocks.Application.Discovery;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Platform;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.Modules.Bookings.Domain;

namespace Trimme.Modules.Bookings.Application.Customer;

public sealed record RescheduleDateResponse(DateOnly Date, int SlotCount);

/// <summary>
/// Dates the customer can move their booking to (same professional and duration), with the number of free starts on each.
/// <c>Bookable</c> is false with the reason while the shop takes no online bookings (reschedule is then refused, D-087).
/// </summary>
public sealed record RescheduleDatesResponse(bool Bookable, string? BlockedReason, string TimeZone, IReadOnlyList<RescheduleDateResponse> Dates);

public sealed record RescheduleSlotResponse(DateTimeOffset StartsAt, string LocalTime, string Period);

public sealed record RescheduleSlotsResponse(bool Bookable, string? BlockedReason, string TimeZone, DateOnly Date, IReadOnlyList<RescheduleSlotResponse> Slots);

/// <summary>What one reschedule availability request resolved to.</summary>
internal sealed record RescheduleRun(bool Bookable, string? BlockedReason, string TimeZone, DateOnly From, DateOnly To, IReadOnlyList<ProbedSlot> Slots);

internal sealed record GetRescheduleDatesQuery(Guid BookingId, DateOnly? From, DateOnly? To) : IQuery<Result<RescheduleDatesResponse>>;

internal sealed record GetRescheduleSlotsQuery(Guid BookingId, DateOnly Date) : IQuery<Result<RescheduleSlotsResponse>>;

/// <summary>
/// Availability for rescheduling one of the customer's own bookings (D-097). The same online rules as the public slots
/// (lead time, horizon, grid, every collision) for the booking's professional and duration; the booking's own time does
/// not block it, so it can move a few minutes. The move itself stays the existing command, which keeps the old time until
/// the new one is saved (D-089).
/// </summary>
internal sealed class RescheduleAvailability(
    CustomerBookingSupport support,
    IPublicDataScope scope,
    IShopBookability bookability,
    ISlotProbe probe)
{
    public const int DefaultDays = 14;
    public const int MaxDays = 31;

    /// <summary>At most this many starts are read for one request (a month of 5-minute starts is well below it).</summary>
    private const int MaxSlots = 10_000;

    public async Task<Result<RescheduleRun>> RunAsync(
        Guid bookingId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        if (support.CustomerId is null || await support.MineAsync(bookingId, track: false, cancellationToken) is not { } booking)
        {
            return BookingErrors.NotFound();
        }

        var cutoff = await support.CutoffAsync(cancellationToken);
        if (!booking.CustomerCanChange(support.Now, cutoff))
        {
            return booking.IsActive ? BookingErrors.CutoffPassed(cutoff) : BookingErrors.NotReschedulable(booking.Status);
        }

        var shop = await support.ShopAsync(booking.ShopId, cancellationToken);
        if (shop is null)
        {
            return BookingErrors.NotFound();
        }

        var start = from ?? probe.Today(shop.TimeZone, support.Now);
        var end = to ?? start.AddDays(DefaultDays - 1);
        if (end < start || end.DayNumber - start.DayNumber >= MaxDays)
        {
            return Error.Validation("validation.failed", "The date range is invalid.", new Dictionary<string, string[]>(StringComparer.Ordinal) { ["to"] = ["validation.out_of_range"] });
        }

        using var _ = scope.Begin(shop.Id);
        var gate = await bookability.GetAsync(shop.Id, cancellationToken);
        if (!gate.AcceptsOnlineBookings)
        {
            return new RescheduleRun(false, gate.BlockedReason, shop.TimeZone, start, end, []);
        }

        var slots = await probe.ProbeAsync(
            shop.Id, shop.TimeZone, booking.DurationMinutes, [booking.ProfessionalId], start, end, MaxSlots, cancellationToken, ignoreBookingId: booking.Id.Value);
        return new RescheduleRun(true, null, shop.TimeZone, start, end, slots);
    }
}

internal sealed class GetRescheduleDatesHandler(RescheduleAvailability availability) : IQueryHandler<GetRescheduleDatesQuery, Result<RescheduleDatesResponse>>
{
    public async Task<Result<RescheduleDatesResponse>> Handle(GetRescheduleDatesQuery query, CancellationToken cancellationToken)
    {
        var run = await availability.RunAsync(query.BookingId, query.From, query.To, cancellationToken);
        if (run.IsFailure)
        {
            return run.Error;
        }

        var result = run.Value;
        var counts = result.Slots.GroupBy(s => s.Date).ToDictionary(g => g.Key, g => g.Count());
        return new RescheduleDatesResponse(
            result.Bookable,
            result.BlockedReason,
            result.TimeZone,
            [.. Enumerable.Range(0, result.To.DayNumber - result.From.DayNumber + 1)
                .Select(result.From.AddDays)
                .Select(d => new RescheduleDateResponse(d, counts.GetValueOrDefault(d)))]);
    }
}

internal sealed class GetRescheduleSlotsHandler(RescheduleAvailability availability) : IQueryHandler<GetRescheduleSlotsQuery, Result<RescheduleSlotsResponse>>
{
    public async Task<Result<RescheduleSlotsResponse>> Handle(GetRescheduleSlotsQuery query, CancellationToken cancellationToken)
    {
        var run = await availability.RunAsync(query.BookingId, query.Date, query.Date, cancellationToken);
        if (run.IsFailure)
        {
            return run.Error;
        }

        var result = run.Value;
        return new RescheduleSlotsResponse(
            result.Bookable, result.BlockedReason, result.TimeZone, query.Date,
            [.. result.Slots.Select(s => new RescheduleSlotResponse(s.StartsAt, s.LocalTime, s.Period))]);
    }
}
