using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Web.Errors;
using Trimme.BuildingBlocks.Web.Security;
using Trimme.Modules.Availability.Application;
using Trimme.Modules.Availability.Application.Public;
using Trimme.Modules.Availability.Domain;

namespace Trimme.Modules.Availability.Api;

/// <summary>The whole week of opening hours. Send the <c>Version</c> read (null only before the first save); stale → 409.</summary>
public sealed record OpeningHoursRequest(IReadOnlyList<HoursIntervalDto> Intervals, uint? Version);

/// <summary>A professional's hours: follow the shop's opening hours, or their own weekly intervals.</summary>
public sealed record WorkingHoursRequest(bool FollowsShopHours, IReadOnlyList<HoursIntervalDto>? Intervals, uint? Version);

/// <summary>Closed days, inclusive. <c>Version</c> is needed to edit.</summary>
public sealed record ClosureRequest(DateOnly StartDate, DateOnly EndDate, string? Reason, uint? Version);

/// <summary>
/// A break for one professional or everyone (<c>ProfessionalId</c> null): weekly on <c>Weekdays</c>, or once on
/// <c>Date</c> (exactly one of the two). Minutes from local midnight, within the day, on 5-minute steps.
/// </summary>
public sealed record BreakRequest(Guid? ProfessionalId, string Label, IReadOnlyList<DayOfWeek>? Weekdays, DateOnly? Date, int StartMinute, int EndMinute, uint? Version);

/// <summary>
/// Time off from <c>StartDate</c> to <c>EndDate</c> (inclusive, shop-local). Without minutes it covers whole days;
/// otherwise it starts at <c>StartMinute</c> of the first day and ends at <c>EndMinute</c> of the last.
/// </summary>
public sealed record TimeOffRequest(Guid ProfessionalId, TimeOffKind Kind, DateOnly StartDate, DateOnly EndDate, int? StartMinute, int? EndMinute, string? Note, uint? Version);

internal static class AvailabilityEndpoints
{
    // Identity owns the permission catalogue; the endpoint matrix test fails if a code is not in it.
    private const string ScheduleRead = "Shop.Schedule.Read";
    private const string ScheduleManage = "Shop.Schedule.Manage";
    private const string WalkIn = "Shop.Bookings.CreateWalkIn";

    public static void Map(IEndpointRouteBuilder api)
    {
        MapShop(api);
        MapPublic(api.MapGroup("/public/shops/{slug}/availability").WithTags("Public"));
    }

    private static void MapShop(IEndpointRouteBuilder api)
    {
        api.MapGet("/shop/availability/walk-in", async (Guid? serviceId, Guid? packageId, DateOnly? date, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new GetWalkInOptionsQuery(serviceId, packageId, date), ct)).ToHttpResult())
            .RequirePermission(WalkIn).WithTags("Shop: bookings")
            .WithName("GetWalkInOptions")
            .WithSummary("The professionals who can do a walk-in of this service or package: free now, next free time and the day's free starts.")
            .Produces<WalkInOptionsResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        var schedule = api.MapGroup("/shop/schedule").WithTags("Shop: schedule");
        schedule.MapGet("/", async (IDispatcher d, CancellationToken ct) =>
                await d.Send(new GetShopScheduleQuery(), ct) is { } found ? TypedResults.Ok(found) : ScheduleErrors.ShopNotFound().ToProblem())
            .RequirePermission(ScheduleRead)
            .WithName("GetShopSchedule").WithSummary("Opening hours, professionals' hours, current and upcoming closures, breaks and time off.")
            .Produces<ShopScheduleResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        schedule.MapPut("/opening-hours", async (OpeningHoursRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new SetOpeningHoursCommand(r.Intervals ?? [], r.Version), ct)).ToHttpResult())
            .RequirePermission(ScheduleManage)
            .WithName("SetShopOpeningHours").WithSummary("Replaces the weekly opening hours; applies from now on, bookings are never cancelled.")
            .Produces<OpeningHoursResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict);

        var closures = schedule.MapGroup("/closures");
        closures.MapPost("/", async (ClosureRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new CreateClosureCommand(Closure(r)), ct)).ToHttpResult(c => TypedResults.Created($"/api/v1/shop/schedule/closures/{c.Id}", c)))
            .RequirePermission(ScheduleManage)
            .WithName("CreateShopClosure").WithSummary("Closes the shop on whole days (no slots on those business days).")
            .Produces<ClosureResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest);
        closures.MapPut("/{closureId:guid}", async (Guid closureId, ClosureRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new UpdateClosureCommand(closureId, Closure(r), r.Version ?? 0), ct)).ToHttpResult())
            .RequirePermission(ScheduleManage)
            .WithName("UpdateShopClosure").WithSummary("Edits a closure (optimistic concurrency).")
            .Produces<ClosureResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        closures.MapDelete("/{closureId:guid}", async (Guid closureId, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new DeleteClosureCommand(closureId), ct)).ToHttpResult())
            .RequirePermission(ScheduleManage)
            .WithName("DeleteShopClosure").WithSummary("Removes a closure.")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);
        closures.MapPost("/preview", async (ClosureRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new PreviewClosureQuery(Closure(r)), ct)).ToHttpResult())
            .RequirePermission(ScheduleManage)
            .WithName("PreviewShopClosure").WithSummary("Upcoming bookings the closure would overlap (nothing is saved or cancelled).")
            .Produces<ConflictPreviewResponse>().ProducesProblem(StatusCodes.Status400BadRequest);

        var breaks = schedule.MapGroup("/breaks");
        breaks.MapPost("/", async (BreakRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new CreateBreakCommand(Break(r)), ct)).ToHttpResult(b => TypedResults.Created($"/api/v1/shop/schedule/breaks/{b.Id}", b)))
            .RequirePermission(ScheduleManage)
            .WithName("CreateScheduleBreak").WithSummary("Adds a weekly or one-off break for one professional or everyone.")
            .Produces<BreakResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
        breaks.MapPut("/{breakId:guid}", async (Guid breakId, BreakRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new UpdateBreakCommand(breakId, Break(r), r.Version ?? 0), ct)).ToHttpResult())
            .RequirePermission(ScheduleManage)
            .WithName("UpdateScheduleBreak").WithSummary("Edits a break (optimistic concurrency).")
            .Produces<BreakResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        breaks.MapDelete("/{breakId:guid}", async (Guid breakId, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new DeleteBreakCommand(breakId), ct)).ToHttpResult())
            .RequirePermission(ScheduleManage)
            .WithName("DeleteScheduleBreak").WithSummary("Removes a break.")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);
        breaks.MapPost("/preview", async (BreakRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new PreviewBreakQuery(Break(r)), ct)).ToHttpResult())
            .RequirePermission(ScheduleManage)
            .WithName("PreviewScheduleBreak").WithSummary("Upcoming bookings (within the booking horizon) the break would overlap.")
            .Produces<ConflictPreviewResponse>().ProducesProblem(StatusCodes.Status400BadRequest);

        var timeOff = schedule.MapGroup("/time-off");
        timeOff.MapPost("/", async (TimeOffRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new CreateTimeOffCommand(TimeOff(r)), ct)).ToHttpResult(t => TypedResults.Created($"/api/v1/shop/schedule/time-off/{t.Id}", t)))
            .RequirePermission(ScheduleManage)
            .WithName("CreateTimeOff").WithSummary("Records a professional's time off (vacation, sick leave…).")
            .Produces<TimeOffResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
        timeOff.MapPut("/{timeOffId:guid}", async (Guid timeOffId, TimeOffRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new UpdateTimeOffCommand(timeOffId, TimeOff(r), r.Version ?? 0), ct)).ToHttpResult())
            .RequirePermission(ScheduleManage)
            .WithName("UpdateTimeOff").WithSummary("Edits time off (same professional; optimistic concurrency).")
            .Produces<TimeOffResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        timeOff.MapDelete("/{timeOffId:guid}", async (Guid timeOffId, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new DeleteTimeOffCommand(timeOffId), ct)).ToHttpResult())
            .RequirePermission(ScheduleManage)
            .WithName("DeleteTimeOff").WithSummary("Removes time off.")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);
        timeOff.MapPost("/preview", async (TimeOffRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new PreviewTimeOffQuery(TimeOff(r)), ct)).ToHttpResult())
            .RequirePermission(ScheduleManage)
            .WithName("PreviewTimeOff").WithSummary("Upcoming bookings of the professional the time off would overlap.")
            .Produces<ConflictPreviewResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPut("/shop/professionals/{professionalId:guid}/working-hours", async (Guid professionalId, WorkingHoursRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new SetWorkingHoursCommand(professionalId, r.FollowsShopHours, r.Intervals ?? [], r.Version), ct)).ToHttpResult())
            .RequirePermission(ScheduleManage).WithTags("Shop: schedule")
            .WithName("SetProfessionalWorkingHours").WithSummary("Sets one of the shop's own professionals' weekly hours (never their profile).")
            .Produces<ProfessionalHoursResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static void MapPublic(RouteGroupBuilder group)
    {
        group.MapGet("/dates", async (string slug, Guid? serviceId, [FromQuery] Guid[]? serviceIds, Guid? packageId, Guid? professionalId, DateOnly? from, DateOnly? to, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new GetAvailableDatesQuery(slug, serviceId, packageId, professionalId, from, to, serviceIds), ct)).ToHttpResult())
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Availability)
            .WithName("GetAvailableDates")
            .WithSummary("Bookable slot counts per local date (default: 14 days from today, at most 31) for a service, several services booked together (serviceIds) or a package, one professional or any.")
            .Produces<AvailableDatesResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        group.MapGet("/slots", async (string slug, Guid? serviceId, [FromQuery] Guid[]? serviceIds, Guid? packageId, Guid? professionalId, DateOnly date, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new GetAvailableSlotsQuery(slug, serviceId, packageId, professionalId, date, serviceIds), ct)).ToHttpResult())
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Availability)
            .WithName("GetAvailableSlots")
            .WithSummary("Only genuinely bookable starts of one local date, with the professionals free for each (any professional when none is given).")
            .Produces<AvailableSlotsResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static ClosureInput Closure(ClosureRequest r) => new(r.StartDate, r.EndDate, r.Reason);

    private static BreakInput Break(BreakRequest r) => new(r.ProfessionalId, r.Label ?? string.Empty, r.Weekdays, r.Date, r.StartMinute, r.EndMinute);

    private static TimeOffInput TimeOff(TimeOffRequest r) => new(r.ProfessionalId, r.Kind, r.StartDate, r.EndDate, r.StartMinute, r.EndMinute, r.Note);
}
