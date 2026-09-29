using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.Modules.Availability.Domain.Engine;

/// <summary>Platform booking rules (spec §11, D-076): minimum lead time, horizon and slot step.</summary>
public sealed record BookingPolicy(int MinLeadMinutes, int HorizonDays, int SlotStepMinutes);

/// <summary>The shop's side of availability: its time zone, weekly opening hours and closed days.</summary>
public sealed record ShopCalendar(TimeZoneInfo TimeZone, IReadOnlyList<WeeklyInterval> OpeningHours, IReadOnlyList<DateRange> Closures);

/// <summary>
/// One professional's side of availability.
/// </summary>
/// <param name="Id">The professional.</param>
/// <param name="WorkingHours">Their weekly hours, or <see langword="null"/> to follow the shop's opening hours.</param>
/// <param name="Breaks">Breaks that apply to them (their own and the shop-wide ones).</param>
/// <param name="Blocked">Time off and existing non-cancelled bookings.</param>
public sealed record ProfessionalCalendar(
    ProfessionalId Id,
    IReadOnlyList<WeeklyInterval>? WorkingHours,
    IReadOnlyList<BreakRule> Breaks,
    IReadOnlyList<InstantRange> Blocked);

/// <summary>Slots wanted for the local dates <see cref="From"/>…<see cref="To"/> (inclusive), for an item of <see cref="DurationMinutes"/>.</summary>
public sealed record AvailabilityQuery(DateOnly From, DateOnly To, int DurationMinutes, DateTimeOffset Now, BookingPolicy Policy);

/// <summary>Where a slot sits in the day (D-009 groups the slot grid by period).</summary>
public enum DayPeriod
{
    /// <summary>05:00–11:59.</summary>
    Morning,

    /// <summary>12:00–16:59.</summary>
    Afternoon,

    /// <summary>17:00–04:59, including the hours after midnight.</summary>
    Evening,
}

/// <summary>
/// A bookable start. <see cref="Date"/> and <see cref="LocalTime"/> are the shop-local calendar date and time of
/// <see cref="StartsAt"/>; <see cref="Professionals"/> are every professional free for the whole item (the candidate set
/// of "any professional", D-012).
/// </summary>
public sealed record AvailableSlot(
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    DateOnly Date,
    TimeOnly LocalTime,
    DayPeriod Period,
    IReadOnlyList<ProfessionalId> Professionals);

/// <summary>
/// The availability engine (spec §11, R-AVL-01/02, D-082). Pure: every input is passed in, including the clock, so the
/// same code answers the public slot list and, in Phase 10, the booking transaction's recheck.
/// <list type="bullet">
/// <item>A professional is free where the shop is open (opening hours of a day that is not closed) and they work
/// (their own hours, or the shop's), outside breaks, time off and existing bookings.</item>
/// <item>A slot needs the whole item <c>[start, start + duration)</c> free; slots start on the slot-step grid counted
/// from local midnight (8:05, 8:10… with a 5-minute step).</item>
/// <item>A closure closes the business day: every window that opens on a closed date, including its part after
/// midnight. The previous day's window running past midnight into a closed date is not affected.</item>
/// <item>Slots are dated by their local start (the date strip). Bookable dates are today…today + horizon − 1, and a
/// slot must start at least the lead time after now.</item>
/// </list>
/// </summary>
public static class AvailabilityEngine
{
    public static readonly IReadOnlySet<int> SlotSteps = new HashSet<int> { 5, 10, 15, 20, 30, 60 };

    /// <summary>The first and last bookable local dates at <paramref name="now"/>.</summary>
    public static (DateOnly First, DateOnly Last) BookableDates(TimeZoneInfo zone, DateTimeOffset now, BookingPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var today = new ShopClock(zone).Date(now);
        return (today, today.AddDays(Math.Max(policy.HorizonDays, 1) - 1));
    }

    public static IReadOnlyList<AvailableSlot> FindSlots(ShopCalendar shop, IReadOnlyList<ProfessionalCalendar> professionals, AvailabilityQuery query)
    {
        ArgumentNullException.ThrowIfNull(shop);
        ArgumentNullException.ThrowIfNull(professionals);
        ArgumentNullException.ThrowIfNull(query);
        if (query.DurationMinutes <= 0 || professionals.Count == 0 || !SlotSteps.Contains(query.Policy.SlotStepMinutes))
        {
            return [];
        }

        var clock = new ShopClock(shop.TimeZone);
        var (firstBookable, lastBookable) = BookableDates(shop.TimeZone, query.Now, query.Policy);
        var first = query.From > firstBookable ? query.From : firstBookable;
        var last = query.To < lastBookable ? query.To : lastBookable;
        if (first > last)
        {
            return [];
        }

        // The window of the day before `first` can run past midnight into it.
        var businessDays = Days(first.AddDays(-1), last);
        var open = Windows(clock, shop.OpeningHours, businessDays.Where(d => !shop.Closures.Any(c => c.Contains(d))));
        var free = professionals.Select(p => (p.Id, Free: FreeTime(clock, shop, p, businessDays, open))).ToList();

        var duration = TimeSpan.FromMinutes(query.DurationMinutes);
        var step = query.Policy.SlotStepMinutes;
        var earliest = query.Now.AddMinutes(query.Policy.MinLeadMinutes);
        var slots = new List<AvailableSlot>();
        foreach (var window in open.Ranges)
        {
            for (var start = FirstOnGrid(clock, window.Start, step); start + duration <= window.End; start = start.AddMinutes(step))
            {
                var local = clock.Local(start);
                var date = DateOnly.FromDateTime(local);
                if (start < earliest || date < first || date > last)
                {
                    continue;
                }

                var item = new InstantRange(start, start + duration);
                var candidates = free.Where(p => p.Free.Contains(item)).Select(p => p.Id).ToList();
                if (candidates.Count > 0)
                {
                    slots.Add(new AvailableSlot(start, item.End, date, TimeOnly.FromDateTime(local), PeriodOf(local.Hour), candidates));
                }
            }
        }

        return slots;
    }

    /// <summary>
    /// Whether <paramref name="professional"/> can take an item of <paramref name="durationMinutes"/> starting exactly
    /// at <paramref name="start"/>: the same rules as <see cref="FindSlots"/>, for one start (the booking recheck).
    /// </summary>
    public static bool IsBookable(ShopCalendar shop, ProfessionalCalendar professional, DateTimeOffset start, int durationMinutes, DateTimeOffset now, BookingPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(shop);
        var date = new ShopClock(shop.TimeZone).Date(start);
        return FindSlots(shop, [professional], new AvailabilityQuery(date, date, durationMinutes, now, policy))
            .Any(s => s.StartsAt == start);
    }

    /// <summary>
    /// The professional's free time over the business days <paramref name="from"/> − 1 … <paramref name="to"/>: open ∩
    /// working hours, minus breaks and everything in <see cref="ProfessionalCalendar.Blocked"/>.
    /// </summary>
    public static InstantSet FreeTime(ShopCalendar shop, ProfessionalCalendar professional, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(shop);
        var clock = new ShopClock(shop.TimeZone);
        var businessDays = Days(from.AddDays(-1), to);
        var open = Windows(clock, shop.OpeningHours, businessDays.Where(d => !shop.Closures.Any(c => c.Contains(d))));
        return FreeTime(clock, shop, professional, businessDays, open);
    }

    /// <summary>
    /// The walk-in rule (D-088): the professional is free for the whole item. Any start minute is allowed, including
    /// "now"; there is no lead time, horizon or grid.
    /// </summary>
    public static bool IsFree(ShopCalendar shop, ProfessionalCalendar professional, DateTimeOffset start, int durationMinutes)
    {
        ArgumentNullException.ThrowIfNull(shop);
        if (durationMinutes <= 0)
        {
            return false;
        }

        var clock = new ShopClock(shop.TimeZone);
        var item = new InstantRange(start, start.AddMinutes(durationMinutes));
        return FreeTime(shop, professional, clock.Date(item.Start), clock.Date(item.End)).Contains(item);
    }

    /// <summary>How far ahead <see cref="OpenStatus"/> looks for the next opening.</summary>
    public const int OpenStatusLookaheadDays = 7;

    /// <summary>
    /// Whether the shop is open at <paramref name="now"/> (a window of a business day that is not closed contains it),
    /// when the current window ends (touching windows are joined, so 21:00–24:00 followed by 00:00–02:00 closes at 02:00),
    /// or when it opens next within <see cref="OpenStatusLookaheadDays"/> days. Same closure rule as the slots: a closure
    /// closes the business day, including its hours after midnight.
    /// </summary>
    public static (bool IsOpen, DateTimeOffset? ClosesAt, DateTimeOffset? NextOpensAt) OpenStatus(ShopCalendar shop, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(shop);
        var clock = new ShopClock(shop.TimeZone);
        var today = clock.Date(now);
        var businessDays = Days(today.AddDays(-1), today.AddDays(OpenStatusLookaheadDays));
        var open = Windows(clock, shop.OpeningHours, businessDays.Where(d => !shop.Closures.Any(c => c.Contains(d))));
        foreach (var window in open.Ranges)
        {
            if (window.Start <= now && now < window.End)
            {
                return (true, window.End, null);
            }

            if (window.Start > now)
            {
                return (false, null, window.Start);
            }
        }

        return (false, null, null);
    }

    public static DayPeriod PeriodOf(int localHour) => localHour switch
    {
        >= 5 and < 12 => DayPeriod.Morning,
        >= 12 and < 17 => DayPeriod.Afternoon,
        _ => DayPeriod.Evening,
    };

    private static InstantSet FreeTime(ShopClock clock, ShopCalendar shop, ProfessionalCalendar professional, List<DateOnly> businessDays, InstantSet open)
    {
        var works = Windows(clock, professional.WorkingHours ?? shop.OpeningHours, businessDays);

        // Breaks are dated by the calendar day they fall on; one day either side covers windows that pass midnight.
        var breakDays = Days(businessDays[0], businessDays[^1].AddDays(1));
        var busy = professional.Breaks
            .SelectMany(b => breakDays.Where(b.AppliesOn).Select(d => new InstantRange(clock.At(d, b.StartMinute), clock.At(d, b.EndMinute))))
            .Concat(professional.Blocked);
        return open.Intersect(works).Subtract(InstantSet.From(busy));
    }

    /// <summary>The windows the weekly <paramref name="hours"/> open on each of these business days, as instants.</summary>
    public static InstantSet Windows(ShopClock clock, IReadOnlyList<WeeklyInterval> hours, IEnumerable<DateOnly> days) =>
        InstantSet.From(days.SelectMany(d => hours.Where(h => h.Day == d.DayOfWeek)
            .Select(h => new InstantRange(clock.At(d, h.StartMinute), clock.At(d, h.EndMinute)))));

    /// <summary>The first instant at or after <paramref name="instant"/> whose local time is a multiple of the step.</summary>
    private static DateTimeOffset FirstOnGrid(ShopClock clock, DateTimeOffset instant, int step)
    {
        var local = clock.Local(instant);
        var minute = (local.Hour * 60) + local.Minute;
        var remainder = minute % step;
        var aligned = instant.AddSeconds(-local.Second).AddTicks(-(local.Ticks % TimeSpan.TicksPerSecond));
        if (aligned < instant)
        {
            aligned = aligned.AddMinutes(1);
            remainder = (remainder + 1) % step;
        }

        return remainder == 0 ? aligned : aligned.AddMinutes(step - remainder);
    }

    private static List<DateOnly> Days(DateOnly from, DateOnly to)
    {
        var days = new List<DateOnly>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            days.Add(day);
        }

        return days;
    }
}
