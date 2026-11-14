using System.Net;
using System.Text.Json;
using Npgsql;
using Shouldly;
using Trimme.IntegrationTests.Identity;
using Trimme.IntegrationTests.Infrastructure;
using static Trimme.IntegrationTests.Bookings.BookingTestData;

namespace Trimme.IntegrationTests.Bookings;

/// <summary>
/// D-145: the shop changes a booking for its customer: a new time with the same barber, or the same time with another
/// barber of the shop. A barber changes only their own bookings. After a change of barber the customer may cancel or
/// change the booking even inside the cancellation window, until it starts.
/// </summary>
public sealed class ShopBookingChangeTests(PostgresFixture postgres)
{
    private static Task<HttpResponseMessage> RescheduleAsync(ApiSession shop, Guid id, DateTimeOffset start, uint version, CancellationToken ct, string? key = null) =>
        shop.SendAsync(HttpMethod.Post, $"/api/v1/shop/bookings/{id}/reschedule", new { startsAt = start, version }, ct, headers: Key(key));

    private static Task<HttpResponseMessage> ChangeAsync(ApiSession shop, Guid id, Guid professionalId, uint version, CancellationToken ct) =>
        shop.SendAsync(HttpMethod.Post, $"/api/v1/shop/bookings/{id}/professional", new { professionalId, version }, ct, headers: Key());

    private static async Task<object?> ScalarAsync(TrimmeApiFactory factory, string sql, CancellationToken ct, object? parameter = null)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        if (parameter is not null)
        {
            command.Parameters.AddWithValue("p", parameter);
        }

        return await command.ExecuteScalarAsync(ct);
    }

    /// <summary>A week-long cancellation window: every booking of the coming days is inside it (no clock tricks needed).</summary>
    private static async Task WeekLongWindowAsync(BookingWorld w, CancellationToken ct)
    {
        var settings = await OkAsync(w.Admin.GetAsync("/api/v1/admin/settings", ct), ct);
        var body = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(settings.GetRawText())!
            .Where(p => p.Key is not ("currency" or "timeZone" or "countryCode" or "defaultLocale" or "updatedAt" or "editable"))
            .ToDictionary(p => p.Key, p => (object)p.Value);
        body["cancellationCutoffMinutes"] = 10_080;
        await OkAsync(w.Admin.PutAsync("/api/v1/admin/settings", body, ct), ct);
    }

    [Fact]
    public async Task TheOwnerAndTheBarber_GiveABookingANewTime_TheCustomerSeesIt_AndAColleaguesBookingIsOutOfReach()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "shop_reschedule", ct);
        using var customer = await CustomerAsync(w.Factory, "نورة", ct);
        var day = TodayAt(DateTimeOffset.UtcNow).AddDays(3);
        var booking = await OkAsync(BookAsync(customer, w.SlugA, w.Haircut, w.Faisal, At(day, 10), ct), ct, HttpStatusCode.Created);
        var id = booking.GetProperty("id").GetGuid();
        var version = booking.GetProperty("version").GetUInt32();

        // The dates and starts on the desk's rules, its own time not blocking it.
        var dates = await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/bookings/{id}/reschedule/dates?from={Iso(day)}&to={Iso(day.AddDays(1))}", ct), ct);
        dates.GetProperty("dates")[0].GetProperty("slotCount").GetInt32().ShouldBeGreaterThan(0);
        var slots = await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/bookings/{id}/reschedule/slots?date={Iso(day)}", ct), ct);
        slots.GetProperty("slots").EnumerateArray().Select(s => s.GetProperty("localTime").GetString()).ShouldContain("10:00");

        // The owner moves it; the same key replays; a stale version is refused.
        const string key = "owner-move-1";
        var moved = await OkAsync(RescheduleAsync(w.OwnerA, id, At(day, 12), version, ct, key), ct);
        moved.GetProperty("startsAt").GetDateTimeOffset().ShouldBe(At(day, 12));
        using (var again = await RescheduleAsync(w.OwnerA, id, At(day, 12), version, ct, key))
        {
            again.StatusCode.ShouldBe(HttpStatusCode.OK);
            again.Headers.GetValues("Idempotent-Replayed").ShouldBe(["true"]);
        }

        await FailsAsync(RescheduleAsync(w.OwnerA, id, At(day, 13), version, ct), HttpStatusCode.Conflict, null, ct);
        await FailsAsync(RescheduleAsync(w.OwnerA, id, DateTimeOffset.UtcNow.AddHours(-1), moved.GetProperty("version").GetUInt32(), ct), HttpStatusCode.BadRequest, "validation.date_in_past", ct);
        using (var noKey = await w.OwnerA.PostAsync($"/api/v1/shop/bookings/{id}/reschedule", new { startsAt = At(day, 13), version }, ct))
        {
            noKey.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        // The customer's booking shows the new time; the history says the shop moved it; the outbox tells the customer.
        (await OkAsync(customer.GetAsync($"/api/v1/me/bookings/{id}", ct), ct)).GetProperty("startsAt").GetDateTimeOffset().ShouldBe(At(day, 12));
        var detail = await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/bookings/{id}", ct), ct);
        var last = detail.GetProperty("history").EnumerateArray().Last();
        last.GetProperty("kind").GetString().ShouldBe("Rescheduled");
        last.GetProperty("actorType").GetString().ShouldBe("ShopUser");
        (await ScalarAsync(w.Factory, "SELECT count(*) FROM infra.outbox_messages WHERE type = 'booking.rescheduled' AND payload::text LIKE '%' || @p || '%'", ct, id.ToString()))
            .ShouldBe(1L);

        // A taken time is refused.
        await OkAsync(BookAsync(customer, w.SlugA, w.Haircut, w.Faisal, At(day, 15), ct), ct, HttpStatusCode.Created);
        var current = (await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/bookings/{id}", ct), ct)).GetProperty("booking").GetProperty("version").GetUInt32();
        await FailsAsync(RescheduleAsync(w.OwnerA, id, At(day, 15), current, ct), HttpStatusCode.Conflict, "booking.slot_unavailable", ct);

        // A barber moves their own booking, never a colleague's.
        using var faisal = await BarberLoginTests.BarberAsync(w, w.Faisal, "+966512340031", ct);
        await OkAsync(RescheduleAsync(faisal, id, At(day, 17), current, ct), ct);
        var omars = await OkAsync(BookAsync(customer, w.SlugA, w.Haircut, w.Omar, At(day, 11), ct), ct, HttpStatusCode.Created);
        var omarsId = omars.GetProperty("id").GetGuid();
        await FailsAsync(RescheduleAsync(faisal, omarsId, At(day, 18), omars.GetProperty("version").GetUInt32(), ct), HttpStatusCode.NotFound, null, ct);
        await FailsAsync(faisal.GetAsync($"/api/v1/shop/bookings/{omarsId}/reschedule/dates", ct), HttpStatusCode.NotFound, null, ct);
        await FailsAsync(w.OwnerB.GetAsync($"/api/v1/shop/bookings/{id}/reschedule/slots?date={Iso(day)}", ct), HttpStatusCode.NotFound, null, ct);
    }

    [Fact]
    public async Task ABookingGoesToAFreeColleague_TheCustomerMayThenCancelInsideTheWindow_UntilTheyChangeItThemselves()
    {
        var ct = TestContext.Current.CancellationToken;
        var day = TodayAt(DateTimeOffset.UtcNow).AddDays(2);
        await using var w = await ArrangeAsync(postgres, "shop_change_pro", ct);
        await WeekLongWindowAsync(w, ct);
        using var faisal = await BarberLoginTests.BarberAsync(w, w.Faisal, "+966512340041", ct);
        using var customer = await CustomerAsync(w.Factory, "خالد", ct);

        // Inside the (week-long) window, so the customer can no longer cancel it.
        var booking = await OkAsync(BookAsync(customer, w.SlugA, w.Haircut, w.Faisal, At(day, 9, 30), ct), ct, HttpStatusCode.Created);
        var id = booking.GetProperty("id").GetGuid();
        var mine = await OkAsync(customer.GetAsync($"/api/v1/me/bookings/{id}", ct), ct);
        mine.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()).ShouldNotContain("Cancel");

        // Omar is free then and does haircuts.
        var options = await OkAsync(faisal.GetAsync($"/api/v1/shop/bookings/{id}/professional-options", ct), ct);
        var omar = options.GetProperty("professionals").EnumerateArray().Single();
        omar.GetProperty("id").GetGuid().ShouldBe(w.Omar);
        omar.GetProperty("free").GetBoolean().ShouldBeTrue();

        await FailsAsync(ChangeAsync(faisal, id, w.Faisal, booking.GetProperty("version").GetUInt32(), ct), HttpStatusCode.BadRequest, "booking.same_professional", ct);
        await FailsAsync(ChangeAsync(faisal, id, w.ProB, booking.GetProperty("version").GetUInt32(), ct), HttpStatusCode.UnprocessableEntity, null, ct);

        // A colleague busy at that time cannot take it.
        using (var other = await CustomerAsync(w.Factory, "سارة", ct))
        {
            var taken = await OkAsync(BookAsync(other, w.SlugA, w.Haircut, w.Omar, At(day, 9, 30), ct), ct, HttpStatusCode.Created);
            (await OkAsync(faisal.GetAsync($"/api/v1/shop/bookings/{id}/professional-options", ct), ct))
                .GetProperty("professionals")[0].GetProperty("free").GetBoolean().ShouldBeFalse();
            await FailsAsync(ChangeAsync(faisal, id, w.Omar, booking.GetProperty("version").GetUInt32(), ct), HttpStatusCode.Conflict, "booking.professional_busy", ct);
            await OkAsync(w.OwnerA.PostAsync(
                $"/api/v1/shop/bookings/{taken.GetProperty("id").GetGuid()}/transitions",
                new { to = "CancelledByShop", reason = "إغلاق", version = taken.GetProperty("version").GetUInt32() }, ct), ct);
        }

        // Faisal gives it to Omar: it leaves Faisal's reach; the owner sees it with Omar; the history keeps who had it.
        var changed = await OkAsync(ChangeAsync(faisal, id, w.Omar, booking.GetProperty("version").GetUInt32(), ct), ct);
        changed.GetProperty("professional").GetProperty("id").GetGuid().ShouldBe(w.Omar);
        changed.GetProperty("startsAt").GetDateTimeOffset().ShouldBe(At(day, 9, 30));
        await FailsAsync(faisal.GetAsync($"/api/v1/shop/bookings/{id}", ct), HttpStatusCode.NotFound, null, ct);
        var detail = await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/bookings/{id}", ct), ct);
        var last = detail.GetProperty("history").EnumerateArray().Last();
        last.GetProperty("kind").GetString().ShouldBe("ProfessionalChanged");
        last.GetProperty("previousProfessional").GetProperty("id").GetGuid().ShouldBe(w.Faisal);
        var payload = (string)(await ScalarAsync(w.Factory, "SELECT payload::text FROM infra.outbox_messages WHERE type = 'booking.professional_changed'", ct))!;
        JsonDocument.Parse(payload).RootElement.GetProperty("previousProfessionalId").GetGuid().ShouldBe(w.Faisal);

        // The customer may now cancel or change it, inside the window, until it starts.
        mine = await OkAsync(customer.GetAsync($"/api/v1/me/bookings/{id}", ct), ct);
        mine.GetProperty("professional").GetProperty("id").GetGuid().ShouldBe(w.Omar);
        mine.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()).ShouldBe(["Cancel", "Reschedule"], ignoreOrder: true);
        mine.GetProperty("professionalChangedAt").ValueKind.ShouldBe(JsonValueKind.String, "the page says why the window is open");
        await OkAsync(customer.GetAsync($"/api/v1/me/bookings/{id}/reschedule/slots?date={Iso(day)}", ct), ct);

        // Changing it themselves uses that up: the window applies again to the time they chose.
        var version = mine.GetProperty("version").GetUInt32();
        var rescheduled = await OkAsync(
            customer.SendAsync(HttpMethod.Post, $"/api/v1/me/bookings/{id}/reschedule", new { startsAt = At(day, 10), professionalId = (Guid?)null, version }, ct, headers: Key()), ct);
        rescheduled.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()).ShouldNotContain("Cancel");
        rescheduled.GetProperty("professionalChangedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        await FailsAsync(customer.PostAsync($"/api/v1/me/bookings/{id}/cancel", new { version = rescheduled.GetProperty("version").GetUInt32() }, ct),
            HttpStatusCode.UnprocessableEntity, "booking.cancellation_cutoff_passed", ct);
    }

    [Fact]
    public async Task AfterAChangeOfBarber_TheCustomerCancelsInsideTheWindow()
    {
        var ct = TestContext.Current.CancellationToken;
        var day = TodayAt(DateTimeOffset.UtcNow).AddDays(2);
        await using var w = await ArrangeAsync(postgres, "shop_change_cancel", ct);
        await WeekLongWindowAsync(w, ct);
        using var customer = await CustomerAsync(w.Factory, "فهد", ct);
        var booking = await OkAsync(BookAsync(customer, w.SlugA, w.Haircut, w.Faisal, At(day, 9, 30), ct), ct, HttpStatusCode.Created);
        var id = booking.GetProperty("id").GetGuid();
        await FailsAsync(customer.PostAsync($"/api/v1/me/bookings/{id}/cancel", new { version = booking.GetProperty("version").GetUInt32() }, ct),
            HttpStatusCode.UnprocessableEntity, "booking.cancellation_cutoff_passed", ct);

        var changed = await OkAsync(ChangeAsync(w.OwnerA, id, w.Omar, booking.GetProperty("version").GetUInt32(), ct), ct);
        var version = (await OkAsync(customer.GetAsync($"/api/v1/me/bookings/{id}", ct), ct)).GetProperty("version").GetUInt32();
        var cancelled = await OkAsync(customer.PostAsync($"/api/v1/me/bookings/{id}/cancel", new { version }, ct), ct);
        cancelled.GetProperty("status").GetString().ShouldBe("CancelledByCustomer");
        changed.GetProperty("professional").GetProperty("id").GetGuid().ShouldBe(w.Omar);
    }
}
