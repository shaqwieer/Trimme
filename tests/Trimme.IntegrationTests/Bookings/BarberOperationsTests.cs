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

    /// <summary>An online booking by its own customer (one active booking per customer, D-155), named «نورة».</summary>
    private static async Task<JsonElement> BookedAsync(BookingWorld w, Guid serviceId, Guid professionalId, DateTimeOffset start, CancellationToken ct, Guid? packageId = null)
    {
        using var customer = await CustomerAsync(w.Factory, "نورة", ct);
        return await OkAsync(BookAsync(customer, w.SlugA, serviceId, professionalId, start, ct, packageId: packageId), ct, HttpStatusCode.Created);
    }

    [Fact]
    public async Task TheOwner_WatchesEverything_AndChangesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "d147_owner", ct);
        using var customer = await CustomerAsync(w.Factory, "نورة", ct);
        var booking = await BookedAsync(w, w.Haircut, w.Faisal, At(Target, 10), ct);
        var id = booking.GetProperty("id").GetGuid();
        var version = booking.GetProperty("version").GetUInt32();
        var iso = Iso(Target);

        // Every action on a booking and the barbers' breaks are the barbers' (D-147); the pause is the owner's again (D-155).
        foreach (var call in new Func<Task<HttpResponseMessage>>[]
        {
            () => Transition(w.OwnerA, id, "CancelledByShop", version, ct, "إغلاق طارئ"),
            () => w.OwnerA.PostAsync($"/api/v1/shop/bookings/{id}/notes", new { text = "x" }, ct),
            () => w.OwnerA.SendAsync(HttpMethod.Post, $"/api/v1/shop/bookings/{id}/reschedule", new { startsAt = At(Target, 12), version }, ct, headers: Key()),
            () => w.OwnerA.SendAsync(HttpMethod.Post, $"/api/v1/shop/bookings/{id}/professional", new { professionalId = w.Omar, version }, ct, headers: Key()),
            () => w.OwnerA.PostAsync($"/api/v1/shop/bookings/{id}/services", new { serviceId = w.Beard, version }, ct),
            () => w.OwnerA.PostAsync($"/api/v1/shop/bookings/{id}/services/{w.Haircut}/delete", new { version }, ct),
            () => w.OwnerA.PutAsync("/api/v1/shop/schedule/barber-day", new { professionalId = w.Faisal, date = iso, breaks = Array.Empty<object>() }, ct),
            () => w.OwnerA.GetAsync($"/api/v1/shop/schedule/barber-day?professionalId={w.Faisal}&date={iso}", ct),
            () => w.OwnerA.SendAsync(HttpMethod.Post, "/api/v1/shop/bookings/walk-in", new { serviceId = w.Haircut, professionalId = w.Faisal, startsAt = At(Target, 15), customerName = "زائر" }, ct, headers: Key()),
        })
        {
            using var response = await call();
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{response.RequestMessage!.Method} {response.RequestMessage.RequestUri}");
        }

        // What the owner still sees: the bookings, their details (no actions offered by the API's rules apply to the
        // barbers), the shop's red bookings, the calendar, and the overview with its figures.
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
    public async Task OnlineBookings_AreConfirmedAtOnce_AndAColleaguesOrdinaryBookingIsReadOnly()
    {
        // D-155 «التأكيد التلقائي دايم»: no shop confirms an online booking, so nothing waits and nobody accepts or rejects.
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "d155_confirmed", ct);
        using var faisal = await w.BarberAsync(w.Faisal, ct);
        var booking = await BookedAsync(w, w.Haircut, w.Omar, At(Target, 10), ct);
        booking.GetProperty("status").GetString().ShouldBe("Confirmed");
        var id = booking.GetProperty("id").GetGuid();

        var attention = await AttentionAsync(faisal, ct);
        attention.TryGetProperty("pending", out _).ShouldBeFalse();
        attention.GetProperty("overdue").GetArrayLength().ShouldBe(0);
        Allowed(await DetailAsync(faisal, id, ct)).ShouldBeEmpty("a colleague's ordinary booking is read-only (D-151)");
        await FailsAsync(Transition(faisal, id, "CancelledByShop", booking.GetProperty("version").GetUInt32(), ct, "تجربة"), HttpStatusCode.NotFound, null, ct);
        (await OkAsync(w.OwnerA.GetAsync("/api/v1/shop/booking-settings", ct), ct)).TryGetProperty("requireManualConfirmation", out _).ShouldBeFalse();
        var counts = (await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/bookings?from={Iso(Target)}&to={Iso(Target)}", ct), ct)).GetProperty("counts");
        counts.TryGetProperty("pending", out _).ShouldBeFalse();
        counts.GetProperty("confirmed").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task RedBookings_AnyBarberClosesThem_LateAndNamed()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var w = await ArrangeAsync(postgres, "d147_red", ct, clock);
        await w.EnsureBarberLoginAsync(w.Faisal, ct);
        await w.EnsureBarberLoginAsync(w.Omar, ct);
        Guid id, second;
        using (var customer = await CustomerAsync(w.Factory, "نورة", ct))
        {
            id = (await BookedAsync(w, w.Haircut, w.Omar, At(Target, 10), ct)).GetProperty("id").GetGuid();
            second = (await BookedAsync(w, w.Haircut, w.Omar, At(Target, 11), ct)).GetProperty("id").GetGuid();
        }

        // 10:25: the 30-minute visit has not ended: not red, and Omar's. Faisal opens it read-only (D-151).
        clock.SetUtcNow(At(Target, 10, 25).ToUniversalTime());
        using (var faisal = await w.BarberAsync(w.Faisal, ct))
        {
            (await AttentionAsync(faisal, ct)).GetProperty("overdue").GetArrayLength().ShouldBe(0);
            Allowed(await DetailAsync(faisal, id, ct)).ShouldBeEmpty("a colleague's booking that is neither pending nor red is read-only");
        }

        // 10:33, three minutes after its end: red (D-151: «حجز فات وقته ٦:٠٠، والساعة ٦:٠٣ شكله عادي»), for every barber of
        // the shop and the owner. A colleague may press the usual buttons: «وصل», «انتهت الخدمة», «ما حضر», or cancel.
        clock.SetUtcNow(At(Target, 10, 33).ToUniversalTime());
        using (var faisal = await w.BarberAsync(w.Faisal, ct))
        {
            var early = (await AttentionAsync(faisal, ct)).GetProperty("overdue").EnumerateArray().ShouldHaveSingleItem();
            early.GetProperty("id").GetGuid().ShouldBe(id);
            Allowed(await DetailAsync(faisal, id, ct)).ShouldBe(["Arrived", "Completed", "NoShow", "CancelledByShop"]);
        }

        // 10:50: still red, for every barber of the shop and the owner.
        clock.SetUtcNow(At(Target, 10, 50).ToUniversalTime());
        using var faisalLater = await w.BarberAsync(w.Faisal, ct);
        using var omarLater = await w.BarberAsync(w.Omar, ct);
        var red = (await AttentionAsync(faisalLater, ct)).GetProperty("overdue").EnumerateArray().ShouldHaveSingleItem();
        red.GetProperty("id").GetGuid().ShouldBe(id);
        red.GetProperty("overdue").GetBoolean().ShouldBeTrue();
        red.GetProperty("professional").GetProperty("id").GetGuid().ShouldBe(w.Omar, "it names the barber responsible");
        Ids((await AttentionAsync(omarLater, ct)).GetProperty("overdue")).ShouldBe([id]);
        var detail = await DetailAsync(faisalLater, id, ct);
        Allowed(detail).ShouldBe(["Arrived", "Completed", "NoShow", "CancelledByShop"], "a colleague closes it (D-147), or marks the arrival (D-151)");

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

        // The 11:00 visit: red at 11:31; a colleague marks «وصل» (D-151) and closes it at 11:40, within the quarter hour after
        // its end, so it is not «انقفل متأخر» and keeps its times.
        clock.SetUtcNow(At(Target, 11, 31).ToUniversalTime());
        using (var faisal = await w.BarberAsync(w.Faisal, ct))
        {
            var version = (await DetailAsync(faisal, second, ct)).GetProperty("version").GetUInt32();
            var arrived = await OkAsync(Transition(faisal, second, "Arrived", version, ct), ct);
            arrived.GetProperty("status").GetString().ShouldBe("Arrived");
            arrived.GetProperty("overdue").GetBoolean().ShouldBeTrue("still open past its end");
            clock.SetUtcNow(At(Target, 11, 40).ToUniversalTime());
            var done = await OkAsync(Transition(faisal, second, "Completed", arrived.GetProperty("version").GetUInt32(), ct), ct);
            done.GetProperty("closedLate").GetBoolean().ShouldBeFalse();
            done.GetProperty("closedBy").GetString().ShouldBe("فيصل");
            done.GetProperty("actualStartAt").GetDateTimeOffset().ShouldBe(At(Target, 11, 31));
            Ids((await AttentionAsync(faisal, ct)).GetProperty("closedLate")).ShouldBe([id], "only the one closed more than a quarter hour late");
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
            served = await BookedAsync(w, w.Haircut, w.Faisal, At(Target, 10), ct);
            missed = await BookedAsync(w, w.Haircut, w.Faisal, At(Target, 11), ct);
            lateCancel = await BookedAsync(w, w.Haircut, w.Faisal, At(Target, 12), ct);
            earlyCancel = await BookedAsync(w, w.Haircut, w.Faisal, At(Target, 14), ct);
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
        phone.GetProperty("customerPhone").ValueKind.ShouldBe(JsonValueKind.Null, "it is Omar's booking: a colleague never reads the number (D-151)");
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
        // Faisal opens his colleague's booking read-only, without the number (D-151).
        var colleagues = await DetailAsync(faisal, id, ct);
        colleagues.GetProperty("customerPhone").ValueKind.ShouldBe(JsonValueKind.Null);
        Allowed(colleagues).ShouldBeEmpty();

        // An online booking never carries a number.
        using (var customer = await CustomerAsync(w.Factory, "نورة", ct))
        {
            var online = await BookedAsync(w, w.Haircut, w.Omar, At(Target, 16), ct);
            (await DetailAsync(omar, online.GetProperty("id").GetGuid(), ct)).GetProperty("customerPhone").ValueKind.ShouldBe(JsonValueKind.Null);
        }

        // Closed: the number is gone, for the barber too.
        var cancelled = await OkAsync(Transition(omar, id, "CancelledByShop", phone.GetProperty("version").GetUInt32(), ct, "اعتذر الزبون"), ct);
        cancelled.GetProperty("customerPhone").ValueKind.ShouldBe(JsonValueKind.Null);
        (await DetailAsync(omar, id, ct)).GetProperty("customerPhone").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task TheBarbersNewTime_MovesAnOnlineBookingAtOnce_OnceADayForTheCustomer()
    {
        // D-155 «الحلاق يغيّر الموعد مباشرة، بدون موافقة الزبون»: no offer, no waiting. The customer is told and may cancel
        // or change it; the shop changes the same customer's time once a day.
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "d155_direct", ct);
        using var faisal = await w.BarberAsync(w.Faisal, ct);
        using var noura = await CustomerAsync(w.Factory, "نورة", ct);
        var booking = await OkAsync(BookAsync(noura, w.SlugA, w.Haircut, w.Faisal, At(Target, 10), ct), ct, HttpStatusCode.Created);
        var id = booking.GetProperty("id").GetGuid();
        Task<HttpResponseMessage> Reschedule(Guid bookingId, DateTimeOffset start, uint version) =>
            faisal.SendAsync(HttpMethod.Post, $"/api/v1/shop/bookings/{bookingId}/reschedule", new { startsAt = start, version }, ct, headers: Key());

        var moved = await OkAsync(Reschedule(id, At(Target, 14), booking.GetProperty("version").GetUInt32()), ct);
        moved.GetProperty("startsAt").GetDateTimeOffset().ShouldBe(At(Target, 14));
        moved.GetProperty("changedToday").GetBoolean().ShouldBeTrue("the answer says the day's change is used");
        var mine = await OkAsync(noura.GetAsync($"/api/v1/me/bookings/{id}", ct), ct);
        mine.GetProperty("startsAt").GetDateTimeOffset().ShouldBe(At(Target, 14));
        mine.TryGetProperty("postponement", out _).ShouldBeFalse();
        mine.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()).ShouldBe(["Cancel", "Reschedule"]);
        (await PublicSlotsAsync(w, w.Faisal, Target, ct)).ShouldContain("10:00");

        // The history says who changed it and from when; the detail tells the barber the day's change is used.
        var detail = await OkAsync(faisal.GetAsync($"/api/v1/shop/bookings/{id}", ct), ct);
        var last = detail.GetProperty("history").EnumerateArray().Last();
        last.GetProperty("kind").GetString().ShouldBe("Rescheduled");
        last.GetProperty("actorName").GetString().ShouldBe("فيصل");
        last.GetProperty("previousStartsAt").GetDateTimeOffset().ShouldBe(At(Target, 10));
        detail.GetProperty("booking").GetProperty("changedToday").GetBoolean().ShouldBeTrue();

        // A second change today is refused; giving it to a colleague at the same time, or cancelling, still works.
        await FailsAsync(Reschedule(id, At(Target, 15), moved.GetProperty("version").GetUInt32()), HttpStatusCode.Conflict, "booking.changed_today", ct);
        var given = await OkAsync(faisal.SendAsync(HttpMethod.Post, $"/api/v1/shop/bookings/{id}/professional",
            new { professionalId = w.Omar, version = moved.GetProperty("version").GetUInt32() }, ct, headers: Key()), ct);
        given.GetProperty("professional").GetProperty("id").GetGuid().ShouldBe(w.Omar);

        // The customer's bell keeps the change with the old time (D-155).
        await ProcessOutboxAsync(w.Factory, ct);
        var bell = (await OkAsync(noura.GetAsync("/api/v1/me/notifications", ct), ct)).GetProperty("items").EnumerateArray().ToList();
        bell.ShouldContain(n => n.GetProperty("kind").GetString() == "customer.booking_confirmed", "the booking's own confirmation is kept too");
        var change = bell.Single(n => n.GetProperty("kind").GetString() == "customer.booking_rescheduled");
        change.GetProperty("parameters").GetProperty("previousStartsAt").GetDateTimeOffset().ShouldBe(At(Target, 10));

        // The shop's own phone bookings are not limited.
        var phone = await OkAsync(faisal.SendAsync(HttpMethod.Post, "/api/v1/shop/bookings/walk-in",
                new { channel = "Phone", serviceId = w.Haircut, professionalId = w.Faisal, startsAt = At(Target, 18), customerName = "متصل" }, ct, headers: Key()),
            ct, HttpStatusCode.Created);
        var once = await OkAsync(Reschedule(phone.GetProperty("id").GetGuid(), At(Target, 19), phone.GetProperty("version").GetUInt32()), ct);
        await OkAsync(Reschedule(phone.GetProperty("id").GetGuid(), At(Target, 20), once.GetProperty("version").GetUInt32()), ct);
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
            served = await BookedAsync(w, w.Haircut, w.Faisal, At(Target, 10), ct);
            quick = await BookedAsync(w, w.Haircut, w.Faisal, At(Target, 11), ct);
            moved = await BookedAsync(w, w.Haircut, w.Faisal, At(Target, 13), ct);
            forgotten = await BookedAsync(w, w.Haircut, w.Omar, At(Target, 10), ct);
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
            visit = await BookedAsync(w, w.Haircut, w.Faisal, At(Target, 10), ct);
            next = await BookedAsync(w, w.Haircut, w.Faisal, At(Target, 10, 50), ct);
            package = await BookedAsync(w, w.Haircut, w.Omar, At(Target, 12), ct, w.Package);
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
    /// One active booking per customer, at every shop (D-155, «حجز واحد فعّال بس للزبون، في كل المحلات»), checked on the
    /// server under a per-customer lock: the same customer booking two shops at the same moment gets exactly one.
    /// </summary>
    [Fact]
    public async Task OneCustomer_BookingTwoShopsAtOnce_GetsOnlyOne()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "d155_one_active", ct);
        var slugB = (await OkAsync(w.Admin.GetAsync($"/api/v1/admin/shops/{w.Shops.B.ShopId}", ct), ct)).GetProperty("slug").GetString()!;
        using var noura = await CustomerAsync(w.Factory, "نورة", ct);
        (await noura.GetAsync("/api/v1/me/bookings/active", ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        for (var round = 0; round < 4; round++)
        {
            var responses = await Task.WhenAll(
                Task.Run(() => BookAsync(noura, w.SlugA, w.Haircut, w.Faisal, At(Target, 10 + round), ct), ct),
                Task.Run(() => BookAsync(noura, slugB, w.ServiceB, w.ProB, At(Target, 10 + round), ct), ct));
            responses.Count(r => r.IsSuccessStatusCode).ShouldBe(1, $"round {round}");
            var refused = responses.Single(r => !r.IsSuccessStatusCode);
            refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await refused.Content.ReadAsStringAsync(ct)).ShouldContain("booking.active_booking_exists");

            // The one that won is the customer's active booking; cancelling it frees them to book again.
            var won = await OkAsync(Task.FromResult(responses.Single(r => r.IsSuccessStatusCode)), ct, HttpStatusCode.Created);
            var active = await OkAsync(noura.GetAsync("/api/v1/me/bookings/active", ct), ct);
            active.GetProperty("id").GetGuid().ShouldBe(won.GetProperty("id").GetGuid());
            active.GetProperty("shop").GetProperty("slug").GetString().ShouldNotBeNullOrEmpty();
            await OkAsync(noura.PostAsync($"/api/v1/me/bookings/{won.GetProperty("id").GetGuid()}/cancel", new { reason = (string?)null, version = won.GetProperty("version").GetUInt32() }, ct), ct);
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        // A booking past its end that nobody closed (red) does not stop a new one; one that has not ended does.
        var first = await OkAsync(BookAsync(noura, w.SlugA, w.Haircut, w.Faisal, At(Target, 16), ct), ct, HttpStatusCode.Created);
        await FailsAsync(BookAsync(noura, slugB, w.ServiceB, w.ProB, At(Target, 17), ct), HttpStatusCode.Conflict, "booking.active_booking_exists", ct);
        await using (var connection = new NpgsqlConnection(w.Factory.ConnectionString))
        {
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand(
                "UPDATE bookings.bookings SET starts_at = now() - interval '2 hours', ends_at = now() - interval '90 minutes', blocked_until = now() - interval '90 minutes' WHERE id = @id",
                connection);
            command.Parameters.AddWithValue("id", first.GetProperty("id").GetGuid());
            (await command.ExecuteNonQueryAsync(ct)).ShouldBe(1);
        }

        (await noura.GetAsync("/api/v1/me/bookings/active", ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await OkAsync(BookAsync(noura, slugB, w.ServiceB, w.ProB, At(Target, 17), ct), ct, HttpStatusCode.Created);
    }
}
