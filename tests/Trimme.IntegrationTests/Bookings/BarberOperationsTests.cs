using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Shouldly;
using Trimme.BuildingBlocks.Web.Jobs;
using Trimme.IntegrationTests.Identity;
using Trimme.IntegrationTests.Infrastructure;
using static Trimme.IntegrationTests.Bookings.BookingTestData;

namespace Trimme.IntegrationTests.Bookings;

/// <summary>
/// D-147: the owner watches and the barbers operate. «وصل» comes before «انتهت الخدمة» and records the actual times;
/// «ما حضر» from a quarter hour late; a late cancellation is marked. Any barber accepts or rejects the shop's pending
/// bookings and closes its red ones (closed late, with who closed it). Walk-in and phone bookings take several services and
/// may be for a free colleague; barbers read their mobile while they are open, the owner never. A booking's services are
/// added after «وصل» and removed until «انتهت الخدمة». Every history entry names who did it.
/// </summary>
public sealed class BarberOperationsTests(PostgresFixture postgres)
{
    private static DateOnly Target => TodayAt(DateTimeOffset.UtcNow).AddDays(2);

    private static Task<HttpResponseMessage> Transition(ApiSession session, Guid id, string to, uint version, CancellationToken ct, string? reason = null) =>
        session.PostAsync($"/api/v1/shop/bookings/{id}/transitions", new { to, reason, version }, ct);

    private static async Task<JsonElement> DetailAsync(ApiSession session, Guid id, CancellationToken ct) =>
        (await OkAsync(session.GetAsync($"/api/v1/shop/bookings/{id}", ct), ct)).GetProperty("booking");

    private static async Task<JsonElement> AttentionAsync(ApiSession session, CancellationToken ct) =>
        await OkAsync(session.GetAsync("/api/v1/shop/bookings/attention", ct), ct);

    private static IEnumerable<Guid> Ids(JsonElement list) => list.EnumerateArray().Select(b => b.GetProperty("id").GetGuid());

    private static IEnumerable<string?> Allowed(JsonElement booking) => booking.GetProperty("allowedTransitions").EnumerateArray().Select(t => t.GetString());

    private static async Task ProcessOutboxAsync(TrimmeApiFactory factory, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
        for (var round = 0; round < 10 && await processor.ProcessBatchAsync(ct) > 0; round++)
        {
        }
    }

    private static async Task<IReadOnlyList<string>> PublicSlotsAsync(BookingWorld w, Guid professionalId, DateOnly day, CancellationToken ct)
    {
        using var anonymous = ApiSession.Create(w.Factory);
        var slots = await OkAsync(anonymous.GetAsync(
            $"/api/v1/public/shops/{w.SlugA}/availability/slots?serviceId={w.Haircut}&professionalId={professionalId}&date={Iso(day)}", ct), ct);
        return [.. slots.GetProperty("slots").EnumerateArray().Select(s => s.GetProperty("localTime").GetString()!)];
    }

    private static async Task<JsonElement> BookedAsync(ApiSession customer, BookingWorld w, Guid serviceId, Guid professionalId, DateTimeOffset start, CancellationToken ct, Guid? packageId = null) =>
        await OkAsync(BookAsync(customer, w.SlugA, serviceId, professionalId, start, ct, packageId: packageId), ct, HttpStatusCode.Created);

    [Fact]
    public async Task TheOwner_WatchesEverything_AndChangesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "d147_owner", ct);
        using var customer = await CustomerAsync(w.Factory, "نورة", ct);
        var booking = await BookedAsync(customer, w, w.Haircut, w.Faisal, At(Target, 10), ct);
        var id = booking.GetProperty("id").GetGuid();
        var version = booking.GetProperty("version").GetUInt32();
        var iso = Iso(Target);

        // Every action on a booking, the barbers' breaks and the pause are the barbers' (D-147).
        foreach (var call in new Func<Task<HttpResponseMessage>>[]
        {
            () => Transition(w.OwnerA, id, "CancelledByShop", version, ct, "إغلاق طارئ"),
            () => w.OwnerA.PostAsync($"/api/v1/shop/bookings/{id}/notes", new { text = "x" }, ct),
            () => w.OwnerA.SendAsync(HttpMethod.Post, $"/api/v1/shop/bookings/{id}/reschedule", new { startsAt = At(Target, 12), version }, ct, headers: Key()),
            () => w.OwnerA.SendAsync(HttpMethod.Post, $"/api/v1/shop/bookings/{id}/professional", new { professionalId = w.Omar, version }, ct, headers: Key()),
            () => w.OwnerA.PostAsync($"/api/v1/shop/bookings/{id}/postpone", new { startsAt = At(Target, 12), version }, ct),
            () => w.OwnerA.PostAsync($"/api/v1/shop/bookings/{id}/services", new { serviceId = w.Beard, version }, ct),
            () => w.OwnerA.PostAsync($"/api/v1/shop/bookings/{id}/services/{w.Haircut}/delete", new { version }, ct),
            () => w.OwnerA.PutAsync("/api/v1/shop/schedule/barber-day", new { professionalId = w.Faisal, date = iso, breaks = Array.Empty<object>() }, ct),
            () => w.OwnerA.GetAsync($"/api/v1/shop/schedule/barber-day?professionalId={w.Faisal}&date={iso}", ct),
            () => w.OwnerA.PostAsync("/api/v1/shop/online-booking/pause", new { }, ct),
            () => w.OwnerA.PostAsync("/api/v1/shop/online-booking/resume", null, ct),
            () => w.OwnerA.SendAsync(HttpMethod.Post, "/api/v1/shop/bookings/walk-in", new { serviceId = w.Haircut, professionalId = w.Faisal, startsAt = At(Target, 15), customerName = "زائر" }, ct, headers: Key()),
        })
        {
            using var response = await call();
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{response.RequestMessage!.Method} {response.RequestMessage.RequestUri}");
        }

        // What the owner still sees: the bookings, their details (no actions offered by the API's rules apply to the
        // barbers), the shop's pending and red bookings, the calendar, and the overview with its figures.
        Ids((await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/bookings?from={iso}&to={iso}", ct), ct)).GetProperty("items")).ShouldBe([id]);
        (await DetailAsync(w.OwnerA, id, ct)).GetProperty("status").GetString().ShouldBe("Confirmed");
        await AttentionAsync(w.OwnerA, ct);
        (await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/calendar?from={iso}", ct), ct)).GetProperty("days")[0].GetProperty("bookings").GetArrayLength().ShouldBe(1);
        var overview = await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/dashboard/overview?date={iso}", ct), ct);
        overview.GetProperty("kpis").GetProperty("total").GetInt32().ShouldBe(1);
        overview.GetProperty("professionals").GetArrayLength().ShouldBe(2);
        (await OkAsync(w.OwnerA.GetAsync("/api/v1/shop/online-booking", ct), ct)).GetProperty("paused").GetBoolean().ShouldBeFalse();

        // The reports and expected income are the owner's; a barber sees no figures (D-147).
        await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/reports?from={iso}&to={iso}", ct), ct);
        using var faisal = await w.BarberAsync(w.Faisal, ct);
        await FailsAsync(faisal.GetAsync($"/api/v1/shop/reports?from={iso}&to={iso}", ct), HttpStatusCode.Forbidden, null, ct);

        // No figures for a barber anywhere (D-147): the loyalty terms, not their statistics.
        (await OkAsync(w.OwnerA.GetAsync("/api/v1/shop/loyalty", ct), ct)).GetProperty("stats").ValueKind.ShouldBe(JsonValueKind.Object);
        (await OkAsync(faisal.GetAsync("/api/v1/shop/loyalty", ct), ct)).GetProperty("stats").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task AnyBarber_AcceptsOrRejects_TheShopsPendingBookings_ButNotAColleaguesOrdinaryOne()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "d147_pending", ct);
        await using (var connection = new NpgsqlConnection(w.Factory.ConnectionString))
        {
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand("UPDATE shops.shops SET require_manual_confirmation = true WHERE id = @p", connection);
            command.Parameters.AddWithValue("p", w.Shops.A.ShopId);
            (await command.ExecuteNonQueryAsync(ct)).ShouldBe(1);
        }

        using var faisal = await w.BarberAsync(w.Faisal, ct);
        using var customer = await CustomerAsync(w.Factory, "نورة", ct);
        var first = await BookedAsync(customer, w, w.Haircut, w.Omar, At(Target, 10), ct);
        var second = await BookedAsync(customer, w, w.Haircut, w.Omar, At(Target, 11), ct);
        first.GetProperty("status").GetString().ShouldBe("Pending");
        var firstId = first.GetProperty("id").GetGuid();
        var secondId = second.GetProperty("id").GetGuid();

        // Omar's pending bookings are in every barber's attention list, and Faisal may accept or reject them.
        var attention = await AttentionAsync(faisal, ct);
        Ids(attention.GetProperty("pending")).ShouldBe([firstId, secondId]);
        attention.GetProperty("overdue").GetArrayLength().ShouldBe(0);
        Ids((await AttentionAsync(w.OwnerA, ct)).GetProperty("pending")).ShouldBe([firstId, secondId], "the owner sees them, read-only");
        var colleagues = await DetailAsync(faisal, firstId, ct);
        Allowed(colleagues).ShouldBe(["Confirmed", "CancelledByShop"]);

        var accepted = await OkAsync(Transition(faisal, firstId, "Confirmed", colleagues.GetProperty("version").GetUInt32(), ct), ct);
        accepted.GetProperty("status").GetString().ShouldBe("Confirmed");
        accepted.GetProperty("professional").GetProperty("id").GetGuid().ShouldBe(w.Omar, "accepting does not take it over");
        var rejected = await OkAsync(Transition(faisal, secondId, "CancelledByShop", second.GetProperty("version").GetUInt32(), ct, "لا يوجد وقت"), ct);
        rejected.GetProperty("status").GetString().ShouldBe("CancelledByShop");

        // The owner hears what Faisal did to Omar's bookings (D-145's «the owner hears what barbers did», D-147).
        await ProcessOutboxAsync(w.Factory, ct);
        var inbox = (await OkAsync(w.OwnerA.GetAsync("/api/v1/shop/notifications", ct), ct)).GetProperty("items").EnumerateArray()
            .Where(n => n.GetProperty("bookingId").ValueKind == JsonValueKind.String)
            .Select(n => (Booking: n.GetProperty("bookingId").GetGuid(), Kind: n.GetProperty("kind").GetString()))
            .ToList();
        inbox.ShouldContain((firstId, "booking.confirmed"));
        inbox.ShouldContain((secondId, "booking.cancelled"));

        // The history names Faisal, the customer's own entry names the customer.
        using (var omar = await w.BarberAsync(w.Omar, ct))
        {
            var history = (await OkAsync(omar.GetAsync($"/api/v1/shop/bookings/{firstId}", ct), ct)).GetProperty("history");
            history[0].GetProperty("actorType").GetString().ShouldBe("Customer");
            history[0].GetProperty("actorName").GetString().ShouldBe("نورة");
            history[history.GetArrayLength() - 1].GetProperty("actorName").GetString().ShouldBe("فيصل");
        }

        // Once accepted it is Omar's ordinary booking again: out of Faisal's reach.
        await FailsAsync(faisal.GetAsync($"/api/v1/shop/bookings/{firstId}", ct), HttpStatusCode.NotFound, null, ct);
        await FailsAsync(Transition(faisal, firstId, "CancelledByShop", accepted.GetProperty("version").GetUInt32(), ct, "تجربة"), HttpStatusCode.NotFound, null, ct);
        (await AttentionAsync(faisal, ct)).GetProperty("pending").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task RedBookings_AnyBarberClosesThem_LateAndNamed()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var w = await ArrangeAsync(postgres, "d147_red", ct, clock);
        await w.EnsureBarberLoginAsync(w.Faisal, ct);
        await w.EnsureBarberLoginAsync(w.Omar, ct);
        Guid id;
        using (var customer = await CustomerAsync(w.Factory, "نورة", ct))
        {
            id = (await BookedAsync(customer, w, w.Haircut, w.Omar, At(Target, 10), ct)).GetProperty("id").GetGuid();
        }

        // 10:40: the 30-minute visit ended ten minutes ago: not red yet, and still Omar's alone.
        clock.SetUtcNow(At(Target, 10, 40).ToUniversalTime());
        using (var faisal = await w.BarberAsync(w.Faisal, ct))
        {
            (await AttentionAsync(faisal, ct)).GetProperty("overdue").GetArrayLength().ShouldBe(0);
            await FailsAsync(faisal.GetAsync($"/api/v1/shop/bookings/{id}", ct), HttpStatusCode.NotFound, null, ct);
        }

        // 10:50: red, for every barber of the shop and the owner.
        clock.SetUtcNow(At(Target, 10, 50).ToUniversalTime());
        using var faisalLater = await w.BarberAsync(w.Faisal, ct);
        using var omarLater = await w.BarberAsync(w.Omar, ct);
        var red = (await AttentionAsync(faisalLater, ct)).GetProperty("overdue").EnumerateArray().ShouldHaveSingleItem();
        red.GetProperty("id").GetGuid().ShouldBe(id);
        red.GetProperty("overdue").GetBoolean().ShouldBeTrue();
        red.GetProperty("professional").GetProperty("id").GetGuid().ShouldBe(w.Omar, "it names the barber responsible");
        Ids((await AttentionAsync(omarLater, ct)).GetProperty("overdue")).ShouldBe([id]);
        var detail = await DetailAsync(faisalLater, id, ct);
        Allowed(detail).ShouldBe(["Completed", "NoShow", "CancelledByShop"], "a colleague closes it, nothing else");
        await FailsAsync(Transition(faisalLater, id, "Arrived", detail.GetProperty("version").GetUInt32(), ct), HttpStatusCode.Conflict, "booking.invalid_transition", ct);

        // Faisal closes it: «انتهت الخدمة» from Confirmed, closed late, with his name; no actual start, so no speed figure.
        var closed = await OkAsync(Transition(faisalLater, id, "Completed", detail.GetProperty("version").GetUInt32(), ct), ct);
        closed.GetProperty("status").GetString().ShouldBe("Completed");
        closed.GetProperty("closedLate").GetBoolean().ShouldBeTrue();
        closed.GetProperty("closedBy").GetString().ShouldBe("فيصل");
        closed.GetProperty("actualStartAt").ValueKind.ShouldBe(JsonValueKind.Null);
        closed.GetProperty("actualEndAt").GetDateTimeOffset().ShouldBe(At(Target, 10, 50));
        closed.GetProperty("overdue").GetBoolean().ShouldBeFalse();

        var after = await AttentionAsync(omarLater, ct);
        after.GetProperty("overdue").GetArrayLength().ShouldBe(0);
        var late = after.GetProperty("closedLate").EnumerateArray().ShouldHaveSingleItem();
        late.GetProperty("id").GetGuid().ShouldBe(id);
        late.GetProperty("closedBy").GetString().ShouldBe("فيصل");
        using (var owner = await IdentityTestData.SignInStaffAsync(w.Factory, w.Shops.A.OwnerEmail, ct))
        {
            Ids((await AttentionAsync(owner, ct)).GetProperty("closedLate")).ShouldBe([id], "the owner sees it too");
        }

        // Three days later it leaves the barbers' lists.
        clock.SetUtcNow(At(Target.AddDays(4), 10).ToUniversalTime());
        using var faisalWeekLater = await w.BarberAsync(w.Faisal, ct);
        (await AttentionAsync(faisalWeekLater, ct)).GetProperty("closedLate").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Visits_ArriveThenFinish_WithActualTimes_NoShowAQuarterHourLate_AndLateCancellations()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var w = await ArrangeAsync(postgres, "d147_visits", ct, clock);
        await w.EnsureBarberLoginAsync(w.Faisal, ct);
        JsonElement served, missed, lateCancel, earlyCancel;
        using (var customer = await CustomerAsync(w.Factory, "نورة", ct))
        {
            served = await BookedAsync(customer, w, w.Haircut, w.Faisal, At(Target, 10), ct);
            missed = await BookedAsync(customer, w, w.Haircut, w.Faisal, At(Target, 11), ct);
            lateCancel = await BookedAsync(customer, w, w.Haircut, w.Faisal, At(Target, 12), ct);
            earlyCancel = await BookedAsync(customer, w, w.Haircut, w.Faisal, At(Target, 14), ct);
        }

        // «انتهت الخدمة» needs «وصل» first; the actual start and end are what the barber pressed.
        clock.SetUtcNow(At(Target, 10, 2).ToUniversalTime());
        var servedId = served.GetProperty("id").GetGuid();
        uint servedVersion;
        using (var faisal = await w.BarberAsync(w.Faisal, ct))
        {
            var version = served.GetProperty("version").GetUInt32();
            await FailsAsync(Transition(faisal, servedId, "Completed", version, ct), HttpStatusCode.UnprocessableEntity, "booking.arrival_required", ct);
            var arrived = await OkAsync(Transition(faisal, servedId, "Arrived", version, ct), ct);
            arrived.GetProperty("actualStartAt").GetDateTimeOffset().ShouldBe(At(Target, 10, 2));
            Allowed(arrived).ShouldBe(["Completed"], "an arrived customer is never cancelled");
            servedVersion = arrived.GetProperty("version").GetUInt32();
        }

        clock.SetUtcNow(At(Target, 10, 20).ToUniversalTime());
        using (var faisal = await w.BarberAsync(w.Faisal, ct))
        {
            var finished = await OkAsync(Transition(faisal, servedId, "Completed", servedVersion, ct), ct);
            finished.GetProperty("actualStartAt").GetDateTimeOffset().ShouldBe(At(Target, 10, 2));
            finished.GetProperty("actualEndAt").GetDateTimeOffset().ShouldBe(At(Target, 10, 20));
            finished.GetProperty("endsAt").GetDateTimeOffset().ShouldBe(At(Target, 10, 20));
            finished.GetProperty("closedLate").GetBoolean().ShouldBeFalse();
            finished.GetProperty("closedBy").GetString().ShouldBe("فيصل");
        }

        // «ما حضر» from a quarter hour after the start.
        clock.SetUtcNow(At(Target, 11, 10).ToUniversalTime());
        using (var faisal = await w.BarberAsync(w.Faisal, ct))
        {
            var id = missed.GetProperty("id").GetGuid();
            Allowed(await DetailAsync(faisal, id, ct)).ShouldNotContain("NoShow");
            await FailsAsync(Transition(faisal, id, "NoShow", missed.GetProperty("version").GetUInt32(), ct), HttpStatusCode.UnprocessableEntity, "booking.too_early", ct);
            clock.SetUtcNow(At(Target, 11, 15).ToUniversalTime());
            Allowed(await DetailAsync(faisal, id, ct)).ShouldContain("NoShow");
            (await OkAsync(Transition(faisal, id, "NoShow", missed.GetProperty("version").GetUInt32(), ct), ct)).GetProperty("status").GetString().ShouldBe("NoShow");
        }

        // A cancellation in the last quarter hour is «إلغاء متأخر»; one earlier is not.
        clock.SetUtcNow(At(Target, 11, 50).ToUniversalTime());
        using (var faisal = await w.BarberAsync(w.Faisal, ct))
        {
            var late = await OkAsync(
                Transition(faisal, lateCancel.GetProperty("id").GetGuid(), "CancelledByShop", lateCancel.GetProperty("version").GetUInt32(), ct, "ظرف طارئ"), ct);
            late.GetProperty("cancelledLate").GetBoolean().ShouldBeTrue();
            var early = await OkAsync(
                Transition(faisal, earlyCancel.GetProperty("id").GetGuid(), "CancelledByShop", earlyCancel.GetProperty("version").GetUInt32(), ct, "ظرف طارئ"), ct);
            early.GetProperty("cancelledLate").GetBoolean().ShouldBeFalse();
        }
    }

    [Fact]
    public async Task WalkInAndPhone_SeveralServices_ForAFreeColleague_WithTheMobileForBarbersOnly_WhileOpen()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "d147_phone", ct);
        using var faisal = await w.BarberAsync(w.Faisal, ct);
        using var omar = await w.BarberAsync(w.Omar, ct);
        const string Mobile = "0551234567";
        const string E164 = "+966551234567";

        // A phone booking names its start; services and a package do not mix.
        await FailsAsync(faisal.SendAsync(HttpMethod.Post, "/api/v1/shop/bookings/walk-in",
                new { channel = "Phone", serviceIds = new[] { w.Haircut }, professionalId = w.Faisal, customerName = "متصل", customerPhone = Mobile }, ct, headers: Key()),
            HttpStatusCode.BadRequest, "startsAt", ct);
        await FailsAsync(faisal.SendAsync(HttpMethod.Post, "/api/v1/shop/bookings/walk-in",
                new { channel = "Phone", serviceIds = new[] { w.Haircut }, packageId = w.Package, professionalId = w.Faisal, startsAt = At(Target, 12), customerName = "متصل" }, ct, headers: Key()),
            HttpStatusCode.BadRequest, "serviceIds", ct);

        // Faisal takes a call for his free colleague Omar: two services, added up, Omar's booking.
        var phone = await OkAsync(faisal.SendAsync(HttpMethod.Post, "/api/v1/shop/bookings/walk-in",
                new { channel = "Phone", serviceIds = new[] { w.Haircut, w.Beard }, professionalId = w.Omar, startsAt = At(Target, 12), customerName = "متصل", customerPhone = Mobile }, ct,
                headers: Key()),
            ct, HttpStatusCode.Created);
        var id = phone.GetProperty("id").GetGuid();
        phone.GetProperty("channel").GetString().ShouldBe("Phone");
        phone.GetProperty("status").GetString().ShouldBe("Confirmed");
        phone.GetProperty("professional").GetProperty("id").GetGuid().ShouldBe(w.Omar);
        phone.GetProperty("item").GetProperty("price").GetDecimal().ShouldBe(120m);
        phone.GetProperty("item").GetProperty("durationMinutes").GetInt32().ShouldBe(50);
        phone.GetProperty("item").GetProperty("nameAr").GetString().ShouldBe("حلاقة + لحية");
        phone.GetProperty("endsAt").GetDateTimeOffset().ShouldBe(At(Target, 12, 50));
        phone.GetProperty("customerPhone").GetString().ShouldBe(E164, "the barber who typed it");
        (await DetailAsync(omar, id, ct)).GetProperty("actualStartAt").ValueKind.ShouldBe(JsonValueKind.Null);

        // A walk-in for later keeps its channel; a walk-in starting now is under way at once.
        var later = await OkAsync(faisal.SendAsync(HttpMethod.Post, "/api/v1/shop/bookings/walk-in",
                new { serviceId = w.Haircut, professionalId = w.Faisal, startsAt = At(Target, 15), customerName = "زائر" }, ct, headers: Key()),
            ct, HttpStatusCode.Created);
        later.GetProperty("channel").GetString().ShouldBe("WalkIn");
        later.GetProperty("customerPhone").ValueKind.ShouldBe(JsonValueKind.Null, "no mobile was given");

        // The booking's barber reads the mobile while it is open, in the detail and the list; the owner never does.
        var iso = Iso(Target);
        (await DetailAsync(omar, id, ct)).GetProperty("customerPhone").GetString().ShouldBe(E164);
        (await OkAsync(omar.GetAsync($"/api/v1/shop/bookings?from={iso}&to={iso}", ct), ct)).GetProperty("items").EnumerateArray()
            .Single(b => b.GetProperty("id").GetGuid() == id).GetProperty("customerPhone").GetString().ShouldBe(E164);
        (await DetailAsync(w.OwnerA, id, ct)).GetProperty("customerPhone").ValueKind.ShouldBe(JsonValueKind.Null);
        var ownersList = await w.OwnerA.GetAsync($"/api/v1/shop/bookings?from={iso}&to={iso}", ct);
        (await ownersList.Content.ReadAsStringAsync(ct)).ShouldNotContain("551234567");
        await FailsAsync(faisal.GetAsync($"/api/v1/shop/bookings/{id}", ct), HttpStatusCode.NotFound, null, ct);

        // An online booking never carries a number.
        using (var customer = await CustomerAsync(w.Factory, "نورة", ct))
        {
            var online = await BookedAsync(customer, w, w.Haircut, w.Omar, At(Target, 16), ct);
            (await DetailAsync(omar, online.GetProperty("id").GetGuid(), ct)).GetProperty("customerPhone").ValueKind.ShouldBe(JsonValueKind.Null);
        }

        // Closed: the number is gone, for the barber too.
        var cancelled = await OkAsync(Transition(omar, id, "CancelledByShop", phone.GetProperty("version").GetUInt32(), ct, "اعتذر الزبون"), ct);
        cancelled.GetProperty("customerPhone").ValueKind.ShouldBe(JsonValueKind.Null);
        (await DetailAsync(omar, id, ct)).GetProperty("customerPhone").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Postponement_HoldsTheOfferedTime_UntilTheCustomerAcceptsOrDeclines()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "d147_postpone", ct);
        using var faisal = await w.BarberAsync(w.Faisal, ct);
        using var noura = await CustomerAsync(w.Factory, "نورة", ct);
        using var sara = await CustomerAsync(w.Factory, "سارة", ct);
        var booking = await BookedAsync(noura, w, w.Haircut, w.Faisal, At(Target, 10), ct);
        var id = booking.GetProperty("id").GetGuid();
        Task<HttpResponseMessage> Postpone(Guid bookingId, DateTimeOffset start, uint version) =>
            faisal.PostAsync($"/api/v1/shop/bookings/{bookingId}/postpone", new { startsAt = start, version }, ct);
        Task<HttpResponseMessage> Answer(ApiSession customer, Guid bookingId, string action, uint version) =>
            customer.PostAsync($"/api/v1/me/bookings/{bookingId}/postponement/{action}", new { version }, ct);
        async Task<JsonElement> Mine(ApiSession customer, Guid bookingId) => await OkAsync(customer.GetAsync($"/api/v1/me/bookings/{bookingId}", ct), ct);

        // Faisal offers 14:00 instead of 10:00: the booking stays at 10:00, and 14:00 is held for Noura.
        var offered = await OkAsync(Postpone(id, At(Target, 14), booking.GetProperty("version").GetUInt32()), ct);
        offered.GetProperty("startsAt").GetDateTimeOffset().ShouldBe(At(Target, 10));
        offered.GetProperty("postponement").GetProperty("startsAt").GetDateTimeOffset().ShouldBe(At(Target, 14));
        var mine = await Mine(noura, id);
        mine.GetProperty("startsAt").GetDateTimeOffset().ShouldBe(At(Target, 10));
        var offer = mine.GetProperty("postponement");
        offer.GetProperty("live").GetBoolean().ShouldBeTrue();
        offer.GetProperty("startsAt").GetDateTimeOffset().ShouldBe(At(Target, 14));
        offer.GetProperty("expiresAt").GetDateTimeOffset().ShouldBe(At(Target, 9), "until an hour before the original appointment");

        // Nobody else gets the held time: not online, not at the shop, not in the public slots.
        (await PublicSlotsAsync(w, w.Faisal, Target, ct)).ShouldNotContain("14:00");
        await FailsAsync(BookAsync(sara, w.SlugA, w.Haircut, w.Faisal, At(Target, 14), ct), HttpStatusCode.Conflict, "booking.slot_unavailable", ct);
        await FailsAsync(faisal.SendAsync(HttpMethod.Post, "/api/v1/shop/bookings/walk-in",
                new { serviceId = w.Haircut, professionalId = w.Faisal, startsAt = At(Target, 14), customerName = "زائر" }, ct, headers: Key()),
            HttpStatusCode.Conflict, "booking.slot_unavailable", ct);

        // Noura accepts: the booking moves to 14:00, the history says so, and 10:00 is free again.
        var accepted = await OkAsync(Answer(noura, id, "accept", mine.GetProperty("version").GetUInt32()), ct);
        accepted.GetProperty("startsAt").GetDateTimeOffset().ShouldBe(At(Target, 14));
        (await OkAsync(faisal.GetAsync($"/api/v1/shop/bookings/{id}", ct), ct)).GetProperty("history").EnumerateArray()
            .Select(h => h.GetProperty("kind").GetString()).ShouldContain("PostponementAccepted");
        (await PublicSlotsAsync(w, w.Faisal, Target, ct)).ShouldContain("10:00");
        await FailsAsync(Answer(noura, id, "accept", accepted.GetProperty("version").GetUInt32()), HttpStatusCode.Conflict, "booking.postponement_gone", ct);

        // Another offer, declined: the booking keeps its time and the held one is free again.
        var other = await BookedAsync(noura, w, w.Haircut, w.Faisal, At(Target, 16), ct);
        var otherId = other.GetProperty("id").GetGuid();
        await OkAsync(Postpone(otherId, At(Target, 17), other.GetProperty("version").GetUInt32()), ct);
        (await PublicSlotsAsync(w, w.Faisal, Target, ct)).ShouldNotContain("17:00");
        await FailsAsync(Answer(sara, otherId, "decline", other.GetProperty("version").GetUInt32()), HttpStatusCode.NotFound, null, ct);
        var declined = await OkAsync(Answer(noura, otherId, "decline", (await Mine(noura, otherId)).GetProperty("version").GetUInt32()), ct);
        declined.GetProperty("startsAt").GetDateTimeOffset().ShouldBe(At(Target, 16));
        (await PublicSlotsAsync(w, w.Faisal, Target, ct)).ShouldContain("17:00");
        (await Mine(noura, otherId)).GetProperty("postponement").ValueKind.ShouldBe(JsonValueKind.Null, "the customer sees an offer only while it is live");
        (await Mine(noura, id)).GetProperty("postponement").ValueKind.ShouldBe(JsonValueKind.Null, "the accepted one is done too");

        // Walk-in and phone bookings get their new time directly, never an offer.
        var phone = await OkAsync(faisal.SendAsync(HttpMethod.Post, "/api/v1/shop/bookings/walk-in",
                new { channel = "Phone", serviceId = w.Haircut, professionalId = w.Faisal, startsAt = At(Target, 18), customerName = "متصل" }, ct, headers: Key()),
            ct, HttpStatusCode.Created);
        await FailsAsync(Postpone(phone.GetProperty("id").GetGuid(), At(Target, 19), phone.GetProperty("version").GetUInt32()),
            HttpStatusCode.UnprocessableEntity, "booking.postpone_online_only", ct);
    }

    [Fact]
    public async Task Postponement_IsRefused_LessThanAnHourBeforeTheStart()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var w = await ArrangeAsync(postgres, "d147_postpone_late", ct, clock);
        await w.EnsureBarberLoginAsync(w.Faisal, ct);
        JsonElement booking;
        using (var noura = await CustomerAsync(w.Factory, "نورة", ct))
        {
            booking = await BookedAsync(noura, w, w.Haircut, w.Faisal, At(Target, 10), ct);
        }

        clock.SetUtcNow(At(Target, 9, 10).ToUniversalTime());
        using var faisal = await w.BarberAsync(w.Faisal, ct);
        await FailsAsync(
            faisal.PostAsync($"/api/v1/shop/bookings/{booking.GetProperty("id").GetGuid()}/postpone", new { startsAt = At(Target, 14), version = booking.GetProperty("version").GetUInt32() }, ct),
            HttpStatusCode.UnprocessableEntity, "booking.postpone_too_late", ct);
    }

    [Fact]
    public async Task Reports_ShowEachBarbersOutcomes_ActualLengths_Moves_AndExpectedIncome()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var w = await ArrangeAsync(postgres, "d147_reports", ct, clock);
        await w.EnsureBarberLoginAsync(w.Faisal, ct);
        await w.EnsureBarberLoginAsync(w.Omar, ct);
        JsonElement served, quick, moved, forgotten;
        using (var noura = await CustomerAsync(w.Factory, "نورة", ct))
        {
            served = await BookedAsync(noura, w, w.Haircut, w.Faisal, At(Target, 10), ct);
            quick = await BookedAsync(noura, w, w.Haircut, w.Faisal, At(Target, 11), ct);
            moved = await BookedAsync(noura, w, w.Haircut, w.Faisal, At(Target, 13), ct);
            forgotten = await BookedAsync(noura, w, w.Haircut, w.Omar, At(Target, 10), ct);
        }

        async Task Step(DateOnly day, int hour, int minute, Guid professional, Func<ApiSession, Task> act)
        {
            clock.SetUtcNow(At(day, hour, minute).ToUniversalTime());
            using var barber = await w.BarberAsync(professional, ct);
            await act(barber);
        }

        Task<JsonElement> Do(ApiSession barber, JsonElement booking, string to) =>
            TransitionAsync(barber, booking.GetProperty("id").GetGuid(), to, ct, to == "CancelledByShop" ? "سبب" : null);

        // 09:00 Faisal gives his 13:00 to Omar; 10:00–10:25 a 25-minute visit; 11:00–11:03 a 3-minute one; 11:30 Omar's
        // forgotten 10:00 is red and Faisal closes it; 13:20 Omar marks the moved one a no-show.
        await Step(Target, 9, 0, w.Faisal, async f => await OkAsync(f.SendAsync(HttpMethod.Post, $"/api/v1/shop/bookings/{moved.GetProperty("id").GetGuid()}/professional",
            new { professionalId = w.Omar, version = moved.GetProperty("version").GetUInt32() }, ct, headers: Key()), ct));
        await Step(Target, 10, 0, w.Faisal, f => Do(f, served, "Arrived"));
        await Step(Target, 10, 25, w.Faisal, f => Do(f, served, "Completed"));
        await Step(Target, 11, 0, w.Faisal, f => Do(f, quick, "Arrived"));
        await Step(Target, 11, 3, w.Faisal, f => Do(f, quick, "Completed"));
        await Step(Target, 11, 30, w.Faisal, f => Do(f, forgotten, "Completed"));
        await Step(Target, 13, 20, w.Omar, f => Do(f, moved, "NoShow"));

        clock.SetUtcNow(At(Target, 18).ToUniversalTime());
        using var owner = await IdentityTestData.SignInStaffAsync(w.Factory, w.Shops.A.OwnerEmail, ct);
        var url = $"/api/v1/shop/reports?from={Iso(Target)}&to={Iso(Target)}";
        using (var faisal = await w.BarberAsync(w.Faisal, ct))
        {
            await FailsAsync(faisal.GetAsync(url, ct), HttpStatusCode.Forbidden, null, ct);
        }

        await FailsAsync(owner.GetAsync($"/api/v1/shop/reports?from={Iso(Target)}&to={Iso(Target.AddDays(31))}", ct), HttpStatusCode.BadRequest, "validation.out_of_range", ct);
        var report = await OkAsync(owner.GetAsync(url, ct), ct);
        var shop = report.GetProperty("shop");
        shop.GetProperty("bookings").GetInt32().ShouldBe(4);
        shop.GetProperty("completed").GetInt32().ShouldBe(3);
        shop.GetProperty("noShows").GetInt32().ShouldBe(1);
        shop.GetProperty("movedIn").GetInt32().ShouldBe(1);
        shop.GetProperty("movedOut").GetInt32().ShouldBe(1);
        shop.GetProperty("closedLate").GetInt32().ShouldBe(1);
        shop.GetProperty("averageMinutes").GetInt32().ShouldBe(14, "(25 + 3) / 2: the visit closed late has no actual length and is left out");
        shop.GetProperty("shortVisits").GetInt32().ShouldBe(1);
        shop.GetProperty("expectedIncome").GetDecimal().ShouldBe(180m, "three completed haircuts at 60");

        JsonElement Barber(Guid id) => report.GetProperty("barbers").EnumerateArray().Single(b => b.GetProperty("professionalId").GetGuid() == id).GetProperty("counts");
        var faisalCounts = Barber(w.Faisal);
        faisalCounts.GetProperty("bookings").GetInt32().ShouldBe(2);
        faisalCounts.GetProperty("movedOut").GetInt32().ShouldBe(1);
        faisalCounts.GetProperty("averageMinutes").GetInt32().ShouldBe(14);
        faisalCounts.GetProperty("expectedIncome").GetDecimal().ShouldBe(120m);
        var omarCounts = Barber(w.Omar);
        omarCounts.GetProperty("bookings").GetInt32().ShouldBe(2);
        omarCounts.GetProperty("movedIn").GetInt32().ShouldBe(1);
        omarCounts.GetProperty("noShows").GetInt32().ShouldBe(1);
        omarCounts.GetProperty("closedLate").GetInt32().ShouldBe(1);
        omarCounts.GetProperty("averageMinutes").ValueKind.ShouldBe(JsonValueKind.Null);
        omarCounts.GetProperty("expectedIncome").GetDecimal().ShouldBe(60m);

        JsonElement Visit(JsonElement booking) => report.GetProperty("visits").EnumerateArray().Single(v => v.GetProperty("id").GetGuid() == booking.GetProperty("id").GetGuid());
        Visit(served).GetProperty("actualMinutes").GetInt32().ShouldBe(25);
        Visit(served).GetProperty("underFiveMinutes").GetBoolean().ShouldBeFalse();
        Visit(quick).GetProperty("actualMinutes").GetInt32().ShouldBe(3);
        Visit(quick).GetProperty("underFiveMinutes").GetBoolean().ShouldBeTrue();
        Visit(forgotten).GetProperty("closedLate").GetBoolean().ShouldBeTrue();
        Visit(forgotten).GetProperty("closedBy").GetString().ShouldBe("فيصل");
        Visit(moved).GetProperty("expectedIncome").GetDecimal().ShouldBe(0m, "a no-show brings nothing in");
        report.ToString().ShouldNotContain("+966");
    }

    [Fact]
    public async Task BookingServices_AreAddedAfterArrival_AndRemovedUntilTheVisitEnds()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var w = await ArrangeAsync(postgres, "d147_services", ct, clock);
        var trim = (await OkAsync(w.OwnerA.PostAsync("/api/v1/shop/services", new { nameAr = "تحديد", price = 15m, durationMinutes = 15, onlineBookable = true }, ct), ct, HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        await OkAsync(w.Admin.PutAsync($"/api/v1/admin/professionals/{w.Faisal}/services", new { serviceIds = new[] { w.Haircut, w.Beard, trim } }, ct), ct);
        await w.EnsureBarberLoginAsync(w.Faisal, ct);
        await w.EnsureBarberLoginAsync(w.Omar, ct);
        JsonElement visit, next, package;
        using (var customer = await CustomerAsync(w.Factory, "نورة", ct))
        {
            visit = await BookedAsync(customer, w, w.Haircut, w.Faisal, At(Target, 10), ct);
            next = await BookedAsync(customer, w, w.Haircut, w.Faisal, At(Target, 10, 50), ct);
            package = await BookedAsync(customer, w, w.Haircut, w.Omar, At(Target, 12), ct, w.Package);
        }

        var id = visit.GetProperty("id").GetGuid();
        Task<HttpResponseMessage> Add(ApiSession who, Guid booking, Guid service, uint version) =>
            who.PostAsync($"/api/v1/shop/bookings/{booking}/services", new { serviceId = service, version }, ct);
        Task<HttpResponseMessage> Remove(ApiSession who, Guid booking, Guid service, uint version) =>
            who.PostAsync($"/api/v1/shop/bookings/{booking}/services/{service}/delete", new { version }, ct);

        clock.SetUtcNow(At(Target, 9, 58).ToUniversalTime());
        using var faisal = await w.BarberAsync(w.Faisal, ct);

        // Adding waits for «وصل».
        await FailsAsync(Add(faisal, id, w.Beard, visit.GetProperty("version").GetUInt32()), HttpStatusCode.UnprocessableEntity, "booking.add_after_arrival", ct);
        var arrived = await OkAsync(Transition(faisal, id, "Arrived", visit.GetProperty("version").GetUInt32(), ct), ct);

        // A beard after the haircut: longer and dearer; named in the history, by Faisal.
        var added = await OkAsync(Add(faisal, id, w.Beard, arrived.GetProperty("version").GetUInt32()), ct);
        added.GetProperty("endsAt").GetDateTimeOffset().ShouldBe(At(Target, 10, 50));
        added.GetProperty("item").GetProperty("price").GetDecimal().ShouldBe(120m);
        added.GetProperty("item").GetProperty("durationMinutes").GetInt32().ShouldBe(50);
        added.GetProperty("item").GetProperty("nameAr").GetString().ShouldBe("حلاقة + لحية");
        var items = added.GetProperty("item").GetProperty("packageItems");
        items.EnumerateArray().Select(i => i.GetProperty("serviceId").GetGuid()).ShouldBe([w.Haircut, w.Beard]);
        items[1].GetProperty("added").GetBoolean().ShouldBeTrue();
        var history = (await OkAsync(faisal.GetAsync($"/api/v1/shop/bookings/{id}", ct), ct)).GetProperty("history");
        history[history.GetArrayLength() - 1].GetProperty("kind").GetString().ShouldBe("ServiceAdded");
        history[history.GetArrayLength() - 1].GetProperty("reason").GetString().ShouldBe("لحية");
        history[history.GetArrayLength() - 1].GetProperty("actorName").GetString().ShouldBe("فيصل");

        // Not twice; and not when the next booking is in the way (10:50).
        var version = added.GetProperty("version").GetUInt32();
        await FailsAsync(Add(faisal, id, w.Beard, version), HttpStatusCode.Conflict, "booking.service_already_booked", ct);
        await FailsAsync(Add(faisal, id, trim, version), HttpStatusCode.Conflict, "booking.professional_busy", ct);
        next.GetProperty("id").GetGuid().ShouldNotBe(id);

        // Taken off again: back to one service; the last one stays.
        var removed = await OkAsync(Remove(faisal, id, w.Beard, version), ct);
        removed.GetProperty("endsAt").GetDateTimeOffset().ShouldBe(At(Target, 10, 30));
        removed.GetProperty("item").GetProperty("price").GetDecimal().ShouldBe(60m);
        removed.GetProperty("item").GetProperty("serviceId").GetGuid().ShouldBe(w.Haircut);
        removed.GetProperty("item").GetProperty("nameAr").GetString().ShouldBe("حلاقة");
        removed.GetProperty("item").GetProperty("packageItems").GetArrayLength().ShouldBe(0);
        await FailsAsync(Remove(faisal, id, w.Haircut, removed.GetProperty("version").GetUInt32()), HttpStatusCode.UnprocessableEntity, "booking.last_service", ct);

        // Once the visit has ended, nothing changes.
        var again = await OkAsync(Add(faisal, id, w.Beard, removed.GetProperty("version").GetUInt32()), ct);
        var done = await OkAsync(Transition(faisal, id, "Completed", again.GetProperty("version").GetUInt32(), ct), ct);
        await FailsAsync(Remove(faisal, id, w.Beard, done.GetProperty("version").GetUInt32()), HttpStatusCode.Conflict, "booking.services_closed", ct);

        // A package's own services stay with it; a colleague's booking is out of reach.
        var packageId = package.GetProperty("id").GetGuid();
        await FailsAsync(Remove(faisal, packageId, w.Haircut, package.GetProperty("version").GetUInt32()), HttpStatusCode.NotFound, null, ct);
        using var omar = await w.BarberAsync(w.Omar, ct);
        await FailsAsync(Remove(omar, packageId, w.Haircut, package.GetProperty("version").GetUInt32()), HttpStatusCode.UnprocessableEntity, "booking.service_fixed", ct);
    }

    /// <summary>
    /// The hold cannot race (R-BKG-03 for offers): an offer and another customer's booking of the same time, sent together,
    /// never both succeed. Both triggers take the same per-barber lock, so whichever commits second sees the first.
    /// </summary>
    [Fact]
    public async Task Postponement_AndABookingOfTheSameTime_SentTogether_NeverBothWin()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "d147_race", ct);
        using var faisal = await w.BarberAsync(w.Faisal, ct);
        using var noura = await CustomerAsync(w.Factory, "نورة", ct);
        using var sara = await CustomerAsync(w.Factory, "سارة", ct);
        var outcomes = new List<(bool Offered, bool Booked)>();
        for (var round = 0; round < 5; round++)
        {
            var booking = await BookedAsync(noura, w, w.Haircut, w.Faisal, At(Target, 10 + round), ct);
            var contested = At(Target, 16 + round);
            var responses = await Task.WhenAll(
                Task.Run(() => faisal.PostAsync(
                    $"/api/v1/shop/bookings/{booking.GetProperty("id").GetGuid()}/postpone",
                    new { startsAt = contested, version = booking.GetProperty("version").GetUInt32() }, ct), ct),
                Task.Run(() => BookAsync(sara, w.SlugA, w.Haircut, w.Faisal, contested, ct), ct));
            foreach (var response in responses)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode)
                {
                    response.StatusCode.ShouldBe(HttpStatusCode.Conflict, body);
                    body.ShouldContain("booking.slot_unavailable");
                }
            }

            outcomes.Add((responses[0].IsSuccessStatusCode, responses[1].IsSuccessStatusCode));
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        outcomes.ShouldAllBe(o => o.Offered != o.Booked, string.Join(", ", outcomes));
    }
}
