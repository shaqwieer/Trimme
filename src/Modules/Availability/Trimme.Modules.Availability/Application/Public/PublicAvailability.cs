using System.Globalization;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Platform;
using Trimme.BuildingBlocks.Application.Scheduling;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.Modules.Availability.Domain;
using Trimme.Modules.Availability.Domain.Engine;

namespace Trimme.Modules.Availability.Application.Public;

/// <summary>
/// A bookable start (D-009: only genuinely bookable slots are returned). <c>LocalTime</c> is <c>HH:mm</c> in the
/// shop's time zone; <c>ProfessionalIds</c> are every professional free for the whole item (D-012 candidates).
/// </summary>
public sealed record AvailabilitySlotResponse(DateTimeOffset StartsAt, DateTimeOffset EndsAt, string LocalTime, DayPeriod Period, IReadOnlyList<Guid> ProfessionalIds);

/// <summary>
/// Slots of one local date. When the shop takes no online bookings right now (paused, subscription not in force…),
/// <c>Bookable</c> is false, <c>BlockedReason</c> says why and there are no slots (D-013, D-078).
/// </summary>
public sealed record AvailableSlotsResponse(DateOnly Date, string TimeZone, bool Bookable, string? BlockedReason, IReadOnlyList<AvailabilitySlotResponse> Slots);

public sealed record AvailableDateResponse(DateOnly Date, int SlotCount);

/// <summary>Every date from <c>From</c> to <c>To</c> with its number of bookable slots (0 = none), for the date strip.</summary>
public sealed record AvailableDatesResponse(DateOnly From, DateOnly To, string TimeZone, bool Bookable, string? BlockedReason, IReadOnlyList<AvailableDateResponse> Dates);

/// <summary><c>ServiceIds</c> adds services booked together with <c>ServiceId</c> (or on their own); never with a package.</summary>
internal sealed record GetAvailableSlotsQuery(string ShopSlug, Guid? ServiceId, Guid? PackageId, Guid? ProfessionalId, DateOnly Date, IReadOnlyList<Guid>? ServiceIds = null)
    : IQuery<Result<AvailableSlotsResponse>>;

internal sealed record GetAvailableDatesQuery(string ShopSlug, Guid? ServiceId, Guid? PackageId, Guid? ProfessionalId, DateOnly? From, DateOnly? To, IReadOnlyList<Guid>? ServiceIds = null)
    : IQuery<Result<AvailableDatesResponse>>;

/// <summary>What one availability request resolved to: the shop and either a block reason or the computed slots.</summary>
internal sealed record AvailabilityRun(ShopSummary Shop, TimeZoneInfo Zone, DateOnly From, DateOnly To, string? BlockedReason, IReadOnlyList<AvailableSlot> Slots);

/// <summary>
/// Runs the engine for a public caller (anonymous or customer). Order of the gates: the shop is active; it takes
/// online bookings (subscription and pause, <see cref="IShopBookability"/>); the service or package is published and
/// online-bookable; the chosen professional is active and assigned to it. Reads open the public scope for this one shop
/// only (D-066).
/// </summary>
internal sealed class PublicAvailabilityService(
    IPublicDataScope scope,
    IShopDirectory shops,
    IShopBookability bookability,
    IBookableOfferCatalog catalog,
    IProfessionalDirectory professionals,
    IPlatformSettings settings,
    ScheduleLoader loader,
    TimeProvider clock)
{
    /// <summary>At most this many days per request (a month of the date strip).</summary>
    public const int MaxRangeDays = 31;

    public const int DefaultRangeDays = 14;

    public async Task<Result<AvailabilityRun>> RunAsync(
        string slug, Guid? serviceId, IReadOnlyList<Guid>? serviceIds, Guid? packageId, Guid? professionalId, DateOnly? from, DateOnly? to,
        CancellationToken cancellationToken)
    {
        if (BookableOfferRequest.Services(serviceId, serviceIds, packageId) is not { } services)
        {
            return ScheduleErrors.InvalidQuery("serviceId");
        }

        ShopSummary? shop;
        using (scope.Begin(shopId: null))
        {
            shop = await shops.FindBySlugAsync(slug, cancellationToken);
        }

        if (shop is not { Status: ShopStatus.Active })
        {
            return ScheduleErrors.ShopNotFound();
        }

        var zone = ScheduleLoader.Zone(shop.TimeZone);
        var now = clock.GetUtcNow();
        var start = from ?? new ShopClock(zone).Date(now);
        var end = to ?? start.AddDays(DefaultRangeDays - 1);
        if (end < start || end.DayNumber - start.DayNumber >= MaxRangeDays)
        {
            return ScheduleErrors.InvalidQuery("to");
        }

        // Inside the public scope: a signed-in shop user asking about another shop must see that shop like anyone else.
        using var _ = scope.Begin(shop.Id);
        var gate = await bookability.GetAsync(shop.Id, cancellationToken);
        if (!gate.AcceptsOnlineBookings)
        {
            return new AvailabilityRun(shop, zone, start, end, gate.BlockedReason, []);
        }

        var offer = await catalog.FindAsync(shop.Id, services, packageId, cancellationToken);
        if (offer is null)
        {
            return ScheduleErrors.OfferNotFound();
        }

        if (!offer.OnlineBookable)
        {
            return ScheduleErrors.OfferNotOnlineBookable();
        }

        var active = (await professionals.ListByShopAsync(shop.Id, cancellationToken)).Where(p => p.IsActive).Select(p => p.Id).ToHashSet();
        var candidates = offer.EligibleProfessionalIds.Where(active.Contains).Distinct().ToList();
        if (professionalId is { } requested)
        {
            var chosen = new ProfessionalId(requested);
            if (!active.Contains(chosen))
            {
                return ScheduleErrors.ProfessionalNotFound();
            }

            if (!candidates.Contains(chosen))
            {
                return ScheduleErrors.ProfessionalNotEligible();
            }

            candidates = [chosen];
        }

        var policy = ScheduleLoader.Policy(await settings.GetAsync(cancellationToken));
        var (shopCalendar, calendars) = await loader.CalendarsAsync(shop.Id, zone, candidates, start, end, cancellationToken);
        var slots = AvailabilityEngine.FindSlots(shopCalendar, calendars, new AvailabilityQuery(start, end, offer.DurationMinutes, now, policy));
        return new AvailabilityRun(shop, zone, start, end, null, slots);
    }

    public static AvailabilitySlotResponse ToResponse(AvailableSlot slot) =>
        new(slot.StartsAt, slot.EndsAt, slot.LocalTime.ToString("HH:mm", CultureInfo.InvariantCulture), slot.Period, [.. slot.Professionals.Select(p => p.Value)]);
}

internal sealed class GetAvailableSlotsHandler(PublicAvailabilityService availability) : IQueryHandler<GetAvailableSlotsQuery, Result<AvailableSlotsResponse>>
{
    public async Task<Result<AvailableSlotsResponse>> Handle(GetAvailableSlotsQuery query, CancellationToken cancellationToken)
    {
        var run = await availability.RunAsync(query.ShopSlug, query.ServiceId, query.ServiceIds, query.PackageId, query.ProfessionalId, query.Date, query.Date, cancellationToken);
        if (run.IsFailure)
        {
            return run.Error;
        }

        var value = run.Value;
        return new AvailableSlotsResponse(
            query.Date, value.Shop.TimeZone, value.BlockedReason is null, value.BlockedReason,
            [.. value.Slots.Select(PublicAvailabilityService.ToResponse)]);
    }
}

internal sealed class GetAvailableDatesHandler(PublicAvailabilityService availability) : IQueryHandler<GetAvailableDatesQuery, Result<AvailableDatesResponse>>
{
    public async Task<Result<AvailableDatesResponse>> Handle(GetAvailableDatesQuery query, CancellationToken cancellationToken)
    {
        var run = await availability.RunAsync(query.ShopSlug, query.ServiceId, query.ServiceIds, query.PackageId, query.ProfessionalId, query.From, query.To, cancellationToken);
        if (run.IsFailure)
        {
            return run.Error;
        }

        var value = run.Value;
        var counts = value.Slots.GroupBy(s => s.Date).ToDictionary(g => g.Key, g => g.Count());
        var dates = Enumerable.Range(0, value.To.DayNumber - value.From.DayNumber + 1)
            .Select(value.From.AddDays)
            .Select(d => new AvailableDateResponse(d, counts.GetValueOrDefault(d)))
            .ToList();
        return new AvailableDatesResponse(value.From, value.To, value.Shop.TimeZone, value.BlockedReason is null, value.BlockedReason, dates);
    }
}
