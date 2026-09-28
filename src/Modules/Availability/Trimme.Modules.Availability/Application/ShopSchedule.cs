using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Platform;
using Trimme.BuildingBlocks.Application.Scheduling;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Availability.Domain;
using Trimme.Modules.Availability.Domain.Engine;

namespace Trimme.Modules.Availability.Application;

// The shop's own schedule (spec §13, R-AVL-03): opening hours, professionals' hours, closures, breaks and time off.
// The shop comes from ICurrentTenant, never from the request; no tenant (a suspended shop) is "not found". The tenant
// filter scopes every read, so another shop's id is 404, and composite keys stop a row pointing at another shop's
// professional.

internal sealed record GetShopScheduleQuery : IQuery<ShopScheduleResponse?>;

internal sealed record SetOpeningHoursCommand(IReadOnlyList<HoursIntervalDto> Intervals, uint? Version) : ICommand<Result<OpeningHoursResponse>>;

internal sealed record SetWorkingHoursCommand(Guid ProfessionalId, bool FollowsShopHours, IReadOnlyList<HoursIntervalDto> Intervals, uint? Version)
    : ICommand<Result<ProfessionalHoursResponse>>;

internal sealed record ClosureInput(DateOnly StartDate, DateOnly EndDate, string? Reason);

internal sealed record CreateClosureCommand(ClosureInput Input) : ICommand<Result<ClosureResponse>>;

internal sealed record UpdateClosureCommand(Guid ClosureId, ClosureInput Input, uint Version) : ICommand<Result<ClosureResponse>>;

internal sealed record DeleteClosureCommand(Guid ClosureId) : ICommand<Result>;

internal sealed record PreviewClosureQuery(ClosureInput Input) : IQuery<Result<ConflictPreviewResponse>>;

internal sealed record BreakInput(Guid? ProfessionalId, string Label, IReadOnlyList<DayOfWeek>? Weekdays, DateOnly? Date, int StartMinute, int EndMinute);

internal sealed record CreateBreakCommand(BreakInput Input) : ICommand<Result<BreakResponse>>;

internal sealed record UpdateBreakCommand(Guid BreakId, BreakInput Input, uint Version) : ICommand<Result<BreakResponse>>;

internal sealed record DeleteBreakCommand(Guid BreakId) : ICommand<Result>;

internal sealed record PreviewBreakQuery(BreakInput Input) : IQuery<Result<ConflictPreviewResponse>>;

internal sealed record TimeOffInput(Guid ProfessionalId, TimeOffKind Kind, DateOnly StartDate, DateOnly EndDate, int? StartMinute, int? EndMinute, string? Note);

internal sealed record CreateTimeOffCommand(TimeOffInput Input) : ICommand<Result<TimeOffResponse>>;

internal sealed record UpdateTimeOffCommand(Guid TimeOffId, TimeOffInput Input, uint Version) : ICommand<Result<TimeOffResponse>>;

internal sealed record DeleteTimeOffCommand(Guid TimeOffId) : ICommand<Result>;

internal sealed record PreviewTimeOffQuery(TimeOffInput Input) : IQuery<Result<ConflictPreviewResponse>>;

/// <summary>The signed-in shop with its clock: time zone, now and today in shop-local time.</summary>
internal sealed record ShopScope(ShopSummary Shop, ShopClock Clock, DateTimeOffset Now, DateOnly Today)
{
    public ShopId Id => Shop.Id;
}

/// <summary>Shared lookups of the shop schedule handlers.</summary>
internal sealed class ShopScheduleContext(ICurrentTenant tenant, IShopDirectory shops, IProfessionalDirectory professionals, TimeProvider clock)
{
    public async Task<ShopScope?> ScopeAsync(CancellationToken cancellationToken)
    {
        if (tenant.ShopId is not { } shopId || await shops.FindAsync(shopId, cancellationToken) is not { } shop)
        {
            return null;
        }

        var shopClock = new ShopClock(ScheduleLoader.Zone(shop.TimeZone));
        var now = clock.GetUtcNow();
        return new ShopScope(shop, shopClock, now, shopClock.Date(now));
    }

    /// <summary>One of the shop's own professionals (tenant-filtered), or <see langword="null"/>.</summary>
    public async Task<ProfessionalSummary?> ProfessionalAsync(ShopScope scope, Guid professionalId, CancellationToken cancellationToken) =>
        await professionals.FindAsync(new ProfessionalId(professionalId), cancellationToken) is { } found && found.ShopId == scope.Id ? found : null;

    /// <summary>The time off span: all day from local midnight when no minutes are given, otherwise the exact local times.</summary>
    public static Result<(InstantRange Span, bool AllDay)> TimeOffSpan(ShopClock clock, TimeOffInput input)
    {
        if (input.StartMinute is { } s && (s is < 0 or >= ScheduleRules.MinutesPerDay || s % ScheduleRules.MinuteStep != 0))
        {
            return ScheduleRules.Field("startMinute", "validation.hours_invalid");
        }

        if (input.EndMinute is { } e && (e is <= 0 or > ScheduleRules.MinutesPerDay || e % ScheduleRules.MinuteStep != 0))
        {
            return ScheduleRules.Field("endMinute", "validation.hours_invalid");
        }

        if (input.EndDate < input.StartDate)
        {
            return ScheduleRules.Field("endDate", "validation.range_invalid");
        }

        var start = clock.At(input.StartDate, input.StartMinute ?? 0);
        var end = input.EndMinute is { } endMinute ? clock.At(input.EndDate, endMinute) : clock.At(input.EndDate.AddDays(1), 0);
        return (new InstantRange(start, end), input.StartMinute is null && input.EndMinute is null);
    }
}

/// <summary>Upcoming bookings a change would overlap (DV-A04). The Bookings module supplies them from Phase 10.</summary>
internal sealed class ConflictFinder(IBookedTimeReader bookings, IPlatformSettings settings, ScheduleLoader loader)
{
    public async Task<ConflictPreviewResponse> OverlappingAsync(ShopScope scope, ProfessionalId? professionalId, IEnumerable<InstantRange> ranges, CancellationToken cancellationToken)
    {
        var upcoming = ranges
            .Select(r => r.Start < scope.Now ? r with { Start = scope.Now } : r)
            .Where(r => !r.IsEmpty).ToList();
        if (upcoming.Count == 0)
        {
            return new ConflictPreviewResponse([]);
        }

        var found = await bookings.GetAppointmentsAsync(scope.Id, professionalId, upcoming.Min(r => r.Start), upcoming.Max(r => r.End), cancellationToken);
        return new ConflictPreviewResponse(
        [
            .. found.Where(b => upcoming.Any(r => r.Overlaps(new InstantRange(b.StartsAt, b.EndsAt))))
                .OrderBy(b => b.StartsAt).Select(ScheduleMapping.ToResponse),
        ]);
    }

    /// <summary>Every occurrence of the break within the booking horizon.</summary>
    public async Task<IReadOnlyList<InstantRange>> BreakRangesAsync(ShopScope scope, BreakRule rule, CancellationToken cancellationToken)
    {
        var horizon = (await settings.GetAsync(cancellationToken)).BookingHorizonDays;
        return
        [
            .. Enumerable.Range(0, horizon).Select(scope.Today.AddDays).Where(rule.AppliesOn)
                .Select(d => new InstantRange(scope.Clock.At(d, rule.StartMinute), scope.Clock.At(d, rule.EndMinute))),
        ];
    }

    /// <summary>The opening windows of the closed business days (a closure closes the whole business day).</summary>
    public async Task<IReadOnlyList<InstantRange>> ClosureRangesAsync(ShopScope scope, DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        var hours = await loader.OpeningHoursAsync(scope.Id, cancellationToken);
        var days = Enumerable.Range(0, Math.Max(end.DayNumber - start.DayNumber + 1, 0)).Select(start.AddDays);
        return AvailabilityEngine.Windows(scope.Clock, hours, days).Ranges;
    }
}

internal sealed class GetShopScheduleHandler(TrimmeDbContext db, ShopScheduleContext context, IProfessionalDirectory professionals)
    : IQueryHandler<GetShopScheduleQuery, ShopScheduleResponse?>
{
    public async Task<ShopScheduleResponse?> Handle(GetShopScheduleQuery query, CancellationToken cancellationToken)
    {
        if (await context.ScopeAsync(cancellationToken) is not { } scope)
        {
            return null;
        }

        var opening = await db.Set<ShopOpeningHours>().AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var staff = await professionals.ListByShopAsync(scope.Id, cancellationToken);
        var hours = await db.Set<ProfessionalWorkingHours>().AsNoTracking().ToDictionaryAsync(h => h.ProfessionalId, cancellationToken);
        var closures = await db.Set<ShopClosure>().AsNoTracking().Where(c => c.EndDate >= scope.Today)
            .OrderBy(c => c.StartDate).ToListAsync(cancellationToken);
        var breaks = await db.Set<ScheduleBreak>().AsNoTracking().Where(b => b.Date == null || b.Date >= scope.Today)
            .OrderBy(b => b.StartMinute).ThenBy(b => b.CreatedAt).ToListAsync(cancellationToken);
        var timeOff = await db.Set<ProfessionalTimeOff>().AsNoTracking().Where(t => t.EndsAt > scope.Now)
            .OrderBy(t => t.StartsAt).ToListAsync(cancellationToken);

        return new ShopScheduleResponse(
            scope.Shop.TimeZone,
            scope.Today,
            ToResponse(opening),
            [
                .. staff.OrderByDescending(p => p.IsActive).Select(p => ToResponse(p, hours.GetValueOrDefault(p.Id))),
            ],
            [.. closures.Select(c => ScheduleMapping.ToResponse(c, scope.Today))],
            [.. breaks.Select(ScheduleMapping.ToResponse)],
            [.. timeOff.Select(t => ScheduleMapping.ToResponse(t, scope.Clock, scope.Now))],
            scope.Shop.OnlineBookingPaused,
            scope.Shop.OnlineBookingPausedAt);
    }

    public static OpeningHoursResponse ToResponse(ShopOpeningHours? hours) =>
        new(hours is null ? [] : [.. hours.Week().Select(ScheduleMapping.ToDto)], hours?.Version);

    public static ProfessionalHoursResponse ToResponse(ProfessionalSummary professional, ProfessionalWorkingHours? hours) =>
        new(professional.Id.Value, professional.NameAr, professional.NameEn, professional.IsActive, hours?.FollowsShopHours ?? true,
            hours?.Week() is { } week ? [.. week.Select(ScheduleMapping.ToDto)] : [], hours?.Version);
}

/// <summary>
/// Replaces the whole week. The first save creates the row; later saves carry the version read (409 when stale). The
/// change applies to availability from now on; existing bookings are never cancelled.
/// </summary>
internal sealed class SetOpeningHoursHandler(TrimmeDbContext db, ShopScheduleContext context, TimeProvider clock)
    : ICommandHandler<SetOpeningHoursCommand, Result<OpeningHoursResponse>>
{
    public async Task<Result<OpeningHoursResponse>> Handle(SetOpeningHoursCommand command, CancellationToken cancellationToken)
    {
        if (await context.ScopeAsync(cancellationToken) is not { } scope)
        {
            return ScheduleErrors.ShopNotFound();
        }

        var week = ScheduleMapping.ToWeek(command.Intervals);
        var hours = await db.Set<ShopOpeningHours>().SingleOrDefaultAsync(cancellationToken);
        if (hours is null)
        {
            var created = ShopOpeningHours.Create(EntityId.New<OpeningHoursId>(), scope.Id, week, clock.GetUtcNow());
            if (created.IsFailure)
            {
                return created.Error;
            }

            hours = created.Value;
            db.Add(hours);
        }
        else
        {
            db.Entry(hours).Property(h => h.Version).OriginalValue = command.Version ?? 0;
            var replaced = hours.Replace(week, clock.GetUtcNow());
            if (replaced.IsFailure)
            {
                return replaced.Error;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return GetShopScheduleHandler.ToResponse(hours);
    }
}

internal sealed class SetWorkingHoursHandler(TrimmeDbContext db, ShopScheduleContext context, TimeProvider clock)
    : ICommandHandler<SetWorkingHoursCommand, Result<ProfessionalHoursResponse>>
{
    public async Task<Result<ProfessionalHoursResponse>> Handle(SetWorkingHoursCommand command, CancellationToken cancellationToken)
    {
        if (await context.ScopeAsync(cancellationToken) is not { } scope
            || await context.ProfessionalAsync(scope, command.ProfessionalId, cancellationToken) is not { } professional)
        {
            return ScheduleErrors.ProfessionalNotFound();
        }

        var week = ScheduleMapping.ToWeek(command.Intervals);
        var hours = await db.Set<ProfessionalWorkingHours>().SingleOrDefaultAsync(h => h.ProfessionalId == professional.Id, cancellationToken);
        if (hours is null)
        {
            var created = ProfessionalWorkingHours.Create(EntityId.New<WorkingHoursId>(), scope.Id, professional.Id, command.FollowsShopHours, week, clock.GetUtcNow());
            if (created.IsFailure)
            {
                return created.Error;
            }

            hours = created.Value;
            db.Add(hours);
        }
        else
        {
            db.Entry(hours).Property(h => h.Version).OriginalValue = command.Version ?? 0;
            var replaced = hours.Replace(command.FollowsShopHours, week, clock.GetUtcNow());
            if (replaced.IsFailure)
            {
                return replaced.Error;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return GetShopScheduleHandler.ToResponse(professional, hours);
    }
}

internal sealed class CreateClosureHandler(TrimmeDbContext db, ShopScheduleContext context) : ICommandHandler<CreateClosureCommand, Result<ClosureResponse>>
{
    public async Task<Result<ClosureResponse>> Handle(CreateClosureCommand command, CancellationToken cancellationToken)
    {
        if (await context.ScopeAsync(cancellationToken) is not { } scope)
        {
            return ScheduleErrors.ShopNotFound();
        }

        var input = command.Input;
        var created = ShopClosure.Create(EntityId.New<ShopClosureId>(), scope.Id, input.StartDate, input.EndDate, input.Reason, scope.Today, scope.Now);
        if (created.IsFailure)
        {
            return created.Error;
        }

        db.Add(created.Value);
        await db.SaveChangesAsync(cancellationToken);
        return ScheduleMapping.ToResponse(created.Value, scope.Today);
    }
}

internal sealed class UpdateClosureHandler(TrimmeDbContext db, ShopScheduleContext context) : ICommandHandler<UpdateClosureCommand, Result<ClosureResponse>>
{
    public async Task<Result<ClosureResponse>> Handle(UpdateClosureCommand command, CancellationToken cancellationToken)
    {
        var id = new ShopClosureId(command.ClosureId);
        if (await context.ScopeAsync(cancellationToken) is not { } scope
            || await db.Set<ShopClosure>().SingleOrDefaultAsync(c => c.Id == id, cancellationToken) is not { } closure)
        {
            return ScheduleErrors.ClosureNotFound();
        }

        db.Entry(closure).Property(c => c.Version).OriginalValue = command.Version;
        var input = command.Input;
        var updated = closure.Update(input.StartDate, input.EndDate, input.Reason, scope.Today, scope.Now);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return ScheduleMapping.ToResponse(closure, scope.Today);
    }
}

internal sealed class DeleteClosureHandler(TrimmeDbContext db, ShopScheduleContext context) : ICommandHandler<DeleteClosureCommand, Result>
{
    public async Task<Result> Handle(DeleteClosureCommand command, CancellationToken cancellationToken)
    {
        var id = new ShopClosureId(command.ClosureId);
        if (await context.ScopeAsync(cancellationToken) is null
            || await db.Set<ShopClosure>().SingleOrDefaultAsync(c => c.Id == id, cancellationToken) is not { } closure)
        {
            return ScheduleErrors.ClosureNotFound();
        }

        db.Remove(closure);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class PreviewClosureHandler(ShopScheduleContext context, ConflictFinder conflicts) : IQueryHandler<PreviewClosureQuery, Result<ConflictPreviewResponse>>
{
    public async Task<Result<ConflictPreviewResponse>> Handle(PreviewClosureQuery query, CancellationToken cancellationToken)
    {
        if (await context.ScopeAsync(cancellationToken) is not { } scope)
        {
            return ScheduleErrors.ShopNotFound();
        }

        if (query.Input.EndDate < query.Input.StartDate || query.Input.EndDate.DayNumber - query.Input.StartDate.DayNumber >= ScheduleRules.MaxRangeDays)
        {
            return ScheduleRules.Field("endDate", "validation.range_invalid");
        }

        var ranges = await conflicts.ClosureRangesAsync(scope, query.Input.StartDate, query.Input.EndDate, cancellationToken);
        return await conflicts.OverlappingAsync(scope, professionalId: null, ranges, cancellationToken);
    }
}

internal sealed class CreateBreakHandler(TrimmeDbContext db, ShopScheduleContext context) : ICommandHandler<CreateBreakCommand, Result<BreakResponse>>
{
    public async Task<Result<BreakResponse>> Handle(CreateBreakCommand command, CancellationToken cancellationToken)
    {
        if (await context.ScopeAsync(cancellationToken) is not { } scope)
        {
            return ScheduleErrors.ShopNotFound();
        }

        var input = command.Input;
        ProfessionalId? professionalId = null;
        if (input.ProfessionalId is { } requested)
        {
            if (await context.ProfessionalAsync(scope, requested, cancellationToken) is not { } professional)
            {
                return ScheduleErrors.ProfessionalNotFound();
            }

            professionalId = professional.Id;
        }

        var created = ScheduleBreak.Create(
            EntityId.New<ScheduleBreakId>(), scope.Id, professionalId, input.Label, input.Weekdays ?? [], input.Date, input.StartMinute, input.EndMinute, scope.Today, scope.Now);
        if (created.IsFailure)
        {
            return created.Error;
        }

        db.Add(created.Value);
        await db.SaveChangesAsync(cancellationToken);
        return ScheduleMapping.ToResponse(created.Value);
    }
}

internal sealed class UpdateBreakHandler(TrimmeDbContext db, ShopScheduleContext context) : ICommandHandler<UpdateBreakCommand, Result<BreakResponse>>
{
    public async Task<Result<BreakResponse>> Handle(UpdateBreakCommand command, CancellationToken cancellationToken)
    {
        var id = new ScheduleBreakId(command.BreakId);
        if (await context.ScopeAsync(cancellationToken) is not { } scope
            || await db.Set<ScheduleBreak>().SingleOrDefaultAsync(b => b.Id == id, cancellationToken) is not { } entry)
        {
            return ScheduleErrors.BreakNotFound();
        }

        var input = command.Input;
        ProfessionalId? professionalId = null;
        if (input.ProfessionalId is { } requested)
        {
            if (await context.ProfessionalAsync(scope, requested, cancellationToken) is not { } professional)
            {
                return ScheduleErrors.ProfessionalNotFound();
            }

            professionalId = professional.Id;
        }

        db.Entry(entry).Property(b => b.Version).OriginalValue = command.Version;
        var updated = entry.Update(professionalId, input.Label, input.Weekdays ?? [], input.Date, input.StartMinute, input.EndMinute, scope.Today, scope.Now);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return ScheduleMapping.ToResponse(entry);
    }
}

internal sealed class DeleteBreakHandler(TrimmeDbContext db, ShopScheduleContext context) : ICommandHandler<DeleteBreakCommand, Result>
{
    public async Task<Result> Handle(DeleteBreakCommand command, CancellationToken cancellationToken)
    {
        var id = new ScheduleBreakId(command.BreakId);
        if (await context.ScopeAsync(cancellationToken) is null
            || await db.Set<ScheduleBreak>().SingleOrDefaultAsync(b => b.Id == id, cancellationToken) is not { } entry)
        {
            return ScheduleErrors.BreakNotFound();
        }

        db.Remove(entry);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class PreviewBreakHandler(ShopScheduleContext context, ConflictFinder conflicts) : IQueryHandler<PreviewBreakQuery, Result<ConflictPreviewResponse>>
{
    public async Task<Result<ConflictPreviewResponse>> Handle(PreviewBreakQuery query, CancellationToken cancellationToken)
    {
        if (await context.ScopeAsync(cancellationToken) is not { } scope)
        {
            return ScheduleErrors.ShopNotFound();
        }

        var input = query.Input;
        ProfessionalId? professionalId = null;
        if (input.ProfessionalId is { } requested)
        {
            if (await context.ProfessionalAsync(scope, requested, cancellationToken) is not { } professional)
            {
                return ScheduleErrors.ProfessionalNotFound();
            }

            professionalId = professional.Id;
        }

        // Validate with the entity's own rules (without saving anything).
        var probe = ScheduleBreak.Create(
            EntityId.New<ScheduleBreakId>(), scope.Id, professionalId, input.Label ?? string.Empty, input.Weekdays ?? [], input.Date, input.StartMinute, input.EndMinute, scope.Today, scope.Now);
        if (probe.IsFailure)
        {
            return probe.Error;
        }

        var ranges = await conflicts.BreakRangesAsync(scope, probe.Value.ToRule(), cancellationToken);
        return await conflicts.OverlappingAsync(scope, professionalId, ranges, cancellationToken);
    }
}

internal sealed class CreateTimeOffHandler(TrimmeDbContext db, ShopScheduleContext context) : ICommandHandler<CreateTimeOffCommand, Result<TimeOffResponse>>
{
    public async Task<Result<TimeOffResponse>> Handle(CreateTimeOffCommand command, CancellationToken cancellationToken)
    {
        if (await context.ScopeAsync(cancellationToken) is not { } scope
            || await context.ProfessionalAsync(scope, command.Input.ProfessionalId, cancellationToken) is not { } professional)
        {
            return ScheduleErrors.ProfessionalNotFound();
        }

        var span = ShopScheduleContext.TimeOffSpan(scope.Clock, command.Input);
        if (span.IsFailure)
        {
            return span.Error;
        }

        var created = ProfessionalTimeOff.Create(
            EntityId.New<TimeOffId>(), scope.Id, professional.Id, command.Input.Kind, span.Value.Span, span.Value.AllDay, command.Input.Note, scope.Now);
        if (created.IsFailure)
        {
            return created.Error;
        }

        db.Add(created.Value);
        await db.SaveChangesAsync(cancellationToken);
        return ScheduleMapping.ToResponse(created.Value, scope.Clock, scope.Now);
    }
}

internal sealed class UpdateTimeOffHandler(TrimmeDbContext db, ShopScheduleContext context) : ICommandHandler<UpdateTimeOffCommand, Result<TimeOffResponse>>
{
    public async Task<Result<TimeOffResponse>> Handle(UpdateTimeOffCommand command, CancellationToken cancellationToken)
    {
        var id = new TimeOffId(command.TimeOffId);
        if (await context.ScopeAsync(cancellationToken) is not { } scope
            || await db.Set<ProfessionalTimeOff>().SingleOrDefaultAsync(t => t.Id == id, cancellationToken) is not { } entry)
        {
            return ScheduleErrors.TimeOffNotFound();
        }

        if (entry.ProfessionalId.Value != command.Input.ProfessionalId)
        {
            return ScheduleRules.Field("professionalId", "validation.immutable");
        }

        var span = ShopScheduleContext.TimeOffSpan(scope.Clock, command.Input);
        if (span.IsFailure)
        {
            return span.Error;
        }

        db.Entry(entry).Property(t => t.Version).OriginalValue = command.Version;
        var updated = entry.Update(command.Input.Kind, span.Value.Span, span.Value.AllDay, command.Input.Note, scope.Now);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return ScheduleMapping.ToResponse(entry, scope.Clock, scope.Now);
    }
}

internal sealed class DeleteTimeOffHandler(TrimmeDbContext db, ShopScheduleContext context) : ICommandHandler<DeleteTimeOffCommand, Result>
{
    public async Task<Result> Handle(DeleteTimeOffCommand command, CancellationToken cancellationToken)
    {
        var id = new TimeOffId(command.TimeOffId);
        if (await context.ScopeAsync(cancellationToken) is null
            || await db.Set<ProfessionalTimeOff>().SingleOrDefaultAsync(t => t.Id == id, cancellationToken) is not { } entry)
        {
            return ScheduleErrors.TimeOffNotFound();
        }

        db.Remove(entry);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class PreviewTimeOffHandler(ShopScheduleContext context, ConflictFinder conflicts) : IQueryHandler<PreviewTimeOffQuery, Result<ConflictPreviewResponse>>
{
    public async Task<Result<ConflictPreviewResponse>> Handle(PreviewTimeOffQuery query, CancellationToken cancellationToken)
    {
        if (await context.ScopeAsync(cancellationToken) is not { } scope
            || await context.ProfessionalAsync(scope, query.Input.ProfessionalId, cancellationToken) is not { } professional)
        {
            return ScheduleErrors.ProfessionalNotFound();
        }

        var span = ShopScheduleContext.TimeOffSpan(scope.Clock, query.Input);
        if (span.IsFailure)
        {
            return span.Error;
        }

        return await conflicts.OverlappingAsync(scope, professional.Id, [span.Value.Span], cancellationToken);
    }
}
