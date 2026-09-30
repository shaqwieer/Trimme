using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Paging;
using Trimme.BuildingBlocks.Application.Qr;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Errors;
using Trimme.BuildingBlocks.Web.Security;
using Trimme.Modules.Bookings.Application;
using Trimme.Modules.Bookings.Application.Admin;
using Trimme.Modules.Bookings.Application.Customer;
using Trimme.Modules.Bookings.Domain;

namespace Trimme.Modules.Bookings.Api;

/// <summary>
/// A customer's online booking of a published service or package (exactly one of the two). Without a professional the
/// server picks a free one (D-012). The start must be one of the offered slots.
/// </summary>
public sealed record CreateBookingRequest(string ShopSlug, Guid? ServiceId, Guid? PackageId, Guid? ProfessionalId, DateTimeOffset StartsAt, string? Note);

/// <summary>Moves the booking to another offered start (and optionally another eligible professional). Send the version read.</summary>
public sealed record RescheduleBookingRequest(DateTimeOffset StartsAt, Guid? ProfessionalId, uint Version);

public sealed record CancelBookingRequest(string? Reason, uint Version);

/// <summary>
/// A walk-in (D-035): without <c>StartsAt</c> it starts now and is marked Arrived; otherwise it is Confirmed. The
/// customer's name only. An <c>Idempotency-Key</c> header is honoured when sent.
/// </summary>
public sealed record WalkInRequest(Guid? ServiceId, Guid? PackageId, Guid ProfessionalId, DateTimeOffset? StartsAt, string CustomerName, string? Note);

/// <summary>Confirmed, Arrived, Completed, NoShow or CancelledByShop (reason required). Send the version read.</summary>
public sealed record BookingTransitionRequest(BookingStatus To, string? Reason, uint Version);

public sealed record BookingNoteRequest(string Text);

public sealed record AdminCancelBookingRequest(string Reason, uint Version);

/// <summary>An admin reschedule: the new start, optionally another eligible professional, a reason (≥ 5 characters) and the version read.</summary>
public sealed record AdminRescheduleBookingRequest(DateTimeOffset StartsAt, Guid? ProfessionalId, string? Reason, uint Version);

internal static class BookingEndpoints
{
    public const string IdempotencyHeader = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";

    // Identity owns the permission catalogue; the endpoint matrix test fails if a code is not in it.
    private const string ShopRead = "Shop.Bookings.Read";
    private const string ShopUpdateStatus = "Shop.Bookings.UpdateStatus";
    private const string ShopWalkIn = "Shop.Bookings.CreateWalkIn";
    private const string AdminView = "Admin.Bookings.View";
    private const string AdminIntervene = "Admin.Bookings.Intervene";

    public static void Map(IEndpointRouteBuilder api)
    {
        MapCustomer(api);
        MapShop(api.MapGroup("/shop/bookings").WithTags("Shop: bookings"));
        MapBoard(api);
        MapAdmin(api.MapGroup("/admin/bookings").WithTags("Admin: bookings"));
    }

    /// <summary>The scan in the first-party attribution cookie (R-QR-02); read here only, never for walk-ins or staff bookings.</summary>
    private static Guid? QrScan(HttpContext http) =>
        Guid.TryParse(http.Request.Cookies[QrAttributionCookie.Name], out var visit) && visit != Guid.Empty ? visit : null;

    private static void MapCustomer(IEndpointRouteBuilder api)
    {
        api.MapPost("/bookings", async ([FromHeader(Name = IdempotencyHeader)] string? key, CreateBookingRequest r, IDispatcher d, HttpContext http, CancellationToken ct) =>
                KeyError(key) is { } missing
                    ? missing.ToProblem()
                    : Created(http, await d.Send(new CreateOnlineBookingCommand(r.ShopSlug ?? string.Empty, r.ServiceId, r.PackageId, r.ProfessionalId, r.StartsAt, r.Note, key!, QrScan(http)), ct)))
            .RequireUserType(UserTypes.Customer).RequireRateLimiting(RateLimitPolicies.Booking).WithTags("Customer: bookings")
            .WithName("CreateBooking")
            .WithSummary("Books an offered slot (Idempotency-Key required; a replay returns the same booking). 409 booking.slot_unavailable when the time was taken.")
            .Produces<CustomerBookingResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        var mine = api.MapGroup("/me/bookings").WithTags("Customer: bookings");
        mine.MapGet("/", async (BookingsTab? tab, int? page, int? pageSize, IDispatcher d, CancellationToken ct) =>
                TypedResults.Ok(await d.Send(new ListMyBookingsQuery(tab ?? BookingsTab.Upcoming, new PageRequest(page, pageSize)), ct)))
            .RequireUserType(UserTypes.Customer)
            .WithName("ListMyBookings").WithSummary("The customer's own bookings: upcoming (soonest first) or past (latest first).")
            .Produces<PagedResponse<CustomerBookingResponse>>();
        mine.MapGet("/{bookingId:guid}", async (Guid bookingId, IDispatcher d, CancellationToken ct) =>
                await d.Send(new GetMyBookingQuery(bookingId), ct) is { } booking ? TypedResults.Ok(booking) : BookingErrors.NotFound().ToProblem())
            .RequireUserType(UserTypes.Customer)
            .WithName("GetMyBooking").WithSummary("One of the customer's own bookings with the actions still allowed; another customer's id is 404.")
            .Produces<CustomerBookingResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        mine.MapPost("/{bookingId:guid}/cancel", async (Guid bookingId, CancelBookingRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new CancelMyBookingCommand(bookingId, r.Reason, r.Version), ct)).ToHttpResult())
            .RequireUserType(UserTypes.Customer)
            .WithName("CancelMyBooking").WithSummary("Cancels the customer's booking until the cancellation cutoff (D-015).")
            .Produces<CustomerBookingResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        mine.MapPost("/{bookingId:guid}/reschedule", async (Guid bookingId, [FromHeader(Name = IdempotencyHeader)] string? key, RescheduleBookingRequest r, IDispatcher d, HttpContext http, CancellationToken ct) =>
                KeyError(key) is { } missing
                    ? missing.ToProblem()
                    : Ok(http, await d.Send(new RescheduleMyBookingCommand(bookingId, r.StartsAt, r.ProfessionalId, r.Version, key!), ct)))
            .RequireUserType(UserTypes.Customer).RequireRateLimiting(RateLimitPolicies.Booking)
            .WithName("RescheduleMyBooking").WithSummary("Moves the booking to another offered slot until the cutoff (Idempotency-Key required).")
            .Produces<CustomerBookingResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        mine.MapGet("/{bookingId:guid}/reschedule/dates", async (Guid bookingId, DateOnly? from, DateOnly? to, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new GetRescheduleDatesQuery(bookingId, from, to), ct)).ToHttpResult())
            .RequireUserType(UserTypes.Customer).RequireRateLimiting(RateLimitPolicies.Availability)
            .WithName("GetMyBookingRescheduleDates")
            .WithSummary("Dates the booking can move to (same professional and duration) with their free starts; the booking's own time does not block it.")
            .Produces<RescheduleDatesResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        mine.MapGet("/{bookingId:guid}/reschedule/slots", async (Guid bookingId, DateOnly date, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new GetRescheduleSlotsQuery(bookingId, date), ct)).ToHttpResult())
            .RequireUserType(UserTypes.Customer).RequireRateLimiting(RateLimitPolicies.Availability)
            .WithName("GetMyBookingRescheduleSlots").WithSummary("The free starts on one date the booking can move to.")
            .Produces<RescheduleSlotsResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        mine.MapGet("/{bookingId:guid}/calendar.ics", async (Guid bookingId, IDispatcher d, CancellationToken ct) =>
                await d.Send(new GetMyBookingCalendarQuery(bookingId), ct) is { } calendar
                    ? Results.Text(calendar, "text/calendar; charset=utf-8")
                    : BookingErrors.NotFound().ToProblem())
            .RequireUserType(UserTypes.Customer)
            .WithName("GetMyBookingCalendar").WithSummary("The booking as an iCalendar event (add to calendar).")
            .Produces<string>(StatusCodes.Status200OK, "text/calendar").ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapShop(RouteGroupBuilder group)
    {
        group.MapGet("/", async (DateOnly? from, DateOnly? to, [FromQuery] BookingStatus[]? status, Guid? professionalId, string? search, int? page, int? pageSize, IDispatcher d, CancellationToken ct) =>
                await d.Send(new ListShopBookingsQuery(from, to, status, professionalId, search, new PageRequest(page, pageSize)), ct) is { } list
                    ? TypedResults.Ok(list)
                    : BookingErrors.NotFound().ToProblem())
            .RequirePermission(ShopRead)
            .WithName("ListShopBookings")
            .WithSummary("The shop's bookings by local date range, any of the given statuses and professional; search by customer name or reference only. Counts per status chip.")
            .Produces<ShopBookingListResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/{bookingId:guid}", async (Guid bookingId, IDispatcher d, CancellationToken ct) =>
                await d.Send(new GetShopBookingQuery(bookingId), ct) is { } booking ? TypedResults.Ok(booking) : BookingErrors.NotFound().ToProblem())
            .RequirePermission(ShopRead)
            .WithName("GetShopBooking").WithSummary("One of the shop's bookings with its history and internal notes; never the customer's phone.")
            .Produces<ShopBookingDetailResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/walk-in", async ([FromHeader(Name = IdempotencyHeader)] string? key, WalkInRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new CreateWalkInCommand(r.ServiceId, r.PackageId, r.ProfessionalId, r.StartsAt, r.CustomerName ?? string.Empty, r.Note, string.IsNullOrWhiteSpace(key) ? null : key), ct))
                    .ToHttpResult(b => TypedResults.Created($"/api/v1/shop/bookings/{b.Id}", b)))
            .RequirePermission(ShopWalkIn)
            .WithName("CreateWalkIn").WithSummary("Records a walk-in with the same collision checks as online bookings (R-BKG-07).")
            .Produces<ShopBookingResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        group.MapPost("/{bookingId:guid}/transitions", async (Guid bookingId, BookingTransitionRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new TransitionShopBookingCommand(bookingId, r.To, r.Reason, r.Version), ct)).ToHttpResult())
            .RequirePermission(ShopUpdateStatus)
            .WithName("TransitionShopBooking").WithSummary("Moves the booking along the state machine (D-016); invalid transitions answer 409.")
            .Produces<ShopBookingResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        group.MapPost("/{bookingId:guid}/notes", async (Guid bookingId, BookingNoteRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new AddBookingNoteCommand(bookingId, r.Text ?? string.Empty), ct)).ToHttpResult(n => TypedResults.Created($"/api/v1/shop/bookings/{bookingId}", n)))
            .RequirePermission(ShopUpdateStatus)
            .WithName("AddBookingNote").WithSummary("Adds an internal note (never shown to the customer).")
            .Produces<BookingNoteResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapBoard(IEndpointRouteBuilder api)
    {
        api.MapGet("/shop/dashboard/overview", async (DateOnly? date, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new GetShopOverviewQuery(date), ct)).ToHttpResult())
            .RequirePermission(ShopRead).WithTags("Shop: bookings")
            .WithName("GetShopOverview")
            .WithSummary("The business day's KPIs, next bookings, each professional's load and the last seven days by hour (s-overview).")
            .Produces<ShopOverviewResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        api.MapGet("/shop/calendar", async (DateOnly from, DateOnly? to, Guid? professionalId, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new GetShopCalendarQuery(from, to, professionalId), ct)).ToHttpResult())
            .RequirePermission(ShopRead).WithTags("Shop: bookings")
            .WithName("GetShopCalendar")
            .WithSummary("One to seven business days: each professional's working time, breaks and time off, and the bookings (s-calendar).")
            .Produces<ShopCalendarResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapAdmin(RouteGroupBuilder group)
    {
        group.MapGet("/", async (Guid? shopId, [FromQuery] BookingStatus[]? status, DateTimeOffset? from, DateTimeOffset? to, BookingChannel? channel, Guid? customerId,
                    Guid? professionalId, string? search, string? sort, int? page, int? pageSize, IDispatcher d, CancellationToken ct) =>
                TypedResults.Ok(await d.Send(new ListAdminBookingsQuery(
                    shopId, status, from, to, channel, customerId, professionalId, search, !string.Equals(sort, "asc", StringComparison.OrdinalIgnoreCase),
                    new PageRequest(page, pageSize)), ct)))
            .RequirePermission(AdminView)
            .WithName("AdminListBookings")
            .WithSummary("Bookings across shops (paged; latest first unless sort=asc): shop, any of several statuses, time range, channel, customer, professional; search by customer name or reference only. Status chip counts.")
            .Produces<AdminBookingListResponse>();
        group.MapGet("/{bookingId:guid}", async (Guid bookingId, IDispatcher d, CancellationToken ct) =>
                await d.Send(new GetAdminBookingQuery(bookingId), ct) is { } booking ? TypedResults.Ok(booking) : BookingErrors.NotFound().ToProblem())
            .RequirePermission(AdminView)
            .WithName("AdminGetBooking").WithSummary("One booking with its history and the shop's internal notes (read-only).")
            .Produces<AdminBookingDetailResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/{bookingId:guid}/transitions", async (Guid bookingId, BookingTransitionRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new AdminTransitionBookingCommand(bookingId, r.To, r.Reason, r.Version), ct)).ToHttpResult())
            .RequirePermission(AdminIntervene)
            .WithName("AdminTransitionBooking")
            .WithSummary("Moves the booking along the state machine on the shop's behalf; a reason is required (audited). Invalid transitions answer 409.")
            .Produces<AdminBookingResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        group.MapPost("/{bookingId:guid}/cancel", async (Guid bookingId, AdminCancelBookingRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new AdminTransitionBookingCommand(bookingId, BookingStatus.CancelledByShop, r.Reason, r.Version), ct)).ToHttpResult())
            .RequirePermission(AdminIntervene)
            .WithName("AdminCancelBooking").WithSummary("Cancels on the shop's behalf with a reason (audited); the same as a transition to CancelledByShop.")
            .Produces<AdminBookingResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapGet("/{bookingId:guid}/reschedule/options", async (Guid bookingId, DateOnly? date, Guid? professionalId, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new GetAdminRescheduleOptionsQuery(bookingId, date, professionalId), ct)).ToHttpResult())
            .RequirePermission(AdminIntervene)
            .WithName("AdminGetRescheduleOptions")
            .WithSummary("The professionals a booking may move to and the free starts on one date for its duration (its own time does not block it).")
            .Produces<AdminRescheduleOptionsResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        group.MapPost("/{bookingId:guid}/reschedule", async (Guid bookingId, [FromHeader(Name = IdempotencyHeader)] string? key, AdminRescheduleBookingRequest r, IDispatcher d, HttpContext http, CancellationToken ct) =>
            {
                if (KeyError(key) is { } missing)
                {
                    return missing.ToProblem();
                }

                var result = await d.Send(new AdminRescheduleBookingCommand(bookingId, r.StartsAt, r.ProfessionalId, r.Reason, r.Version, key!), ct);
                if (result.IsFailure)
                {
                    return result.Error.ToProblem();
                }

                if (result.Value.Replayed)
                {
                    http.Response.Headers[ReplayedHeader] = "true";
                }

                return TypedResults.Ok(result.Value.Booking);
            })
            .RequirePermission(AdminIntervene)
            .WithName("AdminRescheduleBooking")
            .WithSummary("Moves a pending or confirmed booking to a free start (the desk's collision rules, not in the past) with a reason; Idempotency-Key required; audited. 409 booking.slot_unavailable when the time was taken.")
            .Produces<AdminBookingResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static Error? KeyError(string? key) =>
        string.IsNullOrWhiteSpace(key) || key.Length > IdempotencyRecord.MaxKeyLength ? BookingErrors.IdempotencyKeyRequired() : null;

    private static IResult Created(HttpContext http, Result<CustomerBookingResult> result)
    {
        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        if (result.Value.Replayed)
        {
            http.Response.Headers[ReplayedHeader] = "true";
        }

        return TypedResults.Created($"/api/v1/me/bookings/{result.Value.Booking.Id}", result.Value.Booking);
    }

    private static IResult Ok(HttpContext http, Result<CustomerBookingResult> result)
    {
        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        if (result.Value.Replayed)
        {
            http.Response.Headers[ReplayedHeader] = "true";
        }

        return TypedResults.Ok(result.Value.Booking);
    }
}
