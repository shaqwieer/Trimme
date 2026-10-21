using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Scheduling;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Availability.Domain;
using Trimme.Modules.Availability.Domain.Engine;

namespace Trimme.Modules.Availability.Application;

// The desk's «البريكات» page (D-134): one barber's calendar date, 00:00 to 24:00, where the barber's own one-off breaks
// are set by the hour with no reason. The shop comes from ICurrentTenant; another shop's barber is 404.

/// <summary>Minutes from the local midnight of one calendar date, end exclusive (0–1440).</summary>
public sealed record MinuteWindowDto(int StartMinute, int EndMinute);

/// <summary>
/// One barber's calendar date in minutes (D-134). <c>Working</c> includes the part of the previous business day that
/// runs past midnight. <c>Breaks</c> are the barber's own one-off breaks on the date, the only ones the page edits;
/// <c>ShopBreaks</c> are the owner's (weekly, or for everyone). <c>PassedMinutes</c> is how much of the date is
/// already over: 0 for a future date, 1440 for a past one.
/// </summary>
public sealed record BarberDayResponse(
    Guid ProfessionalId,
    DateOnly Date,
    DateOnly Today,
    int PassedMinutes,
    IReadOnlyList<MinuteWindowDto> Working,
    IReadOnlyList<MinuteWindowDto> Breaks,
    IReadOnlyList<MinuteWindowDto> ShopBreaks,
    IReadOnlyList<MinuteWindowDto> Booked,
    IReadOnlyList<MinuteWindowDto> TimeOff);

internal sealed record GetBarberDayQuery(Guid ProfessionalId, DateOnly Date) : IQuery<Result<BarberDayResponse>>;

internal sealed record SetBarberDayBreaksCommand(Guid ProfessionalId, DateOnly Date, IReadOnlyList<MinuteWindowDto> Breaks)
    : ICommand<Result<BarberDayResponse>>;

/// <summary>Reads one barber's date from the same schedule and engine rules as availability.</summary>
internal sealed class BarberDayReader(TrimmeDbContext db, ScheduleLoader loader, IBookedTimeReader bookings)
{
    public async Task<BarberDayResponse> ReadAsync(ShopScope scope, ProfessionalId professionalId, DateOnly date, CancellationToken cancellationToken)
    {
        var dayStart = scope.Clock.StartOfDay(date);
        var dayEnd = scope.Clock.StartOfDay(date.AddDays(1));
        var day = InstantSet.From([new InstantRange(dayStart, dayEnd)]);

        // Without bookings, the calendar's blocked time is the time off.
        var (shop, calendars) = await loader.CalendarsAsync(
            scope.Id, scope.Clock.Zone, [professionalId], date.AddDays(-1), date, cancellationToken, includeBookings: false);
        var calendar = calendars[0];
        var working = InstantSet.From(
        [
            .. AvailabilityEngine.DayOf(shop, calendar, date.AddDays(-1)).Working.Ranges,
            .. AvailabilityEngine.DayOf(shop, calendar, date).Working.Ranges,
        ]).Intersect(day);
        var booked = (await bookings.GetBusyAsync(scope.Id, [professionalId], dayStart, dayEnd, cancellationToken))
            .Select(b => new InstantRange(b.StartsAt, b.EndsAt));

        var breaks = await db.Set<ScheduleBreak>().AsNoTracking()
            .Where(b => b.ProfessionalId == null || b.ProfessionalId == professionalId)
            .ToListAsync(cancellationToken);
        var own = breaks.Where(b => IsOwn(b, professionalId, date)).ToList();

        return new BarberDayResponse(
            professionalId.Value,
            date,
            scope.Today,
            (int)Math.Clamp((scope.Now - dayStart).TotalMinutes, 0, ScheduleRules.MinutesPerDay),
            Minutes(working, dayStart),
            Merge(own.Select(b => new MinuteWindowDto(b.StartMinute, b.EndMinute))),
            Merge(breaks.Except(own).Where(b => b.ToRule().AppliesOn(date)).Select(b => new MinuteWindowDto(b.StartMinute, b.EndMinute))),
            Minutes(InstantSet.From(booked).Intersect(day), dayStart),
            Minutes(InstantSet.From(calendar.Blocked).Intersect(day), dayStart));
    }

    /// <summary>The barber's own one-off break on the date: the only kind the page replaces.</summary>
    public static bool IsOwn(ScheduleBreak entry, ProfessionalId professionalId, DateOnly date) =>
        entry.ProfessionalId == professionalId && entry.Date == date;

    /// <summary>Sorted, with overlapping and touching windows joined.</summary>
    public static IReadOnlyList<MinuteWindowDto> Merge(IEnumerable<MinuteWindowDto> windows)
    {
        var merged = new List<MinuteWindowDto>();
        foreach (var window in windows.OrderBy(w => w.StartMinute))
        {
            if (merged.Count > 0 && window.StartMinute <= merged[^1].EndMinute)
            {
                merged[^1] = merged[^1] with { EndMinute = Math.Max(merged[^1].EndMinute, window.EndMinute) };
            }
            else
            {
                merged.Add(window);
            }
        }

        return merged;
    }

    private static List<MinuteWindowDto> Minutes(InstantSet set, DateTimeOffset dayStart) =>
    [
        .. set.Ranges.Select(r => new MinuteWindowDto(
            (int)Math.Clamp((r.Start - dayStart).TotalMinutes, 0, ScheduleRules.MinutesPerDay),
            (int)Math.Clamp((r.End - dayStart).TotalMinutes, 0, ScheduleRules.MinutesPerDay))),
    ];
}

internal sealed class GetBarberDayHandler(ShopScheduleContext context, BarberDayReader reader)
    : IQueryHandler<GetBarberDayQuery, Result<BarberDayResponse>>
{
    public async Task<Result<BarberDayResponse>> Handle(GetBarberDayQuery query, CancellationToken cancellationToken)
    {
        if (await context.ScopeAsync(cancellationToken) is not { } scope)
        {
            return ScheduleErrors.ShopNotFound();
        }

        if (await context.ProfessionalAsync(scope, query.ProfessionalId, cancellationToken) is not { } professional)
        {
            return ScheduleErrors.ProfessionalNotFound();
        }

        return await reader.ReadAsync(scope, professional.Id, query.Date, cancellationToken);
    }
}

/// <summary>
/// Replaces the barber's own one-off breaks on the date with the given windows, joined where they touch (D-134). No
/// label is stored. Weekly breaks and breaks for everyone are the owner's and stay as they are. Bookings are never
/// cancelled; the page does not offer booked hours. Two people saving the same barber's date at once: the last save wins.
/// </summary>
internal sealed class SetBarberDayBreaksHandler(TrimmeDbContext db, ShopScheduleContext context, BarberDayReader reader)
    : ICommandHandler<SetBarberDayBreaksCommand, Result<BarberDayResponse>>
{
    /// <summary>Every 5-minute step of a day apart is far more than an hour grid needs.</summary>
    public const int MaxBreaksPerDay = 48;

    public async Task<Result<BarberDayResponse>> Handle(SetBarberDayBreaksCommand command, CancellationToken cancellationToken)
    {
        if (await context.ScopeAsync(cancellationToken) is not { } scope)
        {
            return ScheduleErrors.ShopNotFound();
        }

        if (await context.ProfessionalAsync(scope, command.ProfessionalId, cancellationToken) is not { } professional)
        {
            return ScheduleErrors.ProfessionalNotFound();
        }

        if (command.Date < scope.Today)
        {
            return ScheduleRules.Field("date", "validation.date_in_past");
        }

        if (command.Breaks.Count > MaxBreaksPerDay)
        {
            return ScheduleRules.Field("breaks", "validation.too_many");
        }

        if (command.Breaks.Any(b => !ScheduleRules.IsValidDayRange(b.StartMinute, b.EndMinute)))
        {
            return ScheduleRules.Field("breaks", "validation.hours_invalid");
        }

        var date = command.Date;
        var existing = await db.Set<ScheduleBreak>()
            .Where(b => b.ProfessionalId == professional.Id && b.Date == date)
            .ToListAsync(cancellationToken);
        db.RemoveRange(existing);
        foreach (var window in BarberDayReader.Merge(command.Breaks))
        {
            db.Add(ScheduleBreak.Create(
                EntityId.New<ScheduleBreakId>(), scope.Id, professional.Id, label: null, [], date, window.StartMinute, window.EndMinute, scope.Today, scope.Now).Value);
        }

        await db.SaveChangesAsync(cancellationToken);
        return await reader.ReadAsync(scope, professional.Id, date, cancellationToken);
    }
}
