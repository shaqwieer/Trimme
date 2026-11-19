using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Trimme.BuildingBlocks.Web.Security;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.Modules.Identity.Domain;
using static Trimme.IntegrationTests.Bookings.BookingTestData;

namespace Trimme.IntegrationTests.Identity;

/// <summary>
/// D-145: a barber's login reaches only their own professional's bookings, breaks and figures. Every endpoint a barber can
/// reach is reviewed here, as scoped to the barber (checked below with a colleague's booking and day) or shop-wide by
/// design; a new endpoint a barber can reach fails until it is classified. D-147 adds reviewed exceptions: the shop's red
/// bookings (any barber closes them) and a walk-in or phone booking made for a free colleague; its pending-booking
/// exception went with the Pending status (D-155). A barber's overview carries no figures. D-151 adds a read-only one: a
/// barber lists a colleague's or the whole shop's bookings and opens any of them, without acting on them beyond the red
/// exception and never with a colleague's customer's mobile. D-155 adds a fifth: today's open bookings of a barber marked
/// «غير متوفر اليوم» may be given to a colleague, given a new time or cancelled by any barber of the shop, and nothing else.
/// The pause is the owner's again (D-155).
/// </summary>
public sealed class BarberScopeTests(PostgresFixture postgres)
{
    private const string Scoped = "scoped to the barber";

    private const string Red = "scoped to the barber, plus the shop's red bookings (D-147's exception 2)";

    private const string Covered = "scoped to the barber, plus today's open bookings of a barber marked «غير متوفر اليوم» (D-155's exception 5)";

    private const string ReadOnlyShop =
        "any of the shop's bookings, read-only beyond the barber's own and D-147's exceptions; a colleague's customer's mobile never (D-151 exception 4)";

    /// <summary>The reviewed list: every endpoint a ShopBarber login can call, and why that is safe.</summary>
    private static readonly Dictionary<string, string> BarberReachable = new(StringComparer.Ordinal)
    {
        // Scoped: a colleague's booking answers 404 to every change; boards hold only the barber's own (tested below).
        ["GET /api/v1/shop/bookings/"] = "the barber's own by default; a colleague's or the whole shop's when asked. " + ReadOnlyShop,
        ["GET /api/v1/shop/bookings/{bookingId:guid}"] = ReadOnlyShop,
        ["GET /api/v1/shop/bookings/{bookingId:guid}/services/candidates"] = Scoped,
        ["POST /api/v1/shop/bookings/{bookingId:guid}/transitions"] =
            Red + ": on a colleague's, only close it once red; or cancel it while its barber is «غير متوفر اليوم» (D-155's exception 5)",
        ["POST /api/v1/shop/bookings/{bookingId:guid}/notes"] = Scoped,
        ["POST /api/v1/shop/bookings/{bookingId:guid}/services"] = Scoped,
        ["POST /api/v1/shop/bookings/{bookingId:guid}/services/{serviceId:guid}/delete"] = Scoped,
        ["POST /api/v1/shop/bookings/walk-in"] = "the barber, or a free colleague of the same shop (D-147 exception 3)",
        ["GET /api/v1/shop/bookings/{bookingId:guid}/reschedule/dates"] = Covered,
        ["GET /api/v1/shop/bookings/{bookingId:guid}/reschedule/slots"] = Covered,
        ["POST /api/v1/shop/bookings/{bookingId:guid}/reschedule"] = Covered,
        ["GET /api/v1/shop/bookings/{bookingId:guid}/professional-options"] = Covered,
        ["POST /api/v1/shop/bookings/{bookingId:guid}/professional"] = Covered,
        ["GET /api/v1/shop/dashboard/overview"] = Scoped + ", with no figures (D-147)",
        ["GET /api/v1/shop/calendar"] = Scoped,
        ["GET /api/v1/shop/availability/walk-in"] = "the shop's barbers who do it, to book a free colleague (D-147 exception 3); no customer data",
        ["POST /api/v1/shop/loyalty/lookup"] = "whether the number typed for a walk-in or phone booking has a loyalty reward ready, and what it is; nothing else (D-149)",

        // Shop-wide by design (D-147): what any barber of the shop may act on. The pause is the owner's (D-155).
        ["GET /api/v1/shop/bookings/attention"] =
            "the shop's red bookings, any barber's (D-147's exception 2), and today's of a barber marked «غير متوفر اليوم» (D-155's exception 5)",
        ["GET /api/v1/shop/schedule/unavailable-today"] = "who is «غير متوفر اليوم», since when, and how many bookings they have left today; no customer (D-155)",
        ["POST /api/v1/shop/schedule/unavailable-today"] = "the barber marks only themselves «غير متوفر اليوم» (D-155); a colleague's id is ignored",
        ["DELETE /api/v1/shop/schedule/unavailable-today"] = "the barber takes back only their own «غير متوفر اليوم» (D-155)",
        ["GET /api/v1/shop/schedule/barber-day/"] = Scoped,
        ["PUT /api/v1/shop/schedule/barber-day/"] = Scoped,

        // Shop-wide by design: the shop's own profile, catalogue and team (names, no contact data), and its loyalty terms.
        ["GET /api/v1/shop/me"] = "the shop's name and status",
        ["GET /api/v1/shop/profile"] = "the shop's public profile",
        ["GET /api/v1/shop/online-booking"] = "whether online booking is paused",
        ["GET /api/v1/shop/professionals"] = "the colleagues' names, to move a booking to one of them",
        ["GET /api/v1/shop/services/"] = "the shop's services",
        ["GET /api/v1/shop/services/{serviceId:guid}"] = "one of the shop's services",
        ["GET /api/v1/shop/packages/"] = "the shop's packages",
        ["GET /api/v1/shop/packages/{packageId:guid}"] = "one of the shop's packages",
        ["GET /api/v1/shop/service-sections/"] = "the shop's sections of its services, names and order (D-149)",
        ["GET /api/v1/shop/booking-settings"] = "the shop's booking rules in force (D-149); only the owner changes them",
        ["GET /api/v1/shop/loyalty"] = "the loyalty programme's terms and totals, no customer",

        // The account's own.
        ["GET /api/v1/me"] = "self",
        ["GET /api/v1/shop/account/"] = "self: the login's names and masked number (D-151)",
        ["PUT /api/v1/shop/account/name"] = "self: the barber's own professional's name (D-151)",
        ["DELETE /api/v1/shop/account/"] = "self: the barber's own login (D-151)",
        ["GET /api/v1/auth/sessions"] = "self",
        ["DELETE /api/v1/auth/sessions/{sessionId:guid}"] = "self",
        ["POST /api/v1/auth/sessions/revoke-all"] = "self",
        ["GET /api/v1/me/notifications"] = "self",
        ["GET /api/v1/me/notifications/unread-count"] = "self",
        ["POST /api/v1/me/notifications/{notificationId:guid}/read"] = "self",
        ["POST /api/v1/me/notifications/read-all"] = "self",
    };

    [Fact]
    public async Task EveryEndpointABarberCanReach_IsReviewed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "barber_matrix", ct);
        var granted = SystemRoles.Find(SystemRoles.ShopBarber)!.DefaultPermissions.ToHashSet(StringComparer.Ordinal);

        var reachable = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(s => s.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(e => ("/" + e.RoutePattern.RawText?.TrimStart('/')).StartsWith("/api/v1/", StringComparison.Ordinal))
            .Where(e => e.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>() is null)
            .Where(e =>
            {
                var permission = e.Metadata.GetMetadata<RequiredPermissionMetadata>()?.Permission;
                var userType = e.Metadata.GetMetadata<RequiredUserTypeMetadata>()?.UserType;
                return permission is not null ? granted.Contains(permission) : userType is null or UserTypes.ShopUser;
            })
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(method => $"{method} /{e.RoutePattern.RawText!.TrimStart('/')}"))
            .Order(StringComparer.Ordinal)
            .ToList();

        reachable.ShouldBe([.. BarberReachable.Keys.Order(StringComparer.Ordinal)], "a barber can reach an endpoint that is not reviewed here (or one listed here is gone)");
    }

    [Fact]
    public async Task ABarber_SeesAndChangesOnlyTheirOwnBookingsAndDay_TheOwnerSeesEveryone()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "barber_scope", ct);
        using var faisal = await BarberLoginTests.BarberAsync(w, w.Faisal, "+966512340011", ct);
        using var customer = await CustomerAsync(w.Factory, "نورة", ct);
        var day = TodayAt(DateTimeOffset.UtcNow).AddDays(2);
        var mine = (await OkAsync(BookAsync(customer, w.SlugA, w.Haircut, w.Faisal, At(day, 10), ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        using var other = await CustomerAsync(w.Factory, "سارة", ct);
        var omars = await OkAsync(BookAsync(other, w.SlugA, w.Haircut, w.Omar, At(day, 11), ct), ct, HttpStatusCode.Created);
        var theirs = omars.GetProperty("id").GetGuid();
        var iso = Iso(day);

        // Lists: the barber's own by default; a colleague's or the whole shop's when asked (D-151, «جدولي / الكل / اسم
        // الحلاق»), read-only: nothing to do on a colleague's booking that is neither pending nor red.
        static IEnumerable<Guid> Ids(JsonElement list) => list.GetProperty("items").EnumerateArray().Select(b => b.GetProperty("id").GetGuid());
        Ids(await OkAsync(faisal.GetAsync($"/api/v1/shop/bookings?from={iso}&to={iso}", ct), ct)).ShouldBe([mine]);
        var colleague = await OkAsync(faisal.GetAsync($"/api/v1/shop/bookings?from={iso}&to={iso}&professionalId={w.Omar}", ct), ct);
        Ids(colleague).ShouldBe([theirs]);
        colleague.GetProperty("items")[0].GetProperty("allowedTransitions").GetArrayLength().ShouldBe(0);
        Ids(await OkAsync(faisal.GetAsync($"/api/v1/shop/bookings?from={iso}&to={iso}&all=true", ct), ct)).ShouldBe([mine, theirs], ignoreOrder: true);
        Ids(await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/bookings?from={iso}&to={iso}", ct), ct)).ShouldBe([mine, theirs], ignoreOrder: true);

        // A colleague's booking opens read-only, and is not found to change.
        var opened = await OkAsync(faisal.GetAsync($"/api/v1/shop/bookings/{theirs}", ct), ct);
        opened.GetProperty("booking").GetProperty("allowedTransitions").GetArrayLength().ShouldBe(0);
        await FailsAsync(faisal.PostAsync($"/api/v1/shop/bookings/{theirs}/transitions", new { to = "CancelledByShop", reason = "busy", version = omars.GetProperty("version").GetUInt32() }, ct),
            HttpStatusCode.NotFound, null, ct);
        await FailsAsync(faisal.PostAsync($"/api/v1/shop/bookings/{theirs}/notes", new { text = "x" }, ct), HttpStatusCode.NotFound, null, ct);
        await FailsAsync(faisal.GetAsync($"/api/v1/shop/bookings/{theirs}/services/candidates", ct), HttpStatusCode.NotFound, null, ct);
        await OkAsync(faisal.GetAsync($"/api/v1/shop/bookings/{mine}", ct), ct);
        await OkAsync(faisal.PostAsync($"/api/v1/shop/bookings/{mine}/notes", new { text = "يحب التدريج" }, ct), ct, HttpStatusCode.Created);

        // The calendar and the overview hold the barber's lane only.
        var calendar = await OkAsync(faisal.GetAsync($"/api/v1/shop/calendar?from={iso}&professionalId={w.Omar}", ct), ct);
        calendar.GetProperty("professionals").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ShouldBe([w.Faisal]);
        calendar.GetProperty("days")[0].GetProperty("bookings").EnumerateArray().Select(b => b.GetProperty("id").GetGuid()).ShouldBe([mine]);
        // The barber's overview has what comes next, and no figures: no counts, no load, no hours (D-147).
        var overview = await OkAsync(faisal.GetAsync($"/api/v1/shop/dashboard/overview?date={iso}", ct), ct);
        overview.GetProperty("kpis").ValueKind.ShouldBe(JsonValueKind.Null);
        overview.GetProperty("professionals").GetArrayLength().ShouldBe(0);
        overview.GetProperty("hourly").GetArrayLength().ShouldBe(0);
        overview.GetProperty("customers").ValueKind.ShouldBe(JsonValueKind.Null);
        (await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/dashboard/overview?date={iso}", ct), ct)).GetProperty("kpis").GetProperty("total").GetInt32().ShouldBe(2);

        // Walk-ins: for the barber, or a free colleague of the same shop (D-147), never another shop's barber.
        var options = await OkAsync(faisal.GetAsync($"/api/v1/shop/availability/walk-in?serviceId={w.Haircut}&date={iso}", ct), ct);
        options.GetProperty("professionals").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ShouldBe([w.Faisal, w.Omar]);
        var forOmar = await OkAsync(
            faisal.SendAsync(HttpMethod.Post, "/api/v1/shop/bookings/walk-in",
                new { serviceId = w.Haircut, professionalId = w.Omar, startsAt = At(day, 15), customerName = "زائر" }, ct, headers: Key()),
            ct, HttpStatusCode.Created);
        forOmar.GetProperty("professional").GetProperty("id").GetGuid().ShouldBe(w.Omar);

        // A phone booking for Omar with the customer's mobile: Omar reads it, Faisal (a colleague) opens it without it
        // (D-151), the owner never (D-147).
        var withNumber = await OkAsync(
            faisal.SendAsync(HttpMethod.Post, "/api/v1/shop/bookings/walk-in",
                new { serviceId = w.Haircut, professionalId = w.Omar, startsAt = At(day, 16), customerName = "متصل", customerPhone = "0555123456", channel = "Phone" },
                ct, headers: Key()),
            ct, HttpStatusCode.Created);
        var numbered = withNumber.GetProperty("id").GetGuid();
        static string? Phone(JsonElement booking) =>
            booking.TryGetProperty("customerPhone", out var phone) && phone.ValueKind == JsonValueKind.String ? phone.GetString() : null;
        Phone(withNumber).ShouldBeNull("Faisal made it for a colleague");
        Phone((await OkAsync(faisal.GetAsync($"/api/v1/shop/bookings/{numbered}", ct), ct)).GetProperty("booking")).ShouldBeNull();
        (await OkAsync(faisal.GetAsync($"/api/v1/shop/bookings?from={iso}&to={iso}&all=true", ct), ct)).GetProperty("items").EnumerateArray()
            .Where(b => b.GetProperty("id").GetGuid() == numbered).Select(Phone).ShouldBe([null]);
        using var omar = await BarberLoginTests.BarberAsync(w, w.Omar, "+966512340012", ct);
        Phone((await OkAsync(omar.GetAsync($"/api/v1/shop/bookings/{numbered}", ct), ct)).GetProperty("booking")).ShouldBe("+966555123456");
        Phone((await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/bookings/{numbered}", ct), ct)).GetProperty("booking")).ShouldBeNull();
        await FailsAsync(
            faisal.SendAsync(HttpMethod.Post, "/api/v1/shop/bookings/walk-in",
                new { serviceId = w.Haircut, professionalId = w.ProB, startsAt = At(day, 15), customerName = "زائر" }, ct, headers: Key()),
            HttpStatusCode.NotFound, "professional.not_found", ct);
        await OkAsync(
            faisal.SendAsync(HttpMethod.Post, "/api/v1/shop/bookings/walk-in",
                new { serviceId = w.Haircut, professionalId = w.Faisal, startsAt = At(day, 15), customerName = "زائر" }, ct, headers: Key()),
            ct, HttpStatusCode.Created);

        // Breaks: the barber's own day only.
        await FailsAsync(faisal.GetAsync($"/api/v1/shop/schedule/barber-day?professionalId={w.Omar}&date={iso}", ct), HttpStatusCode.NotFound, null, ct);
        await FailsAsync(faisal.PutAsync("/api/v1/shop/schedule/barber-day", new { professionalId = w.Omar, date = iso, breaks = Array.Empty<object>() }, ct),
            HttpStatusCode.NotFound, null, ct);
        await OkAsync(faisal.GetAsync($"/api/v1/shop/schedule/barber-day?professionalId={w.Faisal}&date={iso}", ct), ct);

        // The shop's inbox is the owner's; the barber's notices are their own account's.
        await FailsAsync(faisal.GetAsync("/api/v1/shop/notifications", ct), HttpStatusCode.Forbidden, null, ct);
        await OkAsync(faisal.GetAsync("/api/v1/me/notifications", ct), ct);
        await OkAsync(w.OwnerA.GetAsync("/api/v1/shop/notifications", ct), ct);
    }

    [Fact]
    public async Task ABarberMarkedUnavailableToday_HasTheirBookingsCoveredByColleagues_AndNothingElse()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "barber_unavailable", ct);
        using var faisal = await BarberLoginTests.BarberAsync(w, w.Faisal, "+966512340021", ct);
        using var omar = await BarberLoginTests.BarberAsync(w, w.Omar, "+966512340022", ct);
        using var anonymous = ApiSession.Create(w.Factory);

        // Omar has a booking later today and one tomorrow (test data: seeded at any time of day).
        var start = DateTimeOffset.UtcNow.AddMinutes(2);
        var today = await SeedOnlineBookingAsync(w, w.Shops.A.ShopId, Guid.NewGuid(), "نورة", w.Haircut, w.Omar, start, ct);
        var tomorrow = await SeedOnlineBookingAsync(w, w.Shops.A.ShopId, Guid.NewGuid(), "سارة", w.Haircut, w.Omar, start.AddDays(1), ct);

        // Before: a colleague's booking is the colleague's (D-145).
        await FailsAsync(faisal.GetAsync($"/api/v1/shop/bookings/{today}/professional-options", ct), HttpStatusCode.NotFound, null, ct);

        // Omar marks himself «غير متوفر اليوم»: once; the owner and the colleagues are told.
        var marked = await OkAsync(omar.PostAsync("/api/v1/shop/schedule/unavailable-today", new { }, ct), ct);
        marked.GetProperty("professionalId").GetGuid().ShouldBe(w.Omar);
        marked.GetProperty("bookings").GetInt32().ShouldBe(1);
        await FailsAsync(omar.PostAsync("/api/v1/shop/schedule/unavailable-today", new { }, ct), HttpStatusCode.Conflict, "schedule.already_unavailable", ct);
        (await OkAsync(w.OwnerA.GetAsync("/api/v1/shop/notifications", ct), ct)).GetProperty("items").EnumerateArray()
            .Select(n => n.GetProperty("kind").GetString()).ShouldContain("professional.unavailable_today");
        (await OkAsync(faisal.GetAsync("/api/v1/me/notifications", ct), ct)).GetProperty("items").EnumerateArray()
            .Select(n => n.GetProperty("kind").GetString()).ShouldContain("professional.unavailable_bookings");
        (await OkAsync(w.OwnerA.GetAsync("/api/v1/shop/dashboard/overview", ct), ct)).GetProperty("unavailableToday").EnumerateArray()
            .Select(u => u.GetProperty("professionalId").GetGuid()).ShouldBe([w.Omar]);
        (await OkAsync(faisal.GetAsync("/api/v1/shop/schedule/unavailable-today", ct), ct)).EnumerateArray()
            .Select(u => u.GetProperty("professionalId").GetGuid()).ShouldBe([w.Omar]);

        // Customers: Omar is unavailable today on the public lists.
        (await OkAsync(anonymous.GetAsync($"/api/v1/public/shops/{w.SlugA}/professionals", ct), ct)).EnumerateArray()
            .Single(p => p.GetProperty("id").GetGuid() == w.Omar).GetProperty("unavailableToday").GetBoolean().ShouldBeTrue();
        (await OkAsync(anonymous.GetAsync($"/api/v1/public/shops/{w.SlugA}/status", ct), ct)).GetProperty("professionals").EnumerateArray()
            .Single(p => p.GetProperty("professionalId").GetGuid() == w.Omar).GetProperty("unavailableToday").GetBoolean().ShouldBeTrue();

        // Faisal covers today's: the attention list, another barber, a new time or a cancellation; nothing else.
        (await OkAsync(faisal.GetAsync("/api/v1/shop/bookings/attention", ct), ct)).GetProperty("unavailable").EnumerateArray()
            .Select(b => b.GetProperty("id").GetGuid()).ShouldBe([today]);
        var opened = (await OkAsync(faisal.GetAsync($"/api/v1/shop/bookings/{today}", ct), ct)).GetProperty("booking");
        opened.GetProperty("unavailableBarber").GetBoolean().ShouldBeTrue();
        opened.GetProperty("allowedTransitions").EnumerateArray().Select(t => t.GetString()).ShouldBe(["CancelledByShop"]);
        await OkAsync(faisal.GetAsync($"/api/v1/shop/bookings/{today}/professional-options", ct), ct);
        await OkAsync(faisal.GetAsync($"/api/v1/shop/bookings/{today}/reschedule/dates", ct), ct);
        await FailsAsync(faisal.PostAsync($"/api/v1/shop/bookings/{today}/notes", new { text = "x" }, ct), HttpStatusCode.NotFound, null, ct);
        await FailsAsync(faisal.GetAsync($"/api/v1/shop/bookings/{tomorrow}/professional-options", ct), HttpStatusCode.NotFound, null, ct);
        var version = opened.GetProperty("version").GetUInt32();
        await FailsAsync(faisal.PostAsync($"/api/v1/shop/bookings/{today}/transitions", new { to = "Arrived", version }, ct), HttpStatusCode.Conflict, "booking.invalid_transition", ct);
        var cancelled = await OkAsync(
            faisal.PostAsync($"/api/v1/shop/bookings/{today}/transitions", new { to = "CancelledByShop", reasonCode = "StaffUnwell", version }, ct), ct);
        cancelled.GetProperty("status").GetString().ShouldBe("CancelledByShop");

        // Undone the same day, by the barber or the owner (who names the barber); then again by the owner.
        await FailsAsync(w.OwnerA.PostAsync("/api/v1/shop/schedule/unavailable-today", new { }, ct), HttpStatusCode.BadRequest, null, ct);
        await OkAsync(w.OwnerA.DeleteAsync($"/api/v1/shop/schedule/unavailable-today?professionalId={w.Omar}", ct), ct, HttpStatusCode.NoContent);
        await FailsAsync(omar.DeleteAsync("/api/v1/shop/schedule/unavailable-today", ct), HttpStatusCode.Conflict, "schedule.not_unavailable", ct);
        await OkAsync(w.OwnerA.PostAsync("/api/v1/shop/schedule/unavailable-today", new { professionalId = w.Omar }, ct), ct);
        (await OkAsync(omar.GetAsync("/api/v1/me/notifications", ct), ct)).GetProperty("items").EnumerateArray()
            .Select(n => n.GetProperty("kind").GetString()).ShouldContain("professional.unavailable_today");
        await OkAsync(omar.DeleteAsync("/api/v1/shop/schedule/unavailable-today", ct), ct, HttpStatusCode.NoContent);
    }
}
