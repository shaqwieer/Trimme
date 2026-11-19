using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Trimme.IntegrationTests.Identity;
using Trimme.IntegrationTests.Infrastructure;
using static Trimme.IntegrationTests.Bookings.BookingTestData;

namespace Trimme.IntegrationTests.Bookings;

/// <summary>
/// D-151 (the client's comparison with the Groom Here app): the services a barber can add at the visit, each with the
/// barber's own length and whether it ends before their next booked or held time (the add's own rule); the ready
/// cancellation reasons and the owner's report by day and by reason; and the barber's «حسابي» — reading it, renaming
/// themselves everywhere, and deleting their login while the barber, their bookings and the owner's ability to make a new
/// login stay.
/// </summary>
public sealed class BarberDayAndAccountTests(PostgresFixture postgres)
{
    private static DateOnly Target => TodayAt(DateTimeOffset.UtcNow).AddDays(2);

    private static async Task<JsonElement> BookedAsync(ApiSession customer, BookingWorld w, Guid serviceId, Guid professionalId, DateTimeOffset start, CancellationToken ct) =>
        await OkAsync(BookAsync(customer, w.SlugA, serviceId, professionalId, start, ct), ct, HttpStatusCode.Created);

    private static Task<HttpResponseMessage> Transition(ApiSession session, Guid id, object body, CancellationToken ct) =>
        session.PostAsync($"/api/v1/shop/bookings/{id}/transitions", body, ct);

    private static async Task<uint> VersionAsync(ApiSession session, Guid id, CancellationToken ct) =>
        (await OkAsync(session.GetAsync($"/api/v1/shop/bookings/{id}", ct), ct)).GetProperty("booking").GetProperty("version").GetUInt32();

    [Fact]
    public async Task AddServiceCandidates_ShowTheBarbersLength_AndFitBeforeTheNextBooking_ByTheAddsRule()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var w = await ArrangeAsync(postgres, "d151_candidates", ct, clock);
        Guid Created(JsonElement service) => service.GetProperty("id").GetGuid();
        var trim = Created(await OkAsync(w.OwnerA.PostAsync("/api/v1/shop/services",
            new { nameAr = "تحديد", price = 15m, durationMinutes = 15, onlineBookable = true }, ct), ct, HttpStatusCode.Created));
        var wash = Created(await OkAsync(w.OwnerA.PostAsync("/api/v1/shop/services",
            new { nameAr = "غسيل", price = 25m, durationMinutes = 25, onlineBookable = true }, ct), ct, HttpStatusCode.Created));
        // Faisal takes 10 minutes for a trim; the price stays the shop's.
        await OkAsync(w.OwnerA.PutAsync($"/api/v1/shop/professionals/{w.Faisal}/services", new
        {
            services = new object[]
            {
                new { serviceId = w.Haircut, durationMinutes = (int?)null }, new { serviceId = w.Beard, durationMinutes = (int?)null },
                new { serviceId = trim, durationMinutes = (int?)10 }, new { serviceId = wash, durationMinutes = (int?)null },
            },
        }, ct), ct);
        await w.EnsureBarberLoginAsync(w.Faisal, ct);
        await w.EnsureBarberLoginAsync(w.Omar, ct);

        // 10:00 a 30-minute haircut; at 14:00 another customer's booking with Faisal, which he moves to 10:50 (D-155: at once).
        JsonElement visit, later;
        using (var customer = await CustomerAsync(w.Factory, "نورة", ct))
        {
            visit = await BookedAsync(customer, w, w.Haircut, w.Faisal, At(Target, 10), ct);
        }

        using (var other = await CustomerAsync(w.Factory, "سارة", ct))
        {
            later = await BookedAsync(other, w, w.Haircut, w.Faisal, At(Target, 14), ct);
        }

        var id = visit.GetProperty("id").GetGuid();
        clock.SetUtcNow(At(Target, 9, 0).ToUniversalTime());
        using var faisal = await w.BarberAsync(w.Faisal, ct);
        var url = $"/api/v1/shop/bookings/{id}/services/candidates";

        // The next booking is at 14:00: everything fits, each at Faisal's own length; the haircut is already on it.
        var open = await OkAsync(faisal.GetAsync(url, ct), ct);
        open.GetProperty("roomMinutes").GetInt32().ShouldBe(210);
        JsonElement Candidate(JsonElement list, Guid service) =>
            list.GetProperty("services").EnumerateArray().Single(s => s.GetProperty("serviceId").GetGuid() == service);
        open.GetProperty("services").EnumerateArray().Select(s => s.GetProperty("serviceId").GetGuid()).ShouldBe([w.Beard, trim, wash], ignoreOrder: true);
        Candidate(open, trim).GetProperty("durationMinutes").GetInt32().ShouldBe(10);
        Candidate(open, trim).GetProperty("price").GetDecimal().ShouldBe(15m);
        open.GetProperty("services").EnumerateArray().ShouldAllBe(s => s.GetProperty("fits").GetBoolean());

        // The 14:00 moved to 10:50: 20 minutes after the haircut. The beard (20) and the trim (10) fit, the wash (25) does not.
        await OkAsync(faisal.SendAsync(HttpMethod.Post, $"/api/v1/shop/bookings/{later.GetProperty("id").GetGuid()}/reschedule",
            new { startsAt = At(Target, 10, 50), version = later.GetProperty("version").GetUInt32() }, ct, headers: Key()), ct);
        var held = await OkAsync(faisal.GetAsync(url, ct), ct);
        held.GetProperty("roomMinutes").GetInt32().ShouldBe(20);
        Candidate(held, w.Beard).GetProperty("fits").GetBoolean().ShouldBeTrue();
        Candidate(held, trim).GetProperty("fits").GetBoolean().ShouldBeTrue();
        Candidate(held, wash).GetProperty("fits").GetBoolean().ShouldBeFalse();

        // The add follows the same rule: the wash is refused, the beard fills the time exactly; then nothing fits.
        clock.SetUtcNow(At(Target, 10, 0).ToUniversalTime());
        using var faisalNow = await w.BarberAsync(w.Faisal, ct);
        var arrived = await OkAsync(Transition(faisalNow, id, new { to = "Arrived", version = await VersionAsync(faisalNow, id, ct) }, ct), ct);
        await FailsAsync(faisalNow.PostAsync($"/api/v1/shop/bookings/{id}/services", new { serviceId = wash, version = arrived.GetProperty("version").GetUInt32() }, ct),
            HttpStatusCode.Conflict, "booking.professional_busy", ct);
        await OkAsync(faisalNow.PostAsync($"/api/v1/shop/bookings/{id}/services", new { serviceId = w.Beard, version = arrived.GetProperty("version").GetUInt32() }, ct), ct);
        var full = await OkAsync(faisalNow.GetAsync(url, ct), ct);
        full.GetProperty("roomMinutes").GetInt32().ShouldBe(0);
        full.GetProperty("services").EnumerateArray().Select(s => s.GetProperty("serviceId").GetGuid()).ShouldBe([trim, wash], ignoreOrder: true);
        full.GetProperty("services").EnumerateArray().ShouldAllBe(s => !s.GetProperty("fits").GetBoolean());

        // Another barber's booking, and the owner: as for the add.
        using var omar = await w.BarberAsync(w.Omar, ct);
        await FailsAsync(omar.GetAsync(url, ct), HttpStatusCode.NotFound, null, ct);
        using var owner = await IdentityTestData.SignInStaffAsync(w.Factory, w.Shops.A.OwnerEmail, ct);
        await FailsAsync(owner.GetAsync(url, ct), HttpStatusCode.Forbidden, null, ct);
    }

    [Fact]
    public async Task ReadyCancellationReasons_AreKept_AndTheReportCountsThem_WithEachDay()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var w = await ArrangeAsync(postgres, "d151_reasons", ct, clock);
        await w.EnsureBarberLoginAsync(w.Faisal, ct);
        JsonElement served, servedNextDay, unwell, closed, other, typed;
        // One active booking per customer (D-155): each booking is its own customer's.
        async Task<JsonElement> ByANewCustomerAsync(DateTimeOffset start)
        {
            using var someone = await CustomerAsync(w.Factory, "نورة", ct);
            return await BookedAsync(someone, w, w.Haircut, w.Faisal, start, ct);
        }

        using var customer = await CustomerAsync(w.Factory, "نورة", ct);
        served = await ByANewCustomerAsync(At(Target, 10));
        servedNextDay = await ByANewCustomerAsync(At(Target.AddDays(1), 10));
        unwell = await BookedAsync(customer, w, w.Haircut, w.Faisal, At(Target, 12), ct);
        closed = await ByANewCustomerAsync(At(Target, 13));
        other = await ByANewCustomerAsync(At(Target, 14));
        typed = await ByANewCustomerAsync(At(Target, 15));

        using (var faisal = await w.BarberAsync(w.Faisal, ct))
        {
            Guid Id(JsonElement b) => b.GetProperty("id").GetGuid();
            uint Version(JsonElement b) => b.GetProperty("version").GetUInt32();

            // A code is only for a cancellation; «سبب ثاني» needs the reason typed.
            await FailsAsync(Transition(faisal, Id(unwell), new { to = "NoShow", reasonCode = "Emergency", version = Version(unwell) }, ct),
                HttpStatusCode.BadRequest, "reasonCode", ct);
            await FailsAsync(Transition(faisal, Id(other), new { to = "CancelledByShop", reasonCode = "Other", version = Version(other) }, ct),
                HttpStatusCode.BadRequest, "reason", ct);

            // A ready reason needs no text.
            var cancelled = await OkAsync(Transition(faisal, Id(unwell), new { to = "CancelledByShop", reasonCode = "StaffUnwell", version = Version(unwell) }, ct), ct);
            cancelled.GetProperty("status").GetString().ShouldBe("CancelledByShop");
            cancelled.GetProperty("cancellationReasonCode").GetString().ShouldBe("StaffUnwell");
            cancelled.GetProperty("cancellationReason").ValueKind.ShouldBe(JsonValueKind.Null);
            await OkAsync(Transition(faisal, Id(closed), new { to = "CancelledByShop", reasonCode = "ShopClosed", reason = "انقطعت الكهرباء", version = Version(closed) }, ct), ct);
            await OkAsync(Transition(faisal, Id(other), new { to = "CancelledByShop", reasonCode = "Other", reason = "سافر الحلاق", version = Version(other) }, ct), ct);
            (await OkAsync(Transition(faisal, Id(typed), new { to = "CancelledByShop", reason = "العميل اتصل", version = Version(typed) }, ct), ct))
                .GetProperty("cancellationReasonCode").ValueKind.ShouldBe(JsonValueKind.Null);

            // The customer's booking carries the code too, for their own words.
            (await OkAsync(customer.GetAsync($"/api/v1/me/bookings/{Id(unwell)}", ct), ct)).GetProperty("cancellationReasonCode").GetString().ShouldBe("StaffUnwell");
        }

        // The two haircuts are served, one each day.
        foreach (var (booking, day) in new[] { (served, Target), (servedNextDay, Target.AddDays(1)) })
        {
            var id = booking.GetProperty("id").GetGuid();
            clock.SetUtcNow(At(day, 10).ToUniversalTime());
            using (var faisal = await w.BarberAsync(w.Faisal, ct))
            {
                await TransitionAsync(faisal, id, "Arrived", ct);
            }

            clock.SetUtcNow(At(day, 10, 25).ToUniversalTime());
            using (var faisal = await w.BarberAsync(w.Faisal, ct))
            {
                await TransitionAsync(faisal, id, "Completed", ct);
            }
        }

        clock.SetUtcNow(At(Target.AddDays(2), 9).ToUniversalTime());
        using var owner = await IdentityTestData.SignInStaffAsync(w.Factory, w.Shops.A.OwnerEmail, ct);
        var report = await OkAsync(owner.GetAsync($"/api/v1/shop/reports?from={Iso(Target)}&to={Iso(Target.AddDays(2))}", ct), ct);

        // Every date of the period, zeros included, over every booking.
        var days = report.GetProperty("days").EnumerateArray().ToList();
        days.Select(d => d.GetProperty("date").GetString()).ShouldBe([Iso(Target), Iso(Target.AddDays(1)), Iso(Target.AddDays(2))]);
        days[0].GetProperty("bookings").GetInt32().ShouldBe(5);
        days[0].GetProperty("completed").GetInt32().ShouldBe(1);
        days[0].GetProperty("cancelled").GetInt32().ShouldBe(4);
        days[0].GetProperty("expectedIncome").GetDecimal().ShouldBe(60m);
        days[1].GetProperty("bookings").GetInt32().ShouldBe(1);
        days[1].GetProperty("expectedIncome").GetDecimal().ShouldBe(60m);
        days[2].GetProperty("bookings").GetInt32().ShouldBe(0);
        days.Sum(d => d.GetProperty("expectedIncome").GetDecimal()).ShouldBe(report.GetProperty("shop").GetProperty("expectedIncome").GetDecimal());

        // The shop's cancellations by ready reason; the one typed without a code is in none.
        var reasons = report.GetProperty("cancellationReasons").EnumerateArray()
            .ToDictionary(r => r.GetProperty("code").GetString()!, r => r.GetProperty("count").GetInt32());
        reasons.ShouldBe(new Dictionary<string, int> { ["StaffUnwell"] = 1, ["Emergency"] = 0, ["ShopClosed"] = 1, ["Other"] = 1 });
    }

    [Fact]
    public async Task ABarbersAccount_ReadsMasked_RenamesEverywhere_AndDeletesTheLoginOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "d151_account", ct);
        const string number = "+966512340051";
        var faisal = await BarberLoginTests.BarberAsync(w, w.Faisal, number, ct);
        JsonElement visit;
        using (var customer = await CustomerAsync(w.Factory, "نورة", ct))
        {
            visit = await BookedAsync(customer, w, w.Haircut, w.Faisal, At(Target, 10), ct);
        }

        // «حسابي»: the barber's names and the number they sign in with, masked; they may rename and delete it.
        var account = await OkAsync(faisal.GetAsync("/api/v1/shop/account", ct), ct);
        account.GetProperty("nameAr").GetString().ShouldBe("فيصل");
        account.GetProperty("professionalId").GetGuid().ShouldBe(w.Faisal);
        account.GetProperty("canRename").GetBoolean().ShouldBeTrue();
        account.GetProperty("canDelete").GetBoolean().ShouldBeTrue();
        var masked = account.GetProperty("mobileMasked").GetString().ShouldNotBeNull();
        masked.ShouldNotContain("12340051");
        masked.ShouldEndWith("51");

        // The owner's account: their own, nothing to rename or delete here.
        var owners = await OkAsync(w.OwnerA.GetAsync("/api/v1/shop/account", ct), ct);
        owners.GetProperty("canRename").GetBoolean().ShouldBeFalse();
        owners.GetProperty("canDelete").GetBoolean().ShouldBeFalse();
        owners.GetProperty("email").GetString().ShouldBe(w.Shops.A.OwnerEmail);
        await FailsAsync(w.OwnerA.PutAsync("/api/v1/shop/account/name", new { nameAr = "مالك", version = 1u }, ct), HttpStatusCode.Forbidden, "account.barbers_only", ct);
        await FailsAsync(w.OwnerA.SendAsync(HttpMethod.Delete, "/api/v1/shop/account", null, ct), HttpStatusCode.Forbidden, "account.barbers_only", ct);

        // Renaming: the owner's rules (a name is required; a stale version is 409), then the name everywhere.
        var version = account.GetProperty("version").GetUInt32();
        await FailsAsync(faisal.PutAsync("/api/v1/shop/account/name", new { nameAr = " ", version }, ct), HttpStatusCode.BadRequest, "nameAr", ct);
        var renamed = await OkAsync(faisal.PutAsync("/api/v1/shop/account/name", new { nameAr = "فيصل الحربي", version }, ct), ct);
        renamed.GetProperty("nameAr").GetString().ShouldBe("فيصل الحربي");
        renamed.GetProperty("nameEn").GetString().ShouldBe("Faisal", "left empty, the English name stays");
        await FailsAsync(faisal.PutAsync("/api/v1/shop/account/name", new { nameAr = "فيصل", version }, ct), HttpStatusCode.Conflict, null, ct);
        (await OkAsync(faisal.GetAsync("/api/v1/me", ct), ct)).GetProperty("displayName").GetString().ShouldBe("فيصل الحربي");
        (await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/professionals/{w.Faisal}", ct), ct)).GetProperty("nameAr").GetString().ShouldBe("فيصل الحربي");

        // Deleting: the login only. Signed out at once; the barber, their booking and their number stay with the shop.
        using (var response = await faisal.SendAsync(HttpMethod.Delete, "/api/v1/shop/account", null, ct))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        await FailsAsync(faisal.GetAsync("/api/v1/me", ct), HttpStatusCode.Unauthorized, null, ct);
        faisal.Dispose();
        using (var again = ApiSession.Create(w.Factory))
        {
            await FailsAsync(BarberLoginTests.SignInAsync(again, number, BarberLoginTests.Chosen, ct), HttpStatusCode.Unauthorized, null, ct);
        }

        var login = await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/professionals/{w.Faisal}/login", ct), ct);
        login.GetProperty("enabled").GetBoolean().ShouldBeFalse();
        login.GetProperty("hasNumber").GetBoolean().ShouldBeTrue("the number is the barber's, kept by the shop");
        (await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/professionals/{w.Faisal}", ct), ct)).GetProperty("status").GetString().ShouldBe("Active");
        (await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/bookings/{visit.GetProperty("id").GetGuid()}", ct), ct))
            .GetProperty("booking").GetProperty("professional").GetProperty("id").GetGuid().ShouldBe(w.Faisal);

        // The owner is told, with the barber's name.
        var notice = (await OkAsync(w.OwnerA.GetAsync("/api/v1/shop/notifications", ct), ct)).GetProperty("items").EnumerateArray()
            .Single(n => n.GetProperty("kind").GetString() == "barber.account_deleted");
        notice.GetProperty("parameters").GetProperty("name").GetString().ShouldBe("فيصل الحربي");
        notice.GetProperty("parameters").GetProperty("professionalId").GetString().ShouldBe(w.Faisal.ToString());
        notice.ToString().ShouldNotContain("12340051");

        // The owner gives the barber a new login, which signs in with the same number.
        using var back = await BarberLoginTests.BarberAsync(w, w.Faisal, number, ct);
        (await OkAsync(back.GetAsync("/api/v1/shop/account", ct), ct)).GetProperty("nameAr").GetString().ShouldBe("فيصل الحربي");
        (await OkAsync(back.GetAsync("/api/v1/me", ct), ct)).GetProperty("professionalId").GetGuid().ShouldBe(w.Faisal);
    }
}
