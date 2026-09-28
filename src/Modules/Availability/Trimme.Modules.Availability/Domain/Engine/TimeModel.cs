namespace Trimme.Modules.Availability.Domain.Engine;

/// <summary>A half-open span of time <c>[Start, End)</c> between two instants.</summary>
public readonly record struct InstantRange(DateTimeOffset Start, DateTimeOffset End)
{
    public bool IsEmpty => End <= Start;

    public bool Overlaps(InstantRange other) => Start < other.End && other.Start < End;
}

/// <summary>An inclusive range of calendar days in the shop's time zone.</summary>
public readonly record struct DateRange(DateOnly Start, DateOnly End)
{
    public bool Contains(DateOnly date) => date >= Start && date <= End;
}

/// <summary>
/// One opening or working interval of a weekday, in minutes from that day's local midnight. The end may pass midnight
/// (up to 24 hours after the start), for example 21:00–02:00 is <c>(1260, 1560)</c>: the window opens on this day and
/// closes on the next (D-082).
/// </summary>
public readonly record struct WeeklyInterval(DayOfWeek Day, int StartMinute, int EndMinute);

/// <summary>
/// A break: every week on the given weekdays, or once on <see cref="Date"/>. Minutes are from local midnight of the day
/// it applies to, within that day.
/// </summary>
public sealed record BreakRule(IReadOnlyCollection<DayOfWeek> Weekdays, DateOnly? Date, int StartMinute, int EndMinute)
{
    public bool AppliesOn(DateOnly date) => Date is { } once ? once == date : Weekdays.Contains(date.DayOfWeek);
}

/// <summary>
/// Local wall-clock times of one IANA time zone as instants. Never assumes a day is 24 hours: a local time skipped by
/// a daylight-saving jump resolves to the first instant after the gap, and a repeated local time to its first
/// (earlier) occurrence.
/// </summary>
public sealed class ShopClock(TimeZoneInfo zone)
{
    public TimeZoneInfo Zone { get; } = zone;

    /// <summary>The instant of <paramref name="minute"/> minutes after local midnight of <paramref name="date"/> (may pass the next midnight).</summary>
    public DateTimeOffset At(DateOnly date, int minute)
    {
        var local = date.ToDateTime(TimeOnly.MinValue).AddMinutes(minute);

        // Skipped wall-clock times (spring forward): the first valid minute after them is the transition instant.
        for (var guard = 0; Zone.IsInvalidTime(local) && guard < 24 * 60; guard++)
        {
            local = local.AddMinutes(1);
        }

        var offset = Zone.IsAmbiguousTime(local) ? Zone.GetAmbiguousTimeOffsets(local).Max() : Zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    public DateTime Local(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Zone).DateTime;

    public DateOnly Date(DateTimeOffset instant) => DateOnly.FromDateTime(Local(instant));

    public DateTimeOffset StartOfDay(DateOnly date) => At(date, 0);
}

/// <summary>A sorted set of disjoint, non-adjacent instant ranges (adjacent ranges are merged).</summary>
public sealed class InstantSet
{
    public static readonly InstantSet Empty = new([]);

    private readonly List<InstantRange> _ranges;

    private InstantSet(List<InstantRange> ranges) => _ranges = ranges;

    public IReadOnlyList<InstantRange> Ranges => _ranges;

    public static InstantSet From(IEnumerable<InstantRange> ranges)
    {
        var merged = new List<InstantRange>();
        foreach (var range in ranges.Where(r => !r.IsEmpty).OrderBy(r => r.Start))
        {
            if (merged.Count > 0 && range.Start <= merged[^1].End)
            {
                var last = merged[^1];
                merged[^1] = last with { End = range.End > last.End ? range.End : last.End };
            }
            else
            {
                merged.Add(range);
            }
        }

        return new InstantSet(merged);
    }

    public InstantSet Intersect(InstantSet other)
    {
        var result = new List<InstantRange>();
        int i = 0, j = 0;
        while (i < _ranges.Count && j < other._ranges.Count)
        {
            var a = _ranges[i];
            var b = other._ranges[j];
            var start = a.Start > b.Start ? a.Start : b.Start;
            var end = a.End < b.End ? a.End : b.End;
            if (start < end)
            {
                result.Add(new InstantRange(start, end));
            }

            if (a.End < b.End)
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return new InstantSet(result);
    }

    public InstantSet Subtract(InstantSet other)
    {
        var result = new List<InstantRange>();
        var j = 0;
        foreach (var range in _ranges)
        {
            var start = range.Start;
            while (j < other._ranges.Count && other._ranges[j].End <= start)
            {
                j++;
            }

            var k = j;
            while (k < other._ranges.Count && other._ranges[k].Start < range.End)
            {
                var cut = other._ranges[k];
                if (cut.Start > start)
                {
                    result.Add(new InstantRange(start, cut.Start));
                }

                if (cut.End > start)
                {
                    start = cut.End;
                }

                k++;
            }

            if (start < range.End)
            {
                result.Add(new InstantRange(start, range.End));
            }
        }

        return new InstantSet(result);
    }

    /// <summary>Whether <paramref name="range"/> lies entirely inside one range of the set.</summary>
    public bool Contains(InstantRange range)
    {
        int low = 0, high = _ranges.Count - 1;
        while (low <= high)
        {
            var mid = (low + high) / 2;
            if (_ranges[mid].Start <= range.Start)
            {
                if (range.End <= _ranges[mid].End)
                {
                    return true;
                }

                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return false;
    }
}
