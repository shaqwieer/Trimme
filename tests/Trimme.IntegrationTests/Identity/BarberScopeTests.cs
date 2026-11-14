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
/// design; a new endpoint a barber can reach fails until it is classified.
/// </summary>
public sealed class BarberScopeTests(PostgresFixture postgres)
{
    private const string Scoped = "scoped to the barber";

    /// <summary>The reviewed list: every endpoint a ShopBarber login can call, and why that is safe.</summary>
    private static readonly Dictionary<string, string> BarberReachable = new(StringComparer.Ordinal)
    {
        // Scoped: a colleague's booking answers 404, lists and boards hold only the barber's own (tested below).
        ["GET /api/v1/shop/bookings/"] = Scoped,
        ["GET /api/v1/shop/bookings/{bookingId:guid}"] = Scoped,
        ["POST /api/v1/shop/bookings/{bookingId:guid}/transitions"] = Scoped,
        ["POST /api/v1/shop/bookings/{bookingId:guid}/notes"] = Scoped,
        ["POST /api/v1/shop/bookings/walk-in"] = Scoped,
        ["GET /api/v1/shop/bookings/{bookingId:guid}/reschedule/dates"] = Scoped,
        ["GET /api/v1/shop/bookings/{bookingId:guid}/reschedule/slots"] = Scoped,
        ["POST /api/v1/shop/bookings/{bookingId:guid}/reschedule"] = Scoped,
        ["GET /api/v1/shop/bookings/{bookingId:guid}/professional-options"] = Scoped,
        ["POST /api/v1/shop/bookings/{bookingId:guid}/professional"] = Scoped,
        ["GET /api/v1/shop/dashboard/overview"] = Scoped,
        ["GET /api/v1/shop/calendar"] = Scoped,
        ["GET /api/v1/shop/availability/walk-in"] = Scoped,
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
        ["GET /api/v1/shop/loyalty"] = "the loyalty programme's terms and totals, no customer",

        // The account's own.
        ["GET /api/v1/me"] = "self",
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
        var omars = await OkAsync(BookAsync(customer, w.SlugA, w.Haircut, w.Omar, At(day, 11), ct), ct, HttpStatusCode.Created);
        var theirs = omars.GetProperty("id").GetGuid();
        var iso = Iso(day);

        // Lists, even when asking for a colleague: only the barber's own.
        static IEnumerable<Guid> Ids(JsonElement list) => list.GetProperty("items").EnumerateArray().Select(b => b.GetProperty("id").GetGuid());
        Ids(await OkAsync(faisal.GetAsync($"/api/v1/shop/bookings?from={iso}&to={iso}", ct), ct)).ShouldBe([mine]);
        Ids(await OkAsync(faisal.GetAsync($"/api/v1/shop/bookings?from={iso}&to={iso}&professionalId={w.Omar}", ct), ct)).ShouldBeEmpty();
        Ids(await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/bookings?from={iso}&to={iso}", ct), ct)).ShouldBe([mine, theirs], ignoreOrder: true);

        // A colleague's booking is not found, to read or to change.
        await FailsAsync(faisal.GetAsync($"/api/v1/shop/bookings/{theirs}", ct), HttpStatusCode.NotFound, null, ct);
        await FailsAsync(faisal.PostAsync($"/api/v1/shop/bookings/{theirs}/transitions", new { to = "CancelledByShop", reason = "busy", version = omars.GetProperty("version").GetUInt32() }, ct),
            HttpStatusCode.NotFound, null, ct);
        await FailsAsync(faisal.PostAsync($"/api/v1/shop/bookings/{theirs}/notes", new { text = "x" }, ct), HttpStatusCode.NotFound, null, ct);
        await OkAsync(faisal.GetAsync($"/api/v1/shop/bookings/{mine}", ct), ct);
        await OkAsync(faisal.PostAsync($"/api/v1/shop/bookings/{mine}/notes", new { text = "يحب التدريج" }, ct), ct, HttpStatusCode.Created);

        // The calendar and the overview hold the barber's lane only.
        var calendar = await OkAsync(faisal.GetAsync($"/api/v1/shop/calendar?from={iso}&professionalId={w.Omar}", ct), ct);
        calendar.GetProperty("professionals").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ShouldBe([w.Faisal]);
        calendar.GetProperty("days")[0].GetProperty("bookings").EnumerateArray().Select(b => b.GetProperty("id").GetGuid()).ShouldBe([mine]);
        var overview = await OkAsync(faisal.GetAsync($"/api/v1/shop/dashboard/overview?date={iso}", ct), ct);
        overview.GetProperty("professionals").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ShouldBe([w.Faisal]);
        overview.GetProperty("kpis").GetProperty("total").GetInt32().ShouldBe(1);
        overview.GetProperty("customers").ValueKind.ShouldBe(JsonValueKind.Null);
        (await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/dashboard/overview?date={iso}", ct), ct)).GetProperty("kpis").GetProperty("total").GetInt32().ShouldBe(2);

        // Walk-ins are for the barber themselves.
        var options = await OkAsync(faisal.GetAsync($"/api/v1/shop/availability/walk-in?serviceId={w.Haircut}&date={iso}", ct), ct);
        options.GetProperty("professionals").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ShouldBe([w.Faisal]);
        await FailsAsync(
            faisal.SendAsync(HttpMethod.Post, "/api/v1/shop/bookings/walk-in",
                new { serviceId = w.Haircut, professionalId = w.Omar, startsAt = At(day, 15), customerName = "زائر" }, ct, headers: Key()),
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
}
