using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.IntegrationTests.Identity;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.Modules.Administration.Domain;
using Trimme.Modules.Bookings.Domain;
using Trimme.Modules.Identity.Domain;
using static Trimme.IntegrationTests.Bookings.BookingTestData;

namespace Trimme.IntegrationTests.Bookings;

/// <summary>
/// R-BKG-01/02/05/06/07/08/09, R-SUB-05, R-NEG-04 (D-085…D-089): online bookings with idempotency, snapshot and outbox;
/// customer and shop isolation; walk-ins with the same collision checks; transitions with history; pause and expiry never
/// touch existing bookings; admin intervention.
/// </summary>
public sealed class BookingTests(PostgresFixture postgres)
{
    private static DateOnly Target => TodayAt(DateTimeOffset.UtcNow).AddDays(2);

    private static async Task<List<string>> OutboxAsync(TrimmeApiFactory factory, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
        return await db.Set<OutboxMessage>().AsNoTracking().OrderBy(m => m.OccurredAt).Select(m => m.Type + "|" + m.Payload).ToListAsync(ct);
    }

    private static async Task<string[]> CandidatesAsync(ApiSession session, string slug, Guid serviceId, DateTimeOffset start, CancellationToken ct)
    {
        var slots = await OkAsync(session.GetAsync($"/api/v1/public/shops/{slug}/availability/slots?serviceId={serviceId}&date={Iso(Target)}", ct), ct);
        return slots.GetProperty("slots").EnumerateArray()
            .Where(s => s.GetProperty("startsAt").GetDateTimeOffset() == start)
            .SelectMany(s => s.GetProperty("professionalIds").EnumerateArray().Select(p => p.GetString()!))
            .ToArray();
    }

    [Fact]
    public async Task OnlineBooking_IsIdempotent_KeepsItsSnapshot_WritesTheOutbox_AndCanBeCancelled()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "bkg_flow", ct);
        using var noura = await CustomerAsync(w.Factory, "نورة", ct);
        using var anonymous = ApiSession.Create(w.Factory);
        var ten = At(Target, 10);

        // R-BKG-05: the key is required, a replay returns the same booking, a reused key with another body is refused.
        await FailsAsync(noura.PostAsync("/api/v1/bookings", new { shopSlug = w.SlugA, serviceId = w.Haircut, startsAt = ten }, ct), HttpStatusCode.BadRequest, "idempotency.key_required", ct);
        using var first = await BookAsync(noura, w.SlugA, w.Haircut, null, ten, ct, key: "k-1");
        var firstBody = await first.Content.ReadAsStringAsync(ct);
        first.StatusCode.ShouldBe(HttpStatusCode.Created, firstBody);
        var booking = JsonDocument.Parse(firstBody).RootElement;
        var id = booking.GetProperty("id").GetGuid();
        booking.GetProperty("status").GetString().ShouldBe("Confirmed", "auto-confirm by default (D-006)");
        booking.GetProperty("item").GetProperty("price").GetDecimal().ShouldBe(60m);
        booking.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()).ShouldBe(["Cancel", "Reschedule"]);
        var chosen = booking.GetProperty("professional").GetProperty("id").GetGuid();
        chosen.ShouldBe(new[] { w.Faisal, w.Omar }.Min(), "both are free with no bookings that day: the stable id order decides (D-012)");
        firstBody.ShouldNotContain("+966");

        using (var replay = await BookAsync(noura, w.SlugA, w.Haircut, null, ten, ct, key: "k-1"))
        {
            replay.StatusCode.ShouldBe(HttpStatusCode.Created);
            replay.Headers.GetValues("Idempotent-Replayed").ShouldBe(["true"]);
            (await replay.JsonAsync(ct)).GetProperty("id").GetGuid().ShouldBe(id);
        }

        await FailsAsync(BookAsync(noura, w.SlugA, w.Haircut, null, ten.AddMinutes(30), ct, key: "k-1"), HttpStatusCode.UnprocessableEntity, "idempotency.key_reused", ct);

        // The next "any professional" booking at the same time goes to the other professional; the slot then has none.
        (await CandidatesAsync(anonymous, w.SlugA, w.Haircut, ten, ct)).ShouldBe([(chosen == w.Faisal ? w.Omar : w.Faisal).ToString()]);
        await FailsAsync(BookAsync(noura, w.SlugA, w.Haircut, chosen, ten.AddMinutes(15), ct), HttpStatusCode.Conflict, "booking.slot_unavailable", ct);

        // The shop sees the customer's name, never a phone (R-NEG-04), and the history.
        var list = await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/bookings?from={Iso(Target)}&to={Iso(Target)}", ct), ct);
        list.GetProperty("total").GetInt32().ShouldBe(1);
        list.GetProperty("items")[0].GetProperty("customerName").GetString().ShouldBe("نورة");
        list.ToString().ShouldNotContain("+966");
        var detail = await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/bookings/{id}", ct), ct);
        detail.GetProperty("history")[0].GetProperty("kind").GetString().ShouldBe("Created");
        (await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/bookings?search={booking.GetProperty("reference").GetString()}", ct), ct)).GetProperty("total").GetInt32().ShouldBe(1);

        // R-BKG-08: one outbox row in the same transaction, ids only.
        var outbox = await OutboxAsync(w.Factory, ct);
        outbox.ShouldHaveSingleItem().ShouldStartWith("booking.created|");
        outbox[0].ShouldContain(id.ToString());
        outbox[0].ShouldNotContain("+966");
        outbox[0].ShouldNotContain("نورة");

        // R-BKG-01: editing the service never rewrites the booking; a booked service cannot be deleted (R-SVC-02).
        var service = await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/services/{w.Haircut}", ct), ct);
        await OkAsync(w.OwnerA.PutAsync($"/api/v1/shop/services/{w.Haircut}", new
        {
            nameAr = "حلاقة فاخرة", price = 99m, durationMinutes = 45, onlineBookable = true, version = service.GetProperty("version").GetUInt32(),
        }, ct), ct);
        var mine = await OkAsync(noura.GetAsync($"/api/v1/me/bookings/{id}", ct), ct);
        mine.GetProperty("item").GetProperty("nameAr").GetString().ShouldBe("حلاقة");
        mine.GetProperty("item").GetProperty("price").GetDecimal().ShouldBe(60m);
        mine.GetProperty("item").GetProperty("durationMinutes").GetInt32().ShouldBe(30);

        using (var calendar = await noura.GetAsync($"/api/v1/me/bookings/{id}/calendar.ics", ct))
        {
            calendar.Content.Headers.ContentType!.MediaType.ShouldBe("text/calendar");
            var text = await calendar.Content.ReadAsStringAsync(ct);
            text.ShouldContain("BEGIN:VEVENT");
            text.ShouldContain(booking.GetProperty("reference").GetString()!);
        }

        (await OkAsync(noura.GetAsync("/api/v1/me/bookings?tab=Upcoming", ct), ct)).GetProperty("total").GetInt32().ShouldBe(1);

        // R-BKG-09: cancel within the cutoff frees the time and writes a cancellation event.
        var cancelled = await OkAsync(noura.PostAsync($"/api/v1/me/bookings/{id}/cancel", new { reason = "سفر", version = mine.GetProperty("version").GetUInt32() }, ct), ct);
        cancelled.GetProperty("status").GetString().ShouldBe("CancelledByCustomer");
        cancelled.GetProperty("allowedActions").GetArrayLength().ShouldBe(0);
        (await CandidatesAsync(anonymous, w.SlugA, w.Haircut, ten, ct)).Length.ShouldBe(2);
        (await OutboxAsync(w.Factory, ct))[^1].ShouldStartWith("booking.cancelled|");
        (await OkAsync(noura.GetAsync("/api/v1/me/bookings?tab=Past", ct), ct)).GetProperty("total").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task ServiceInBookingHistory_CannotBeDeleted_OnlyArchived()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "bkg_usage", ct);
        using var noura = await CustomerAsync(w.Factory, "نورة", ct);
        await OkAsync(BookAsync(noura, w.SlugA, w.Beard, w.Faisal, At(Target, 11), ct), ct, HttpStatusCode.Created);

        // The beard service is also in the package; drop that dependency so only the booking holds it.
        var package = await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/packages/{w.Package}", ct), ct);
        await OkAsync(w.OwnerA.PostAsync($"/api/v1/shop/packages/{w.Package}/archive", null, ct), ct);
        package.GetProperty("id").GetGuid().ShouldBe(w.Package);
        await FailsAsync(w.OwnerA.DeleteAsync($"/api/v1/shop/services/{w.Beard}", ct), HttpStatusCode.Conflict, "service.in_use", ct);
        await OkAsync(w.OwnerA.PostAsync($"/api/v1/shop/services/{w.Beard}/archive", null, ct), ct);
    }

    [Fact]
    public async Task ServiceKeptOnlyInABookedPackageSnapshot_IsStillInUse()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "bkg_usage_pkg", ct);
        using var noura = await CustomerAsync(w.Factory, "نورة", ct);
        await OkAsync(BookAsync(noura, w.SlugA, w.Haircut, w.Faisal, At(Target, 11), ct, packageId: w.Package), ct, HttpStatusCode.Created);

        // The package drops the beard service; only the booked snapshot still names it.
        var facial = (await OkAsync(w.OwnerA.PostAsync("/api/v1/shop/services", new { nameAr = "وجه", price = 40m, durationMinutes = 20, onlineBookable = true }, ct), ct, HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        var package = await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/packages/{w.Package}", ct), ct);
        await OkAsync(w.OwnerA.PutAsync($"/api/v1/shop/packages/{w.Package}", new
        {
            nameAr = "باقة", price = 80m, durationMinutes = 60, serviceIds = new[] { w.Haircut, facial }, version = package.GetProperty("version").GetUInt32(),
        }, ct), ct);
        await FailsAsync(w.OwnerA.DeleteAsync($"/api/v1/shop/services/{w.Beard}", ct), HttpStatusCode.Conflict, "service.in_use", ct);
    }

    [Fact]
    public async Task Customers_SeeAndChangeOnlyTheirOwnBookings_ShopsOnlyTheirOwn()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "bkg_isolation", ct);
        using var noura = await CustomerAsync(w.Factory, "نورة", ct);
        using var khalid = await CustomerAsync(w.Factory, "خالد", ct);
        var booking = await OkAsync(BookAsync(noura, w.SlugA, w.Haircut, w.Faisal, At(Target, 12), ct), ct, HttpStatusCode.Created);
        var id = booking.GetProperty("id").GetGuid();
        var version = booking.GetProperty("version").GetUInt32();

        await FailsAsync(khalid.GetAsync($"/api/v1/me/bookings/{id}", ct), HttpStatusCode.NotFound, "booking.not_found", ct);
        await FailsAsync(khalid.PostAsync($"/api/v1/me/bookings/{id}/cancel", new { version }, ct), HttpStatusCode.NotFound, null, ct);
        await FailsAsync(khalid.SendAsync(HttpMethod.Post, $"/api/v1/me/bookings/{id}/reschedule", new { startsAt = At(Target, 14), version }, ct, headers: Key()), HttpStatusCode.NotFound, null, ct);
        await FailsAsync(khalid.GetAsync($"/api/v1/me/bookings/{id}/calendar.ics", ct), HttpStatusCode.NotFound, null, ct);
        (await OkAsync(khalid.GetAsync("/api/v1/me/bookings", ct), ct)).GetProperty("total").GetInt32().ShouldBe(0);

        // Shop B guesses shop A's booking id.
        await FailsAsync(w.OwnerB.GetAsync($"/api/v1/shop/bookings/{id}", ct), HttpStatusCode.NotFound, null, ct);
        await FailsAsync(w.OwnerB.PostAsync($"/api/v1/shop/bookings/{id}/transitions", new { to = "CancelledByShop", reason = "guess", version }, ct), HttpStatusCode.NotFound, null, ct);
        await FailsAsync(w.OwnerB.PostAsync($"/api/v1/shop/bookings/{id}/notes", new { text = "x" }, ct), HttpStatusCode.NotFound, null, ct);
        (await OkAsync(w.OwnerB.GetAsync("/api/v1/shop/bookings", ct), ct)).GetProperty("total").GetInt32().ShouldBe(0);
        await FailsAsync(w.OwnerB.PostAsync("/api/v1/shop/bookings/walk-in", new { serviceId = w.Haircut, professionalId = w.Faisal, customerName = "x" }, ct), HttpStatusCode.NotFound, null, ct);

        // Customers and shops cannot use each other's endpoints.
        (await noura.GetAsync("/api/v1/shop/bookings", ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await w.OwnerA.GetAsync("/api/v1/me/bookings", ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await w.OwnerA.SendAsync(HttpMethod.Post, "/api/v1/bookings", new { shopSlug = w.SlugA, serviceId = w.Haircut, startsAt = At(Target, 15) }, ct, headers: Key())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // D-085, in the data layer: a customer context sees only its own rows and cannot write another customer's.
        await using var scope = w.Factory.Services.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<TrimmeDbContext>>();
        var contributors = scope.ServiceProvider.GetRequiredService<IEnumerable<IModelContributor>>();
        var khalidId = (await OkAsync(khalid.GetAsync("/api/v1/me", ct), ct)).GetProperty("id").GetGuid();
        await using var asKhalid = new TrimmeDbContext(options, contributors, tenant: null, new FixedCustomer(khalidId));
        (await asKhalid.Set<Booking>().CountAsync(ct)).ShouldBe(0);
        var stolen = await asKhalid.Set<Booking>().IgnoreQueryFilters().SingleAsync(b => b.Id == new BookingId(id), ct);
        stolen.CancelByCustomer(khalidId, null, DateTimeOffset.UtcNow, 0);
        await Should.ThrowAsync<TenantViolationException>(() => asKhalid.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task WalkIns_UseTheSameCollisionChecks_TransitionsFollowTheStateMachine_AndSchedulesFlagBookings()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var w = await ArrangeAsync(postgres, "bkg_walkin", ct, clock);

        // Jump to 10:00 on the target day (the clock only moves forward), then sign in again.
        clock.SetUtcNow(At(Target, 10).ToUniversalTime());
        using var owner = await IdentityTestData.SignInStaffAsync(w.Factory, w.Shops.A.OwnerEmail, ct);
        using var staff = await IdentityTestData.SignInStaffAsync(w.Factory, w.Shops.A.StaffEmail, ct);

        var now = await OkAsync(owner.PostAsync("/api/v1/shop/bookings/walk-in", new { serviceId = w.Haircut, professionalId = w.Faisal, customerName = "زائر" }, ct), ct, HttpStatusCode.Created);
        now.GetProperty("status").GetString().ShouldBe("Arrived", "a walk-in starting now (D-035)");
        now.GetProperty("channel").GetString().ShouldBe("WalkIn");
        now.GetProperty("allowedTransitions").EnumerateArray().Select(t => t.GetString()).ShouldBe(["Completed"]);

        // R-BKG-07: an overlapping walk-in is refused; an off-grid one after it is fine.
        await FailsAsync(staff.PostAsync("/api/v1/shop/bookings/walk-in", new { serviceId = w.Haircut, professionalId = w.Faisal, startsAt = At(Target, 10, 10), customerName = "آخر" }, ct),
            HttpStatusCode.Conflict, "booking.slot_unavailable", ct);
        var later = await OkAsync(staff.PostAsync("/api/v1/shop/bookings/walk-in", new { serviceId = w.Haircut, professionalId = w.Faisal, startsAt = At(Target, 10, 32), customerName = "آخر" }, ct),
            ct, HttpStatusCode.Created);
        later.GetProperty("status").GetString().ShouldBe("Confirmed");
        await FailsAsync(owner.PostAsync("/api/v1/shop/bookings/walk-in", new { serviceId = w.Haircut, professionalId = w.Faisal, startsAt = At(Target, 9), customerName = "x" }, ct),
            HttpStatusCode.BadRequest, "validation.date_in_past", ct);
        await FailsAsync(owner.PostAsync("/api/v1/shop/bookings/walk-in", new { serviceId = w.Haircut, professionalId = w.Omar, startsAt = At(Target, 21), customerName = "x" }, ct),
            HttpStatusCode.Conflict, "booking.slot_unavailable", ct);

        // Transitions (R-BKG-02): complete the arrived one; refuse what the machine forbids; time rules; reason required.
        var nowId = now.GetProperty("id").GetGuid();
        var completed = await OkAsync(staff.PostAsync($"/api/v1/shop/bookings/{nowId}/transitions", new { to = "Completed", version = now.GetProperty("version").GetUInt32() }, ct), ct);
        completed.GetProperty("status").GetString().ShouldBe("Completed");
        await FailsAsync(staff.PostAsync($"/api/v1/shop/bookings/{nowId}/transitions", new { to = "Arrived", version = completed.GetProperty("version").GetUInt32() }, ct),
            HttpStatusCode.Conflict, "booking.invalid_transition", ct);
        var laterId = later.GetProperty("id").GetGuid();
        var laterVersion = later.GetProperty("version").GetUInt32();
        await FailsAsync(staff.PostAsync($"/api/v1/shop/bookings/{laterId}/transitions", new { to = "NoShow", version = laterVersion }, ct), HttpStatusCode.UnprocessableEntity, "booking.too_early", ct);
        await FailsAsync(staff.PostAsync($"/api/v1/shop/bookings/{laterId}/transitions", new { to = "CancelledByShop", version = laterVersion }, ct), HttpStatusCode.BadRequest, "validation.reason_required", ct);
        await FailsAsync(staff.PostAsync($"/api/v1/shop/bookings/{laterId}/transitions", new { to = "CancelledByShop", reason = "stale", version = laterVersion - 1 }, ct), HttpStatusCode.Conflict, null, ct);
        var noted = await OkAsync(staff.PostAsync($"/api/v1/shop/bookings/{laterId}/notes", new { text = "يفضّل المقص" }, ct), ct, HttpStatusCode.Created);
        noted.GetProperty("text").GetString().ShouldBe("يفضّل المقص");

        // DV-S22: time off over a booking flags it for the shop; nothing is cancelled.
        await OkAsync(owner.PostAsync("/api/v1/shop/schedule/time-off", new { professionalId = w.Faisal, kind = "Sick", startDate = Iso(Target), endDate = Iso(Target), startMinute = 630, endMinute = 720 }, ct),
            ct, HttpStatusCode.Created);
        var flagged = await OkAsync(owner.GetAsync($"/api/v1/shop/bookings/{laterId}", ct), ct);
        flagged.GetProperty("booking").GetProperty("outsideSchedule").GetBoolean().ShouldBeTrue();
        flagged.GetProperty("booking").GetProperty("status").GetString().ShouldBe("Confirmed");
        flagged.GetProperty("notes").GetArrayLength().ShouldBe(1);
        flagged.GetProperty("history").GetArrayLength().ShouldBe(1);

        // Arrived is allowed from an hour before; a no-show after the start.
        clock.SetUtcNow(At(Target, 10, 40).ToUniversalTime());
        using var staffLater = await IdentityTestData.SignInStaffAsync(w.Factory, w.Shops.A.StaffEmail, ct); // the access cookie is short-lived
        var arrived = await OkAsync(staffLater.PostAsync($"/api/v1/shop/bookings/{laterId}/transitions", new { to = "NoShow", version = laterVersion }, ct), ct);
        arrived.GetProperty("status").GetString().ShouldBe("NoShow");
        (await OutboxAsync(w.Factory, ct)).Select(m => m.Split('|')[0]).ShouldBe(["booking.created", "booking.created", "booking.status_changed", "booking.status_changed"]);
    }

    [Fact]
    public async Task PauseAndExpiry_NeverTouchExistingBookings_ButBlockNewOnesAndReschedules()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "bkg_gates", ct);
        using var noura = await CustomerAsync(w.Factory, "نورة", ct);
        var booking = await OkAsync(BookAsync(noura, w.SlugA, w.Haircut, w.Faisal, At(Target, 13), ct), ct, HttpStatusCode.Created);
        var id = booking.GetProperty("id").GetGuid();

        await OkAsync(w.OwnerA.PostAsync("/api/v1/shop/online-booking/pause", new { }, ct), ct);
        await FailsAsync(BookAsync(noura, w.SlugA, w.Haircut, null, At(Target, 14), ct), HttpStatusCode.UnprocessableEntity, "shop.paused", ct);
        await FailsAsync(noura.SendAsync(HttpMethod.Post, $"/api/v1/me/bookings/{id}/reschedule", new { startsAt = At(Target, 14), version = booking.GetProperty("version").GetUInt32() }, ct, headers: Key()),
            HttpStatusCode.UnprocessableEntity, "booking.shop_not_accepting", ct);
        (await OkAsync(noura.GetAsync($"/api/v1/me/bookings/{id}", ct), ct)).GetProperty("status").GetString().ShouldBe("Confirmed");
        await OkAsync(w.OwnerA.PostAsync("/api/v1/shop/online-booking/resume", null, ct), ct);

        // R-SUB-05: a suspended subscription blocks new online bookings only; walk-ins still work (D-014).
        var subscription = await OkAsync(w.Admin.GetAsync($"/api/v1/admin/shops/{w.Shops.A.ShopId}/subscription", ct), ct);
        await OkAsync(w.Admin.PostAsync($"/api/v1/admin/shops/{w.Shops.A.ShopId}/subscription/suspend", new { reason = "Payment overdue", version = subscription.GetProperty("version").GetUInt32() }, ct), ct);
        await FailsAsync(BookAsync(noura, w.SlugA, w.Haircut, null, At(Target, 15), ct), HttpStatusCode.UnprocessableEntity, "subscription.suspended", ct);
        var kept = await OkAsync(noura.GetAsync($"/api/v1/me/bookings/{id}", ct), ct);
        kept.GetProperty("status").GetString().ShouldBe("Confirmed");
        kept.GetProperty("startsAt").GetDateTimeOffset().ShouldBe(At(Target, 13));
        await OkAsync(w.OwnerA.PostAsync("/api/v1/shop/bookings/walk-in", new { serviceId = w.Haircut, professionalId = w.Omar, startsAt = At(Target, 16), customerName = "زائر" }, ct), ct, HttpStatusCode.Created);

        // Cancelling stays possible for the customer.
        await OkAsync(noura.PostAsync($"/api/v1/me/bookings/{id}/cancel", new { version = kept.GetProperty("version").GetUInt32() }, ct), ct);
    }

    [Fact]
    public async Task Reschedule_MovesToAnotherOfferedSlot_KeepsTheSnapshot_AndIsIdempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "bkg_reschedule", ct);
        using var noura = await CustomerAsync(w.Factory, "نورة", ct);
        using var khalid = await CustomerAsync(w.Factory, "خالد", ct);
        var booking = await OkAsync(BookAsync(noura, w.SlugA, w.Haircut, w.Faisal, At(Target, 10), ct), ct, HttpStatusCode.Created);
        await OkAsync(BookAsync(khalid, w.SlugA, w.Haircut, w.Faisal, At(Target, 11), ct), ct, HttpStatusCode.Created);
        var id = booking.GetProperty("id").GetGuid();
        var version = booking.GetProperty("version").GetUInt32();
        var key = Key("move-1");

        await FailsAsync(noura.SendAsync(HttpMethod.Post, $"/api/v1/me/bookings/{id}/reschedule", new { startsAt = At(Target, 11, 15), version }, ct, headers: Key()), HttpStatusCode.Conflict, "booking.slot_unavailable", ct);
        await FailsAsync(noura.SendAsync(HttpMethod.Post, $"/api/v1/me/bookings/{id}/reschedule", new { startsAt = At(Target, 10, 3), version }, ct, headers: Key()), HttpStatusCode.Conflict, "booking.slot_unavailable", ct);

        // Moving by 15 minutes overlaps its own current time: its own booking does not block it.
        var moved = await OkAsync(noura.SendAsync(HttpMethod.Post, $"/api/v1/me/bookings/{id}/reschedule", new { startsAt = At(Target, 10, 15), professionalId = w.Omar, version }, ct, headers: key), ct);
        moved.GetProperty("startsAt").GetDateTimeOffset().ShouldBe(At(Target, 10, 15));
        moved.GetProperty("professional").GetProperty("id").GetGuid().ShouldBe(w.Omar);
        moved.GetProperty("item").GetProperty("price").GetDecimal().ShouldBe(60m);
        using (var replay = await noura.SendAsync(HttpMethod.Post, $"/api/v1/me/bookings/{id}/reschedule", new { startsAt = At(Target, 10, 15), professionalId = w.Omar, version }, ct, headers: key))
        {
            replay.StatusCode.ShouldBe(HttpStatusCode.OK);
            replay.Headers.GetValues("Idempotent-Replayed").ShouldBe(["true"]);
        }

        var detail = await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/bookings/{id}", ct), ct);
        detail.GetProperty("history")[1].GetProperty("kind").GetString().ShouldBe("Rescheduled");
        detail.GetProperty("history")[1].GetProperty("previousStartsAt").GetDateTimeOffset().ShouldBe(At(Target, 10));
        (await OutboxAsync(w.Factory, ct))[^1].ShouldStartWith("booking.rescheduled|");
    }

    [Fact]
    public async Task ProfileIncomplete_OrUnpublishedItems_AreRefusedBeforeAnyBooking()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "bkg_refusals", ct);
        using var nameless = await IdentityTestData.SignInCustomerAsync(w.Factory, IdentityTestData.NewPhone(), ct);
        await FailsAsync(BookAsync(nameless, w.SlugA, w.Haircut, null, At(Target, 10), ct), HttpStatusCode.UnprocessableEntity, "booking.profile_incomplete", ct);

        using var noura = await CustomerAsync(w.Factory, "نورة", ct);
        await FailsAsync(BookAsync(noura, "no-such-shop", w.Haircut, null, At(Target, 10), ct), HttpStatusCode.NotFound, "shop.not_found", ct);
        await FailsAsync(BookAsync(noura, w.SlugA, w.ServiceB, null, At(Target, 10), ct), HttpStatusCode.NotFound, "booking.offer_not_found", ct);
        await FailsAsync(BookAsync(noura, w.SlugA, w.Haircut, w.ProB, At(Target, 10), ct), HttpStatusCode.NotFound, "professional.not_found", ct);
        await FailsAsync(BookAsync(noura, w.SlugA, w.Haircut, null, At(Target, 10, 2), ct), HttpStatusCode.Conflict, "booking.slot_unavailable", ct);
        await FailsAsync(BookAsync(noura, w.SlugA, w.Haircut, null, At(Target, 22), ct), HttpStatusCode.Conflict, "booking.slot_unavailable", ct);
        await FailsAsync(BookAsync(noura, w.SlugA, w.Haircut, null, DateTimeOffset.UtcNow.AddMinutes(10), ct), HttpStatusCode.Conflict, "booking.slot_unavailable", ct);
        // Text over its limit is a field error, never a database error.
        var longNote = new string('x', 501);
        await FailsAsync(noura.SendAsync(HttpMethod.Post, "/api/v1/bookings", new { shopSlug = w.SlugA, serviceId = w.Haircut, startsAt = At(Target, 10), note = longNote }, ct, headers: Key()),
            HttpStatusCode.BadRequest, "validation.too_long", ct);
        await FailsAsync(w.OwnerA.PostAsync("/api/v1/shop/bookings/walk-in", new { serviceId = w.Haircut, professionalId = w.Faisal, startsAt = At(Target, 12), customerName = "x", note = longNote }, ct),
            HttpStatusCode.BadRequest, "validation.too_long", ct);
        (await OutboxAsync(w.Factory, ct)).ShouldBeEmpty("a refused booking writes nothing");

        var booking = await OkAsync(BookAsync(noura, w.SlugA, w.Haircut, w.Faisal, At(Target, 10), ct), ct, HttpStatusCode.Created);
        var id = booking.GetProperty("id").GetGuid();
        var version = booking.GetProperty("version").GetUInt32();
        var longReason = new string('x', 301);
        await FailsAsync(noura.PostAsync($"/api/v1/me/bookings/{id}/cancel", new { reason = longReason, version }, ct), HttpStatusCode.BadRequest, "validation.too_long", ct);
        await FailsAsync(w.OwnerA.PostAsync($"/api/v1/shop/bookings/{id}/transitions", new { to = "CancelledByShop", reason = longReason, version }, ct), HttpStatusCode.BadRequest, "validation.too_long", ct);
        await FailsAsync(w.Admin.PostAsync($"/api/v1/admin/bookings/{id}/cancel", new { reason = longReason, version }, ct), HttpStatusCode.BadRequest, "validation.too_long", ct);
        (await OkAsync(noura.GetAsync($"/api/v1/me/bookings/{id}", ct), ct)).GetProperty("status").GetString().ShouldBe("Confirmed");
    }

    [Fact]
    public async Task Admins_ReadAcrossShops_AndCancelOnTheShopsBehalf_Audited()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "bkg_admin", ct);
        using var noura = await CustomerAsync(w.Factory, "نورة", ct);
        var booking = await OkAsync(BookAsync(noura, w.SlugA, w.Haircut, w.Faisal, At(Target, 17), ct), ct, HttpStatusCode.Created);
        var id = booking.GetProperty("id").GetGuid();

        using var support = await IdentityTestData.SignInNewStaffAsync(w.Factory, SystemRoles.Support, ct);
        var list = await OkAsync(support.GetAsync($"/api/v1/admin/bookings?shopId={w.Shops.A.ShopId}", ct), ct);
        list.GetProperty("total").GetInt32().ShouldBe(1);
        list.ToString().ShouldNotContain("+966");

        await FailsAsync(w.Admin.PostAsync($"/api/v1/admin/bookings/{id}/cancel", new { reason = "", version = booking.GetProperty("version").GetUInt32() }, ct), HttpStatusCode.BadRequest, null, ct);
        var cancelled = await OkAsync(w.Admin.PostAsync($"/api/v1/admin/bookings/{id}/cancel", new { reason = "Shop closed by the platform", version = booking.GetProperty("version").GetUInt32() }, ct), ct);
        cancelled.GetProperty("booking").GetProperty("status").GetString().ShouldBe("CancelledByShop");
        cancelled.GetProperty("history").EnumerateArray().Last().GetProperty("actorType").GetString().ShouldBe("PlatformAdmin");
        (await w.OwnerA.GetAsync("/api/v1/admin/bookings", ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await using var scope = w.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
        (await db.Set<AuditEntry>().AsNoTracking().Where(e => e.EntityId == id.ToString()).Select(e => e.Action).ToListAsync(ct)).ShouldBe(["booking.cancelled_by_admin"]);
    }

    [Fact]
    public async Task DemoSeed_AddsValidSampleBookings_Idempotently()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await TrimmeApiFactory.CreateMigratedAsync(postgres, "bkg_seed", ct);
        for (var run = 0; run < 2; run++)
        {
            await using var seedScope = factory.Services.CreateAsyncScope();
            foreach (var seeder in seedScope.ServiceProvider.GetServices<IDevSeeder>().OrderBy(s => s.Order))
            {
                await seeder.SeedAsync(seedScope.ServiceProvider, ct);
            }
        }

        await using var scope = factory.Services.CreateAsyncScope();
        using var system = scope.ServiceProvider.GetRequiredService<ISystemDataScope>().Begin();
        var bookings = await scope.ServiceProvider.GetRequiredService<TrimmeDbContext>().Set<Booking>().AsNoTracking().ToListAsync(ct);
        bookings.Select(b => b.Status).Order().ShouldBe(
            [BookingStatus.Confirmed, BookingStatus.Confirmed, BookingStatus.CancelledByCustomer, BookingStatus.NoShow, .. Enumerable.Repeat(BookingStatus.Completed, DemoVisits.All.Count)],
            ignoreOrder: true);
        bookings.ShouldAllBe(b => b.ProfessionalId != new ProfessionalId(Guid.Parse("0199a0de-5a10-7000-8000-000000000101")), "Faisal is kept free for the schedule E2E");
        bookings.Where(b => b.IsActive).ShouldAllBe(b => b.StartsAt > DateTimeOffset.UtcNow);
        bookings.Select(b => b.CustomerId).Distinct().ShouldBe(DemoCustomers.All.Select(c => (Guid?)c.Id), ignoreOrder: true);
    }

    private sealed class FixedCustomer(Guid id) : ICurrentCustomer
    {
        public Guid? CustomerId { get; } = id;
    }
}
