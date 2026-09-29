using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Platform;
using Trimme.BuildingBlocks.Application.Scheduling;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.Modules.Availability.Domain;
using Trimme.Modules.Availability.Domain.Engine;

namespace Trimme.Modules.Availability.Application;

/// <summary>
/// One professional who can do the walk-in: free right now (whole duration, D-035), the next free start on the day's
/// grid, and every free start that day.
/// </summary>
public sealed record WalkInProfessionalResponse(Guid Id, string NameAr, string NameEn, bool FreeNow, DateTimeOffset? NextFreeAt, IReadOnlyList<DateTimeOffset> Starts);

/// <summary>Walk-in choices for a service or package on one shop-local date (s-walkin).</summary>
public sealed record WalkInOptionsResponse(
    DateOnly Date, string TimeZone, DateTimeOffset Now, int DurationMinutes, int SlotStepMinutes, IReadOnlyList<WalkInProfessionalResponse> Professionals);

internal sealed record GetWalkInOptionsQuery(Guid? ServiceId, Guid? PackageId, DateOnly? Date) : IQuery<Result<WalkInOptionsResponse>>;

/// <summary>
/// The walk-in step's professionals and times (spec §13, D-088): the active professionals assigned to the item (for a
/// package, to every item), with the same collision rules as the walk-in command — hours, breaks, time off, closures and
/// bookings — at any minute for "now" and on the slot grid for later times. Online-only rules (lead time, horizon, the
/// online-bookable flag, the pause) do not apply to the shop's own desk. The shop comes from the session.
/// </summary>
internal sealed class GetWalkInOptionsHandler(
    ICurrentTenant tenant,
    IShopDirectory shops,
    IBookableOfferCatalog catalog,
    IProfessionalDirectory professionals,
    IPlatformSettings settings,
    ScheduleLoader loader,
    TimeProvider clock) : IQueryHandler<GetWalkInOptionsQuery, Result<WalkInOptionsResponse>>
{
    /// <summary>How far ahead the desk may look (a walk-in is for today; later dates serve phone-in bookings at the desk).</summary>
    public const int MaxDaysAhead = 60;

    public async Task<Result<WalkInOptionsResponse>> Handle(GetWalkInOptionsQuery query, CancellationToken cancellationToken)
    {
        if (tenant.ShopId is not { } shopId || await shops.FindAsync(shopId, cancellationToken) is not { } shop)
        {
            return ScheduleErrors.ShopNotFound();
        }

        if ((query.ServiceId is null) == (query.PackageId is null)
            || await catalog.FindAsync(shopId, query.ServiceId, query.PackageId, cancellationToken) is not { } offer)
        {
            return Error.NotFound("availability.offer_not_found", "The service or package was not found.");
        }

        var zone = ScheduleLoader.Zone(shop.TimeZone);
        var now = clock.GetUtcNow();
        var today = new ShopClock(zone).Date(now);
        var date = query.Date ?? today;
        if (date < today || date > today.AddDays(MaxDaysAhead))
        {
            return Error.Validation("validation.failed", "The date is out of range.",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["date"] = ["validation.out_of_range"] });
        }

        var eligible = offer.EligibleProfessionalIds.ToHashSet();
        var candidates = (await professionals.ListByShopAsync(shopId, cancellationToken))
            .Where(p => p.IsActive && eligible.Contains(p.Id)).ToList();
        var step = (await settings.GetAsync(cancellationToken)).SlotStepMinutes;
        var (calendar, calendars) = await loader.CalendarsAsync(shopId, zone, [.. candidates.Select(p => p.Id)], date, date, cancellationToken);

        // The desk policy: no lead time, and the date asked for is always inside the horizon.
        var policy = new BookingPolicy(0, MaxDaysAhead + 1, step);
        var result = candidates.Select(p =>
        {
            var own = calendars.Single(c => c.Id == p.Id);
            var starts = AvailabilityEngine.FindSlots(calendar, [own], new AvailabilityQuery(date, date, offer.DurationMinutes, now, policy))
                .Select(s => s.StartsAt).ToList();
            var freeNow = date == today && AvailabilityEngine.IsFree(calendar, own, now, offer.DurationMinutes);
            return new WalkInProfessionalResponse(p.Id.Value, p.NameAr, p.NameEn, freeNow, starts.Count > 0 ? starts[0] : null, starts);
        }).ToList();

        return new WalkInOptionsResponse(date, shop.TimeZone, now, offer.DurationMinutes, step, result);
    }
}
