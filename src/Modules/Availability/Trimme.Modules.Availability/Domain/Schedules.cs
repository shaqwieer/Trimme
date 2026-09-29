using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.Modules.Availability.Domain.Engine;

namespace Trimme.Modules.Availability.Domain;

public readonly record struct OpeningHoursId(Guid Value) : IEntityId<OpeningHoursId>
{
    public static OpeningHoursId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct WorkingHoursId(Guid Value) : IEntityId<WorkingHoursId>
{
    public static WorkingHoursId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct ShopClosureId(Guid Value) : IEntityId<ShopClosureId>
{
    public static ShopClosureId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct ScheduleBreakId(Guid Value) : IEntityId<ScheduleBreakId>
{
    public static ScheduleBreakId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct TimeOffId(Guid Value) : IEntityId<TimeOffId>
{
    public static TimeOffId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

/// <summary>One stored weekly interval (JSON element of the hours row). See <see cref="WeeklyInterval"/>.</summary>
public sealed class HoursInterval
{
    public DayOfWeek Day { get; set; }

    public int StartMinute { get; set; }

    public int EndMinute { get; set; }

    public static HoursInterval Of(WeeklyInterval interval) =>
        new() { Day = interval.Day, StartMinute = interval.StartMinute, EndMinute = interval.EndMinute };

    public WeeklyInterval ToWeekly() => new(Day, StartMinute, EndMinute);
}

public enum TimeOffKind
{
    Vacation,
    Sick,
    Other,
}

/// <summary>Limits shared by the schedule entities and validators (D-082).</summary>
public static class ScheduleRules
{
    public const int MinutesPerDay = 24 * 60;
    public const int MinuteStep = 5;
    public const int MaxIntervalsPerWeek = 28;
    public const int MaxLabelLength = 60;
    public const int MaxNoteLength = 200;
    public const int MaxRangeDays = 366;

    /// <summary>
    /// Each interval starts on a day between 00:00 and 23:55, ends after it and at most 24 hours later (so it may pass
    /// midnight), on 5-minute steps; no two intervals of the week overlap, counting the part after midnight.
    /// </summary>
    public static Error? ValidateWeek(IReadOnlyList<WeeklyInterval> intervals)
    {
        ArgumentNullException.ThrowIfNull(intervals);
        if (intervals.Count > MaxIntervalsPerWeek)
        {
            return Field("intervals", "validation.too_many");
        }

        foreach (var interval in intervals)
        {
            if (!Enum.IsDefined(interval.Day)
                || interval.StartMinute is < 0 or >= MinutesPerDay
                || interval.EndMinute <= interval.StartMinute
                || interval.EndMinute - interval.StartMinute > MinutesPerDay
                || interval.StartMinute % MinuteStep != 0
                || interval.EndMinute % MinuteStep != 0)
            {
                return Field("intervals", "validation.hours_invalid");
            }
        }

        // Minutes of the week from Sunday 00:00; the last interval may run into next Sunday.
        const int week = 7 * MinutesPerDay;
        var spans = intervals
            .Select(i => (Start: ((int)i.Day * MinutesPerDay) + i.StartMinute, End: ((int)i.Day * MinutesPerDay) + i.EndMinute))
            .OrderBy(s => s.Start).ToList();
        for (var k = 1; k < spans.Count; k++)
        {
            if (spans[k].Start < spans[k - 1].End)
            {
                return Field("intervals", "validation.hours_overlap");
            }
        }

        return spans.Count > 1 && spans[^1].End - week > spans[0].Start ? Field("intervals", "validation.hours_overlap") : null;
    }

    /// <summary>A time range within one day on 5-minute steps.</summary>
    public static bool IsValidDayRange(int startMinute, int endMinute) =>
        startMinute is >= 0 and < MinutesPerDay && endMinute > startMinute && endMinute <= MinutesPerDay
        && startMinute % MinuteStep == 0 && endMinute % MinuteStep == 0;

    public static Error Field(string field, string code) =>
        Error.Validation("validation.failed", "The request is invalid.",
            new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [code] });

    internal static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// The shop's weekly opening hours (s-hours "دوام المحل الأسبوعي"): one row per shop, edited as a whole week. They are
/// the outer limit of every professional's hours. No row means the shop has not set its hours and is closed.
/// </summary>
public sealed class ShopOpeningHours : AggregateRoot<OpeningHoursId>, IShopOwned, IConcurrencyVersioned, IPublicContent
{
    private ShopOpeningHours(OpeningHoursId id, ShopId shopId, DateTimeOffset now)
        : base(id)
    {
        ShopId = shopId;
        UpdatedAt = now;
    }

    private ShopOpeningHours()
    {
    }

    public ShopId ShopId { get; private set; }

    public List<HoursInterval> Intervals { get; private set; } = [];

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public static Result<ShopOpeningHours> Create(OpeningHoursId id, ShopId shopId, IReadOnlyList<WeeklyInterval> intervals, DateTimeOffset now)
    {
        var hours = new ShopOpeningHours(id, shopId, now);
        var set = hours.Replace(intervals, now);
        return set.IsFailure ? set.Error : hours;
    }

    public Result Replace(IReadOnlyList<WeeklyInterval> intervals, DateTimeOffset now)
    {
        if (ScheduleRules.ValidateWeek(intervals) is { } invalid)
        {
            return invalid;
        }

        Intervals = [.. intervals.OrderBy(i => i.Day).ThenBy(i => i.StartMinute).Select(HoursInterval.Of)];
        UpdatedAt = now;
        return Result.Success();
    }

    public IReadOnlyList<WeeklyInterval> Week() => [.. Intervals.Select(i => i.ToWeekly())];
}

/// <summary>
/// A professional's weekly working hours (DV-A03). While <see cref="FollowsShopHours"/> is set they work whenever the
/// shop is open; otherwise their own intervals apply, always within the shop's hours.
/// </summary>
public sealed class ProfessionalWorkingHours : AggregateRoot<WorkingHoursId>, IShopOwned, IConcurrencyVersioned
{
    private ProfessionalWorkingHours(WorkingHoursId id, ShopId shopId, ProfessionalId professionalId, DateTimeOffset now)
        : base(id)
    {
        ShopId = shopId;
        ProfessionalId = professionalId;
        FollowsShopHours = true;
        UpdatedAt = now;
    }

    private ProfessionalWorkingHours()
    {
    }

    public ShopId ShopId { get; private set; }

    public ProfessionalId ProfessionalId { get; private set; }

    public bool FollowsShopHours { get; private set; }

    public List<HoursInterval> Intervals { get; private set; } = [];

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public static Result<ProfessionalWorkingHours> Create(
        WorkingHoursId id, ShopId shopId, ProfessionalId professionalId, bool followsShopHours, IReadOnlyList<WeeklyInterval> intervals, DateTimeOffset now)
    {
        var hours = new ProfessionalWorkingHours(id, shopId, professionalId, now);
        var set = hours.Replace(followsShopHours, intervals, now);
        return set.IsFailure ? set.Error : hours;
    }

    /// <summary>Following the shop's hours clears the professional's own intervals.</summary>
    public Result Replace(bool followsShopHours, IReadOnlyList<WeeklyInterval> intervals, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(intervals);
        if (!followsShopHours && ScheduleRules.ValidateWeek(intervals) is { } invalid)
        {
            return invalid;
        }

        FollowsShopHours = followsShopHours;
        Intervals = followsShopHours ? [] : [.. intervals.OrderBy(i => i.Day).ThenBy(i => i.StartMinute).Select(HoursInterval.Of)];
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>The hours the engine uses: <see langword="null"/> means the shop's.</summary>
    public IReadOnlyList<WeeklyInterval>? Week() => FollowsShopHours ? null : [.. Intervals.Select(i => i.ToWeekly())];
}

/// <summary>Whole days the shop is closed (national day, renovation…): no slots on those business days.</summary>
public sealed class ShopClosure : AggregateRoot<ShopClosureId>, IShopOwned, IConcurrencyVersioned
{
    private ShopClosure(ShopClosureId id, ShopId shopId, DateTimeOffset now)
        : base(id)
    {
        ShopId = shopId;
        CreatedAt = now;
    }

    private ShopClosure()
    {
    }

    public ShopId ShopId { get; private set; }

    public DateOnly StartDate { get; private set; }

    /// <summary>Inclusive.</summary>
    public DateOnly EndDate { get; private set; }

    public string? Reason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public static Result<ShopClosure> Create(ShopClosureId id, ShopId shopId, DateOnly start, DateOnly end, string? reason, DateOnly today, DateTimeOffset now)
    {
        var closure = new ShopClosure(id, shopId, now);
        var set = closure.Apply(start, end, reason, today);
        return set.IsFailure ? set.Error : closure;
    }

    public Result Update(DateOnly start, DateOnly end, string? reason, DateOnly today, DateTimeOffset now)
    {
        var set = Apply(start, end, reason, today);
        if (set.IsSuccess)
        {
            UpdatedAt = now;
        }

        return set;
    }

    private Result Apply(DateOnly start, DateOnly end, string? reason, DateOnly today)
    {
        if (end < start || end.DayNumber - start.DayNumber >= ScheduleRules.MaxRangeDays)
        {
            return ScheduleRules.Field("endDate", "validation.range_invalid");
        }

        if (end < today)
        {
            return ScheduleRules.Field("endDate", "validation.date_in_past");
        }

        if (reason?.Trim().Length > ScheduleRules.MaxNoteLength)
        {
            return ScheduleRules.Field("reason", "validation.too_long");
        }

        StartDate = start;
        EndDate = end;
        Reason = ScheduleRules.Clean(reason);
        return Result.Success();
    }

    public DateRange Range() => new(StartDate, EndDate);
}

/// <summary>
/// A break (s-hours "الاستراحات المتكررة", DV-A04): weekly on some weekdays, or once on a date. It applies to one
/// professional or, when <see cref="ProfessionalId"/> is <see langword="null"/>, to everyone (prayer breaks).
/// </summary>
public sealed class ScheduleBreak : AggregateRoot<ScheduleBreakId>, IShopOwned, IConcurrencyVersioned
{
    private ScheduleBreak(ScheduleBreakId id, ShopId shopId, DateTimeOffset now)
        : base(id)
    {
        ShopId = shopId;
        Label = string.Empty;
        CreatedAt = now;
    }

    private ScheduleBreak()
    {
        Label = string.Empty;
    }

    public ShopId ShopId { get; private set; }

    public ProfessionalId? ProfessionalId { get; private set; }

    public string Label { get; private set; }

    /// <summary>Weekly break days; empty for a one-off break.</summary>
    public DayOfWeek[] Weekdays { get; private set; } = [];

    /// <summary>The day of a one-off break; <see langword="null"/> for a weekly one.</summary>
    public DateOnly? Date { get; private set; }

    public int StartMinute { get; private set; }

    public int EndMinute { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public static Result<ScheduleBreak> Create(
        ScheduleBreakId id, ShopId shopId, ProfessionalId? professionalId, string label, IReadOnlyCollection<DayOfWeek> weekdays, DateOnly? date,
        int startMinute, int endMinute, DateOnly today, DateTimeOffset now)
    {
        var entry = new ScheduleBreak(id, shopId, now);
        var set = entry.Apply(professionalId, label, weekdays, date, startMinute, endMinute, today);
        return set.IsFailure ? set.Error : entry;
    }

    public Result Update(
        ProfessionalId? professionalId, string label, IReadOnlyCollection<DayOfWeek> weekdays, DateOnly? date, int startMinute, int endMinute, DateOnly today, DateTimeOffset now)
    {
        var set = Apply(professionalId, label, weekdays, date, startMinute, endMinute, today);
        if (set.IsSuccess)
        {
            UpdatedAt = now;
        }

        return set;
    }

    private Result Apply(
        ProfessionalId? professionalId, string label, IReadOnlyCollection<DayOfWeek> weekdays, DateOnly? date, int startMinute, int endMinute, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(weekdays);
        if (string.IsNullOrWhiteSpace(label))
        {
            return ScheduleRules.Field("label", "validation.required");
        }

        if (label.Trim().Length > ScheduleRules.MaxLabelLength)
        {
            return ScheduleRules.Field("label", "validation.too_long");
        }

        if ((weekdays.Count == 0) == (date is null) || weekdays.Any(d => !Enum.IsDefined(d)))
        {
            return ScheduleRules.Field("weekdays", "validation.break_days");
        }

        if (date is { } once && once < today)
        {
            return ScheduleRules.Field("date", "validation.date_in_past");
        }

        if (!ScheduleRules.IsValidDayRange(startMinute, endMinute))
        {
            return ScheduleRules.Field("endMinute", "validation.hours_invalid");
        }

        ProfessionalId = professionalId;
        Label = label.Trim();
        Weekdays = [.. weekdays.Distinct().Order()];
        Date = date;
        StartMinute = startMinute;
        EndMinute = endMinute;
        return Result.Success();
    }

    public BreakRule ToRule() => new(Weekdays, Date, StartMinute, EndMinute);

    public bool AppliesTo(ProfessionalId professionalId) => ProfessionalId is null || ProfessionalId == professionalId;
}

/// <summary>
/// A professional's time off (vacation, sick leave…): the exact span in UTC instants. An all-day entry runs from the
/// local start of its first day to the local start of the day after its last (D-082).
/// </summary>
public sealed class ProfessionalTimeOff : AggregateRoot<TimeOffId>, IShopOwned, IConcurrencyVersioned
{
    private ProfessionalTimeOff(TimeOffId id, ShopId shopId, ProfessionalId professionalId, DateTimeOffset now)
        : base(id)
    {
        ShopId = shopId;
        ProfessionalId = professionalId;
        CreatedAt = now;
    }

    private ProfessionalTimeOff()
    {
    }

    public ShopId ShopId { get; private set; }

    public ProfessionalId ProfessionalId { get; private set; }

    public TimeOffKind Kind { get; private set; }

    public DateTimeOffset StartsAt { get; private set; }

    public DateTimeOffset EndsAt { get; private set; }

    /// <summary>Entered as whole days (shown as dates only).</summary>
    public bool AllDay { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public static Result<ProfessionalTimeOff> Create(
        TimeOffId id, ShopId shopId, ProfessionalId professionalId, TimeOffKind kind, InstantRange span, bool allDay, string? note, DateTimeOffset now)
    {
        var entry = new ProfessionalTimeOff(id, shopId, professionalId, now);
        var set = entry.Apply(kind, span, allDay, note, now);
        return set.IsFailure ? set.Error : entry;
    }

    /// <summary>The professional is fixed once recorded; to move time off to someone else, delete it and add a new one.</summary>
    public Result Update(TimeOffKind kind, InstantRange span, bool allDay, string? note, DateTimeOffset now)
    {
        var set = Apply(kind, span, allDay, note, now);
        if (set.IsSuccess)
        {
            UpdatedAt = now;
        }

        return set;
    }

    private Result Apply(TimeOffKind kind, InstantRange span, bool allDay, string? note, DateTimeOffset now)
    {
        if (!Enum.IsDefined(kind))
        {
            return ScheduleRules.Field("kind", "validation.invalid");
        }

        if (span.IsEmpty || span.End - span.Start > TimeSpan.FromDays(ScheduleRules.MaxRangeDays))
        {
            return ScheduleRules.Field("endDate", "validation.range_invalid");
        }

        if (span.End <= now)
        {
            return ScheduleRules.Field("endDate", "validation.date_in_past");
        }

        if (note?.Trim().Length > ScheduleRules.MaxNoteLength)
        {
            return ScheduleRules.Field("note", "validation.too_long");
        }

        Kind = kind;
        StartsAt = span.Start;
        EndsAt = span.End;
        AllDay = allDay;
        Note = ScheduleRules.Clean(note);
        return Result.Success();
    }

    public InstantRange Span() => new(StartsAt, EndsAt);
}

public static class ScheduleErrors
{
    public static Error ClosureNotFound() => Error.NotFound("schedule.closure_not_found", "The closure was not found.");

    public static Error BreakNotFound() => Error.NotFound("schedule.break_not_found", "The break was not found.");

    public static Error TimeOffNotFound() => Error.NotFound("schedule.time_off_not_found", "The time off was not found.");

    public static Error ProfessionalNotFound() => Error.NotFound("professional.not_found", "The professional was not found.");

    public static Error ShopNotFound() => Error.NotFound("shop.not_found", "The shop was not found.");

    public static Error OfferNotFound() => Error.NotFound("availability.offer_not_found", "The service or package is not offered.");

    public static Error OfferNotOnlineBookable() =>
        Error.BusinessRule("availability.offer_not_online_bookable", "The service or package cannot be booked online.");

    public static Error ProfessionalNotEligible() =>
        Error.BusinessRule("availability.professional_not_eligible", "The professional does not provide this service or is not available.");

    public static Error InvalidQuery(string field) => ScheduleRules.Field(field, "validation.invalid");
}
