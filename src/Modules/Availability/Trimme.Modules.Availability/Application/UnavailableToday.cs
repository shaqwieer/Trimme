using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Auditing;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Notifications;
using Trimme.BuildingBlocks.Application.Scheduling;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Availability.Domain;
using Trimme.Modules.Availability.Domain.Engine;

namespace Trimme.Modules.Availability.Application;

// «غير متوفر اليوم» (D-155, the client's 1-2): a barber who cannot work the rest of today. The barber presses it for
// themselves, or the owner for any of the shop's barbers; it is time off from now to the end of the shop's day, so their
// remaining times vanish and «أي مختص» never picks them. Their bookings stay: the shop's other barbers are told and may give
// each one to a colleague, change its time or cancel it with a reason (BarberScope's fifth exception, in Bookings). It is
// recorded with who and when, audited, and either of them can undo it the same day. Planned leave stays the owner's time off.

/// <summary>
/// A barber marked «غير متوفر اليوم» (D-155): since when, until the end of the shop's day, and how many of their open
/// bookings are still ahead today (the ones the colleagues handle).
/// </summary>
public sealed record UnavailableBarberResponse(Guid ProfessionalId, string NameAr, string NameEn, DateTimeOffset Since, DateTimeOffset Until, int Bookings);

internal sealed record ListUnavailableTodayQuery : IQuery<IReadOnlyList<UnavailableBarberResponse>?>;

internal sealed record MarkUnavailableTodayCommand(Guid? ProfessionalId) : ICommand<Result<UnavailableBarberResponse>>;

internal sealed record UndoUnavailableTodayCommand(Guid? ProfessionalId) : ICommand<Result>;

/// <summary><see cref="IUnavailableBarbers"/>: the marks of today, and the days marked per barber, from the time-off rows.</summary>
internal sealed class UnavailableBarbers(TrimmeDbContext db, IShopDirectory shops, TimeProvider clock) : IUnavailableBarbers
{
    public async Task<IReadOnlyList<UnavailableBarber>> TodayAsync(ShopId shopId, CancellationToken cancellationToken)
    {
        if (await shops.FindAsync(shopId, cancellationToken) is not { } shop)
        {
            return [];
        }

        var shopClock = new ShopClock(ScheduleLoader.Zone(shop.TimeZone));
        var now = clock.GetUtcNow();
        var today = shopClock.Date(now);
        var dayStart = shopClock.StartOfDay(today);
        var dayEnd = shopClock.StartOfDay(today.AddDays(1));
        var marks = await db.Set<ProfessionalTimeOff>().AsNoTracking()
            .Where(t => t.ShopId == shopId && t.Kind == TimeOffKind.Unavailable && t.EndsAt > now && t.StartsAt < dayEnd)
            .OrderBy(t => t.StartsAt)
            .ToListAsync(cancellationToken);
        return [.. marks
            .GroupBy(t => t.ProfessionalId)
            .Select(g => g.First())
            .Select(t => new UnavailableBarber(t.ProfessionalId, t.StartsAt, dayStart, dayEnd, t.CreatedByUserId))];
    }

    public async Task<IReadOnlyDictionary<ProfessionalId, int>> DaysAsync(ShopId shopId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        (await db.Set<ProfessionalTimeOff>().AsNoTracking()
            .Where(t => t.ShopId == shopId && t.Kind == TimeOffKind.Unavailable && t.StartsAt >= from && t.StartsAt < to)
            .GroupBy(t => t.ProfessionalId)
            .Select(g => new { ProfessionalId = g.Key, Days = g.Count() })
            .ToListAsync(cancellationToken))
        .ToDictionary(x => x.ProfessionalId, x => x.Days);
}

/// <summary>
/// Who may mark whom (D-155): a barber's login only themselves (a professional id asked for is ignored); the owner any of
/// the shop's active barbers, with <c>Shop.Schedule.Manage</c>.
/// </summary>
internal sealed class UnavailableTodayAccess(ShopScheduleContext context, ICurrentUser user, IPermissionResolver permissions)
{
    public const string OwnerPermission = "Shop.Schedule.Manage";

    public async Task<Result<(ShopScope Scope, ProfessionalSummary Professional)>> ResolveAsync(Guid? asked, CancellationToken cancellationToken)
    {
        if (await context.ScopeAsync(cancellationToken) is not { } scope)
        {
            return ScheduleErrors.ShopNotFound();
        }

        if (user.ProfessionalId is null)
        {
            if (user.UserId is not { } userId || !(await permissions.GetPermissionsAsync(userId, cancellationToken)).Contains(OwnerPermission))
            {
                return Error.Forbidden("auth.forbidden", "Only the barber or the shop's owner may do this.");
            }

            if (asked is null)
            {
                return ScheduleRules.Field("professionalId", "validation.required");
            }
        }

        var target = user.ProfessionalId ?? asked!.Value;
        if (await context.ProfessionalAsync(scope, target, cancellationToken) is not { IsActive: true } professional)
        {
            return ScheduleErrors.ProfessionalNotFound();
        }

        return (scope, professional);
    }
}

internal sealed class ListUnavailableTodayHandler(
    ShopScheduleContext context, IUnavailableBarbers unavailable, IProfessionalDirectory professionals, IBookedTimeReader bookings)
    : IQueryHandler<ListUnavailableTodayQuery, IReadOnlyList<UnavailableBarberResponse>?>
{
    public async Task<IReadOnlyList<UnavailableBarberResponse>?> Handle(ListUnavailableTodayQuery query, CancellationToken cancellationToken)
    {
        if (await context.ScopeAsync(cancellationToken) is not { } scope)
        {
            return null;
        }

        var list = new List<UnavailableBarberResponse>();
        foreach (var mark in await unavailable.TodayAsync(scope.Id, cancellationToken))
        {
            if (await professionals.FindAsync(mark.ProfessionalId, cancellationToken) is not { } professional)
            {
                continue;
            }

            var ahead = await bookings.GetAppointmentsAsync(scope.Id, mark.ProfessionalId, scope.Now, mark.DayEnd, cancellationToken);
            list.Add(new UnavailableBarberResponse(mark.ProfessionalId.Value, professional.NameAr, professional.NameEn, mark.Since, mark.DayEnd, ahead.Count));
        }

        return list;
    }
}

/// <summary>
/// Marks the barber «غير متوفر اليوم» (D-155). Refused while already marked (409 <c>schedule.already_unavailable</c>).
/// The owner hears it when the barber pressed it, the barber when the owner did; the shop's other barbers get «حجوزات حلاق
/// غير متوفر» when bookings remain today.
/// </summary>
internal sealed class MarkUnavailableTodayHandler(
    TrimmeDbContext db,
    UnavailableTodayAccess access,
    ICurrentUser user,
    IBookedTimeReader bookings,
    IProfessionalDirectory professionals,
    IStaffDirectory staff,
    INotificationCenter notices,
    IAuditLog audit) : ICommandHandler<MarkUnavailableTodayCommand, Result<UnavailableBarberResponse>>
{
    public const string OwnerNoticeKind = "professional.unavailable_today";
    public const string ColleagueNoticeKind = "professional.unavailable_bookings";

    public async Task<Result<UnavailableBarberResponse>> Handle(MarkUnavailableTodayCommand command, CancellationToken cancellationToken)
    {
        var resolved = await access.ResolveAsync(command.ProfessionalId, cancellationToken);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        var (scope, professional) = resolved.Value;
        var dayEnd = scope.Clock.StartOfDay(scope.Today.AddDays(1));
        if (await db.Set<ProfessionalTimeOff>().AnyAsync(
                t => t.ProfessionalId == professional.Id && t.Kind == TimeOffKind.Unavailable && t.EndsAt > scope.Now, cancellationToken))
        {
            return Error.Conflict("schedule.already_unavailable", "The barber is already marked unavailable today.");
        }

        var mark = ProfessionalTimeOff.MarkUnavailable(EntityId.New<TimeOffId>(), scope.Id, professional.Id, scope.Now, dayEnd, user.UserId);
        if (mark.IsFailure)
        {
            return mark.Error;
        }

        db.Add(mark.Value);
        var byBarber = user.ProfessionalId is not null;
        audit.Record(new AuditRecord(
            "schedule.unavailable_today", "Professional", professional.Id.ToString(), scope.Id,
            byBarber ? "Marked unavailable today by the barber" : "Marked unavailable today by the owner",
            SummaryArgs: AuditArgs.Of(("by", byBarber ? "barber" : "owner"), ("until", dayEnd))));

        var ahead = await bookings.GetAppointmentsAsync(scope.Id, professional.Id, scope.Now, dayEnd, cancellationToken);
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["professionalId"] = professional.Id.Value.ToString(),
            ["professionalNameAr"] = professional.NameAr,
            ["professionalNameEn"] = string.IsNullOrWhiteSpace(professional.NameEn) ? professional.NameAr : professional.NameEn,
            ["since"] = scope.Now.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["bookings"] = ahead.Count.ToString(CultureInfo.InvariantCulture),
            ["by"] = byBarber ? "barber" : "owner",
        };
        var key = string.Create(CultureInfo.InvariantCulture, $"unavailable:{professional.Id.Value:N}:{scope.Today:yyyyMMdd}:{scope.Now.UtcTicks}");
        if (byBarber)
        {
            // The owner's dashboard notice.
            await notices.NotifyShopAsync(scope.Id, new InAppNotice(OwnerNoticeKind, key, parameters), cancellationToken);
        }
        else if (await staff.BarberAccountAsync(professional.Id, cancellationToken) is { } own)
        {
            await notices.NotifyUserAsync(own, new InAppNotice(OwnerNoticeKind, key, parameters), cancellationToken);
        }

        if (ahead.Count > 0)
        {
            foreach (var colleague in await professionals.ListActiveProfilesAsync([scope.Id], cancellationToken))
            {
                if (colleague.Id != professional.Id && await staff.BarberAccountAsync(colleague.Id, cancellationToken) is { } account)
                {
                    await notices.NotifyUserAsync(account, new InAppNotice(ColleagueNoticeKind, key, parameters), cancellationToken);
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await notices.PushPendingAsync(cancellationToken);
        return new UnavailableBarberResponse(professional.Id.Value, professional.NameAr, professional.NameEn, scope.Now, dayEnd, ahead.Count);
    }
}

/// <summary>Takes the mark back the same day (D-155): the barber's remaining times come back. 409 <c>schedule.not_unavailable</c> without one.</summary>
internal sealed class UndoUnavailableTodayHandler(TrimmeDbContext db, UnavailableTodayAccess access, ICurrentUser user, IAuditLog audit)
    : ICommandHandler<UndoUnavailableTodayCommand, Result>
{
    public async Task<Result> Handle(UndoUnavailableTodayCommand command, CancellationToken cancellationToken)
    {
        var resolved = await access.ResolveAsync(command.ProfessionalId, cancellationToken);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        var (scope, professional) = resolved.Value;
        var marks = await db.Set<ProfessionalTimeOff>()
            .Where(t => t.ProfessionalId == professional.Id && t.Kind == TimeOffKind.Unavailable && t.EndsAt > scope.Now)
            .ToListAsync(cancellationToken);
        if (marks.Count == 0)
        {
            return Error.Conflict("schedule.not_unavailable", "The barber is not marked unavailable today.");
        }

        db.RemoveRange(marks);
        audit.Record(new AuditRecord(
            "schedule.unavailable_today_undone", "Professional", professional.Id.ToString(), scope.Id,
            "Unavailable today taken back",
            SummaryArgs: AuditArgs.Of(("by", user.ProfessionalId is not null ? "barber" : "owner"))));
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
