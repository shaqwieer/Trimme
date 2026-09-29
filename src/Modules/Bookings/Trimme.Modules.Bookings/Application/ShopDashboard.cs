using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Scheduling;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Bookings.Domain;

namespace Trimme.Modules.Bookings.Application;

// The shop's operations board (spec §13, s-overview and s-calendar, D-100). Everything is counted per business day: a
// booking belongs to the day whose opening window contains its start, so 00:30 in a window that opened on Thursday is
// Thursday's. Outside every window, its local date decides. The shop comes from the session.

/// <param name="Total">The day's bookings, cancellations left out.</param>
/// <param name="PreviousDayTotal">The same for the day before (the design's "vs yesterday").</param>
/// <param name="PendingUpcoming">Bookings still waiting for the shop's confirmation, from now on (any day).</param>
/// <param name="Completed">The day's completed visits.</param>
/// <param name="NoShow">The day's no-shows.</param>
/// <param name="Cancelled">The day's cancellations (by the customer or the shop).</param>
/// <param name="FreeMinutes">Bookable minutes left that day: each professional's available time minus their booked time.</param>
public sealed record ShopOverviewKpis(int Total, int PreviousDayTotal, int PendingUpcoming, int Completed, int NoShow, int Cancelled, int FreeMinutes);

/// <param name="Id">The professional.</param>
/// <param name="NameAr">Arabic name.</param>
/// <param name="NameEn">English name.</param>
/// <param name="Bookings">Their bookings that day (cancellations left out).</param>
/// <param name="BookedMinutes">The minutes those bookings take.</param>
/// <param name="AvailableMinutes">Their working time that day outside breaks and time off.</param>
/// <param name="Working">They have working time that day.</param>
/// <param name="OnLeave">Time off takes all of their working time that day.</param>
public sealed record ProfessionalLoadResponse(
    Guid Id, string NameAr, string NameEn, int Bookings, int BookedMinutes, int AvailableMinutes, bool Working, bool OnLeave);

public sealed record HourCountResponse(int Hour, int Count);

/// <summary>
/// The operational overview of one business day (s-overview): KPIs, what comes next, each professional's load and the
/// last seven days by starting hour (local time; cancellations left out).
/// </summary>
public sealed record ShopOverviewResponse(
    DateOnly Date,
    string TimeZone,
    DateTimeOffset Now,
    ShopOverviewKpis Kpis,
    IReadOnlyList<ShopBookingResponse> Upcoming,
    IReadOnlyList<ProfessionalLoadResponse> Professionals,
    IReadOnlyList<HourCountResponse> Hourly,
    bool OnlineBookingPaused);

public sealed record TimeWindowResponse(DateTimeOffset Start, DateTimeOffset End);

public sealed record CalendarProfessionalResponse(Guid Id, string NameAr, string NameEn, bool IsActive);

/// <summary>A professional's lane on a business day: working time, breaks and time off (the calendar shades the rest).</summary>
public sealed record CalendarLaneResponse(
    Guid ProfessionalId, IReadOnlyList<TimeWindowResponse> Working, IReadOnlyList<TimeWindowResponse> Breaks, IReadOnlyList<TimeWindowResponse> TimeOff);

/// <summary>A booking block on the calendar: the customer's name and the booked item, never a phone number.</summary>
public sealed record CalendarBookingResponse(
    Guid Id,
    string Reference,
    Guid ProfessionalId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string CustomerName,
    string ItemNameAr,
    string? ItemNameEn,
    BookingStatus Status,
    BookingChannel Channel);

public sealed record CalendarDayResponse(
    DateOnly Date, bool Closed, IReadOnlyList<TimeWindowResponse> Open, IReadOnlyList<CalendarLaneResponse> Lanes, IReadOnlyList<CalendarBookingResponse> Bookings);

/// <summary>Business days <c>From</c>…<c>To</c> (at most seven) with lanes per professional and their bookings (s-calendar).</summary>
public sealed record ShopCalendarResponse(
    DateOnly From, DateOnly To, string TimeZone, IReadOnlyList<CalendarProfessionalResponse> Professionals, IReadOnlyList<CalendarDayResponse> Days);

internal sealed record GetShopOverviewQuery(DateOnly? Date) : IQuery<Result<ShopOverviewResponse>>;

internal sealed record GetShopCalendarQuery(DateOnly From, DateOnly? To, Guid? ProfessionalId) : IQuery<Result<ShopCalendarResponse>>;

/// <summary>Assigns bookings to business days from the shop's day plans.</summary>
internal sealed class BusinessDays(IReadOnlyList<ShopDayPlan> plans, TimeZoneInfo zone)
{
    public DateOnly Of(DateTimeOffset start) =>
        plans.FirstOrDefault(p => p.Contains(start))?.Date ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, zone).DateTime);

    public ShopDayPlan? Plan(DateOnly date) => plans.FirstOrDefault(p => p.Date == date);

    public static DateTimeOffset StartOf(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }
}

/// <summary>Loads what the overview and the calendar share: the shop, its professionals, day plans and bookings.</summary>
internal sealed class ShopBoardLoader(
    TrimmeDbContext db, ICurrentTenant tenant, IShopDirectory shops, IProfessionalDirectory professionals, IShopDayPlanReader plans)
{
    /// <summary>The signed-in user's shop and its time zone, or <see langword="null"/> without an operable shop.</summary>
    public async Task<(ShopSummary Shop, TimeZoneInfo Zone)?> ShopAsync(CancellationToken cancellationToken) =>
        tenant.ShopId is { } shopId && await shops.FindAsync(shopId, cancellationToken) is { } shop
            ? (shop, TimeZoneInfo.FindSystemTimeZoneById(shop.TimeZone))
            : null;

    /// <summary>The business days <paramref name="from"/>…<paramref name="to"/>, optionally for one professional.</summary>
    public async Task<ShopBoard> LoadAsync(
        ShopSummary shop, TimeZoneInfo zone, DateOnly from, DateOnly to, Guid? professionalId, CancellationToken cancellationToken)
    {
        // From the first day's midnight to the day after the last: the last day's windows may pass midnight.
        var bookings = await db.Set<Booking>().AsNoTracking()
            .Where(b => b.StartsAt >= BusinessDays.StartOf(from, zone) && b.StartsAt < BusinessDays.StartOf(to.AddDays(2), zone))
            .OrderBy(b => b.StartsAt).ThenBy(b => b.Id)
            .ToListAsync(cancellationToken);
        var team = (await professionals.ListByShopAsync(shop.Id, cancellationToken))
            .Where(p => p.IsActive || bookings.Any(b => b.ProfessionalId == p.Id)).ToList();
        if (professionalId is { } only)
        {
            team = [.. team.Where(p => p.Id.Value == only)];
            bookings = [.. bookings.Where(b => b.ProfessionalId.Value == only)];
        }

        // The day before the first too: its windows past midnight claim the first day's early bookings.
        var dayPlans = await plans.GetAsync(shop.Id, shop.TimeZone, [.. team.Select(p => p.Id)], from.AddDays(-1), to, cancellationToken);
        var days = new BusinessDays(dayPlans, zone);
        return new ShopBoard(shop, zone, team, days, [.. bookings.Where(b => days.Of(b.StartsAt) >= from && days.Of(b.StartsAt) <= to)]);
    }
}

internal sealed record ShopBoard(ShopSummary Shop, TimeZoneInfo Zone, IReadOnlyList<ProfessionalSummary> Team, BusinessDays Days, IReadOnlyList<Booking> Bookings);

/// <summary>
/// The overview (s-overview, D-100). Load is minutes-based: booked minutes against the professional's available minutes
/// (working time outside breaks and time off); free capacity is what remains, never below zero per professional.
/// </summary>
internal sealed class GetShopOverviewHandler(TrimmeDbContext db, ShopBoardLoader loader, ShopBookingReader reader, TimeProvider clock)
    : IQueryHandler<GetShopOverviewQuery, Result<ShopOverviewResponse>>
{
    public const int UpcomingLimit = 8;
    public const int HourlyDays = 7;

    public async Task<Result<ShopOverviewResponse>> Handle(GetShopOverviewQuery query, CancellationToken cancellationToken)
    {
        if (await loader.ShopAsync(cancellationToken) is not var (shop, zone))
        {
            return BookingErrors.NotFound();
        }

        var now = clock.GetUtcNow();
        var date = query.Date ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var board = await loader.LoadAsync(shop, zone, date.AddDays(-(HourlyDays - 1)), date, null, cancellationToken);
        var kept = board.Bookings.Where(b => !BookingRules.IsCancelled(b.Status)).ToList();
        var today = kept.Where(b => board.Days.Of(b.StartsAt) == date).ToList();
        var todayAll = board.Bookings.Where(b => board.Days.Of(b.StartsAt) == date).ToList();

        var plan = board.Days.Plan(date);
        var load = board.Team.Select(p =>
        {
            var lane = plan?.Professionals.FirstOrDefault(l => l.ProfessionalId == p.Id);
            var theirs = today.Where(b => b.ProfessionalId == p.Id).ToList();
            var working = lane is { Working.Count: > 0 };
            return new ProfessionalLoadResponse(
                p.Id.Value, p.NameAr, p.NameEn, theirs.Count, theirs.Sum(b => b.DurationMinutes), Math.Max(0, lane?.AvailableMinutes ?? 0),
                working, working && lane!.TimeOff.Count > 0 && lane.AvailableMinutes <= 0);
        }).ToList();

        var pending = await db.Set<Booking>().AsNoTracking().CountAsync(b => b.Status == BookingStatus.Pending && b.StartsAt >= now, cancellationToken);
        var kpis = new ShopOverviewKpis(
            today.Count,
            kept.Count(b => board.Days.Of(b.StartsAt) == date.AddDays(-1)),
            pending,
            today.Count(b => b.Status == BookingStatus.Completed),
            today.Count(b => b.Status == BookingStatus.NoShow),
            todayAll.Count(b => BookingRules.IsCancelled(b.Status)),
            load.Sum(l => Math.Max(0, l.AvailableMinutes - l.BookedMinutes)));

        var upcoming = today.Where(b => b.IsActive && b.EndsAt > now).OrderBy(b => b.StartsAt).Take(UpcomingLimit).ToList();
        var hourly = Enumerable.Range(0, 24)
            .Select(hour => new HourCountResponse(hour, kept.Count(b => TimeZoneInfo.ConvertTime(b.StartsAt, board.Zone).Hour == hour)))
            .ToList();

        return new ShopOverviewResponse(
            date, board.Shop.TimeZone, now, kpis, await reader.MapAsync(board.Shop.Id, upcoming, cancellationToken), load, hourly,
            board.Shop.OnlineBookingPaused);
    }
}

/// <summary>
/// The calendar (s-calendar, D-034, D-100): one to seven business days with each professional's lane (working time,
/// breaks, time off) and every booking except cancellations, minute-accurate. Optionally one professional only.
/// </summary>
internal sealed class GetShopCalendarHandler(ShopBoardLoader loader) : IQueryHandler<GetShopCalendarQuery, Result<ShopCalendarResponse>>
{
    public const int MaxDays = 7;

    public async Task<Result<ShopCalendarResponse>> Handle(GetShopCalendarQuery query, CancellationToken cancellationToken)
    {
        var to = query.To ?? query.From;
        if (to < query.From || to.DayNumber - query.From.DayNumber >= MaxDays)
        {
            return Error.Validation("validation.failed", "The date range is invalid.",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["to"] = ["validation.out_of_range"] });
        }

        if (await loader.ShopAsync(cancellationToken) is not var (shop, zone))
        {
            return BookingErrors.NotFound();
        }

        var board = await loader.LoadAsync(shop, zone, query.From, to, query.ProfessionalId, cancellationToken);

        static IReadOnlyList<TimeWindowResponse> Windows(IEnumerable<TimeWindow> windows) => [.. windows.Select(w => new TimeWindowResponse(w.Start, w.End))];
        var days = new List<CalendarDayResponse>();
        for (var date = query.From; date <= to; date = date.AddDays(1))
        {
            var plan = board.Days.Plan(date);
            var bookings = board.Bookings
                .Where(b => !BookingRules.IsCancelled(b.Status) && board.Days.Of(b.StartsAt) == date)
                .Select(b => new CalendarBookingResponse(
                    b.Id.Value, b.Reference, b.ProfessionalId.Value, b.StartsAt, b.EndsAt, b.CustomerName, b.ItemNameAr, b.ItemNameEn, b.Status, b.Channel))
                .ToList();
            days.Add(new CalendarDayResponse(
                date,
                plan?.Closed ?? false,
                Windows(plan?.Open ?? []),
                [.. (plan?.Professionals ?? []).Select(l => new CalendarLaneResponse(l.ProfessionalId.Value, Windows(l.Working), Windows(l.Breaks), Windows(l.TimeOff)))],
                bookings));
        }

        return new ShopCalendarResponse(
            query.From, to, board.Shop.TimeZone,
            [.. board.Team.Select(p => new CalendarProfessionalResponse(p.Id.Value, p.NameAr, p.NameEn, p.IsActive))],
            days);
    }
}
