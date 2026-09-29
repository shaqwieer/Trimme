using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.IntegrationTests.Identity;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.Modules.Administration.Domain;
using Trimme.Modules.Identity.Domain;
using static Trimme.IntegrationTests.Bookings.BookingTestData;

namespace Trimme.IntegrationTests.Administration;

/// <summary>
/// Phase 14 (R-AD-01/05/06/07/11/12, R-TEN-08, D-101…D-106): the overview KPIs on the platform calendar, booking
/// interventions, the customers directory and its audited phone reveal, reviews moderation, roles and staff with their
/// escalation guards, and the audit log.
/// </summary>
public sealed class AdminOperationsTests(PostgresFixture postgres)
{
    private static DateOnly Target => TodayAt(DateTimeOffset.UtcNow).AddDays(2);

    private static async Task<(ApiSession Session, string Phone)> NamedCustomerAsync(TrimmeApiFactory factory, string name, CancellationToken ct)
    {
        var phone = IdentityTestData.NewPhone();
        var session = await IdentityTestData.SignInCustomerAsync(factory, phone, ct);
        await OkAsync(session.PostAsync("/api/v1/auth/profile/complete", new { displayName = name, preferredLocale = "ar", termsAccepted = true }, ct), ct);
        return (session, phone);
    }

    private static async Task<JsonElement> ShopTransitionAsync(ApiSession staff, Guid bookingId, string to, CancellationToken ct, string? reason = null)
    {
        var current = await OkAsync(staff.GetAsync($"/api/v1/shop/bookings/{bookingId}", ct), ct);
        return await OkAsync(staff.PostAsync($"/api/v1/shop/bookings/{bookingId}/transitions", new { to, reason, version = current.GetProperty("booking").GetProperty("version").GetUInt32() }, ct), ct);
    }

    private static async Task<List<AuditEntry>> AuditAsync(TrimmeApiFactory factory, string entityId, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
        return await db.Set<AuditEntry>().AsNoTracking().Where(e => e.EntityId == entityId).OrderBy(e => e.Sequence).ToListAsync(ct);
    }

    private static async Task<Guid> RoleIdAsync(ApiSession admin, string name, CancellationToken ct) =>
        (await OkAsync(admin.GetAsync("/api/v1/admin/roles", ct), ct)).EnumerateArray().Single(r => r.GetProperty("name").GetString() == name).GetProperty("id").GetGuid();

    private static async Task<string[]> RolePermissionsAsync(ApiSession admin, string name, CancellationToken ct) =>
        [
            .. (await OkAsync(admin.GetAsync("/api/v1/admin/roles", ct), ct)).EnumerateArray().Single(r => r.GetProperty("name").GetString() == name)
                .GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!),
        ];

    [Fact]
    public async Task Overview_CountsThePlatformDay_RatesAndRankings_OnTheRiyadhCalendar()
    {
        var ct = TestContext.Current.CancellationToken;
        var day1 = TodayAt(DateTimeOffset.UtcNow).AddDays(1);
        var day2 = day1.AddDays(1);
        var clock = new FakeTimeProvider(At(day1, 8).ToUniversalTime());
        await using var w = await ArrangeAsync(postgres, "adm_overview", ct, clock);

        // Shop A opens 09:00–02:00, so it has bookings on both sides of Riyadh midnight (both on the same UTC date).
        var schedule = await OkAsync(w.OwnerA.GetAsync("/api/v1/shop/schedule", ct), ct);
        var lateHours = Enum.GetValues<DayOfWeek>().Select(d => new { day = d.ToString(), startMinute = 540, endMinute = 1560 }).ToArray();
        await OkAsync(w.OwnerA.PutAsync("/api/v1/shop/schedule/opening-hours",
            new { intervals = lateHours, version = schedule.GetProperty("openingHours").GetProperty("version").GetUInt32() }, ct), ct);

        // A categorised service, so popular services group by platform category (DV-S02).
        var hair = (await OkAsync(w.Admin.PostAsync("/api/v1/admin/service-categories", new { nameAr = "شعر", nameEn = "Hair", icon = "scissors", displayOrder = 1 }, ct), ct, HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        var cut = (await OkAsync(w.OwnerA.PostAsync("/api/v1/shop/services", new { nameAr = "قص", price = 50m, durationMinutes = 30, onlineBookable = true, categoryId = hair }, ct), ct, HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        foreach (var professional in new[] { w.Faisal, w.Omar })
        {
            await OkAsync(w.Admin.PutAsync($"/api/v1/admin/professionals/{professional}/services", new { serviceIds = new[] { w.Haircut, w.Beard, cut } }, ct), ct);
        }

        var (noura, _) = await NamedCustomerAsync(w.Factory, "نورة", ct);
        var (sara, _) = await NamedCustomerAsync(w.Factory, "سارة", ct);
        async Task<Guid> Book(ApiSession who, Guid service, Guid professional, DateTimeOffset start, string? slug = null, Guid? package = null) =>
            (await OkAsync(BookAsync(who, slug ?? w.SlugA, service, professional, start, ct, packageId: package), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var completed = await Book(noura, cut, w.Faisal, At(day1, 10));
        var noShow = await Book(noura, w.Haircut, w.Faisal, At(day1, 11));
        var cancelled = await Book(sara, w.Haircut, w.Omar, At(day1, 12), package: w.Package);
        var lateNight = await Book(sara, w.Haircut, w.Omar, At(day1, 23, 30));
        var afterMidnight = await Book(noura, w.Beard, w.Faisal, At(day2, 0, 30));
        var tomorrowAfternoon = await Book(sara, cut, w.Omar, At(day2, 15));
        var shopB = (await OkAsync(w.Admin.GetAsync($"/api/v1/admin/shops/{w.Shops.B.ShopId}", ct), ct)).GetProperty("slug").GetString()!;
        await Book(noura, w.ServiceB, w.ProB, At(day1, 14), shopB);
        noura.Dispose();
        sara.Dispose();

        // 01:00 on day 2 in Riyadh: day 1's visits are over.
        clock.SetUtcNow(At(day2, 1).ToUniversalTime());
        using var owner = await IdentityTestData.SignInStaffAsync(w.Factory, w.Shops.A.OwnerEmail, ct);
        await ShopTransitionAsync(owner, completed, "Arrived", ct);
        await ShopTransitionAsync(owner, completed, "Completed", ct);
        await ShopTransitionAsync(owner, noShow, "NoShow", ct);
        await ShopTransitionAsync(owner, cancelled, "CancelledByShop", ct, "Barber unwell");
        await ShopTransitionAsync(owner, lateNight, "Arrived", ct);
        await ShopTransitionAsync(owner, lateNight, "Completed", ct);

        using var support = await IdentityTestData.SignInNewStaffAsync(w.Factory, SystemRoles.Support, ct);
        var today = await OkAsync(support.GetAsync("/api/v1/admin/dashboard/overview", ct), ct);
        today.GetProperty("today").GetString().ShouldBe(Iso(day2));
        today.GetProperty("appointmentsToday").GetInt32().ShouldBe(2, "00:30 and 15:00 on day 2 (the 00:30 visit is day 2 in Riyadh, day 1 in UTC)");
        today.GetProperty("appointmentsYesterday").GetInt32().ShouldBe(4, "the cancellation is left out");
        today.GetProperty("period").GetProperty("total").GetInt32().ShouldBe(1);
        today.GetProperty("period").GetProperty("completionRate").GetDecimal().ShouldBe(0m);

        var week = await OkAsync(support.GetAsync("/api/v1/admin/dashboard/overview?days=7", ct), ct);
        var period = week.GetProperty("period");
        period.GetProperty("total").GetInt32().ShouldBe(6, "every booking that started so far; the 15:00 one has not");
        period.GetProperty("completed").GetInt32().ShouldBe(2);
        period.GetProperty("cancelled").GetInt32().ShouldBe(1);
        period.GetProperty("noShows").GetInt32().ShouldBe(1);
        period.GetProperty("completionRate").GetDecimal().ShouldBe(33.3m);
        period.GetProperty("cancellationRate").GetDecimal().ShouldBe(16.7m);
        period.GetProperty("noShowRate").GetDecimal().ShouldBe(16.7m);
        week.GetProperty("previous").GetProperty("total").GetInt32().ShouldBe(0);
        week.GetProperty("shops").GetProperty("active").GetInt32().ShouldBe(2);
        week.GetProperty("professionals").GetProperty("active").GetInt32().ShouldBe(3);
        week.GetProperty("professionals").GetProperty("addedSince").GetInt32().ShouldBe(3);
        week.GetProperty("newCustomers").GetInt32().ShouldBe(2);
        week.GetProperty("newCustomersPrevious").GetInt32().ShouldBe(0);

        var trend = week.GetProperty("trend").EnumerateArray().ToList();
        trend.Count.ShouldBe(14);
        trend[^1].GetProperty("date").GetString().ShouldBe(Iso(day2));
        trend[^2].GetProperty("completed").GetInt32().ShouldBe(2);
        trend[^2].GetProperty("cancelledOrNoShow").GetInt32().ShouldBe(2);
        trend[^2].GetProperty("other").GetInt32().ShouldBe(1, "shop B's confirmed visit");
        trend[^1].GetProperty("other").GetInt32().ShouldBe(2);

        var popular = week.GetProperty("popularCategories").EnumerateArray().ToList();
        popular.Select(p => p.GetProperty("kind").GetString()).ShouldBe(["Uncategorised", "Category"]);
        popular[0].GetProperty("bookings").GetInt32().ShouldBe(4, "the cancelled package and the future booking are not counted");
        popular[1].GetProperty("nameEn").GetString().ShouldBe("Hair");
        popular[1].GetProperty("bookings").GetInt32().ShouldBe(1);

        var top = week.GetProperty("topShops").EnumerateArray().ToList();
        top.Select(s => s.GetProperty("shopId").GetGuid()).ShouldBe([w.Shops.A.ShopId, w.Shops.B.ShopId]);
        top[0].GetProperty("bookings").GetInt32().ShouldBe(5);
        top[0].GetProperty("cancellationRate").GetDecimal().ShouldBe(20m);

        await FailsAsync(support.GetAsync("/api/v1/admin/dashboard/overview?days=3", ct), HttpStatusCode.BadRequest, "validation.out_of_range", ct);
        (await owner.GetAsync("/api/v1/admin/dashboard/overview", ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The customers directory shows the same figures per customer, never the number.
        var customers = await OkAsync(support.GetAsync("/api/v1/admin/customers?search=نورة", ct), ct);
        customers.GetProperty("total").GetInt32().ShouldBe(1);
        var nouraRow = customers.GetProperty("items")[0];
        nouraRow.GetProperty("bookings").GetInt32().ShouldBe(4);
        nouraRow.GetProperty("upcoming").GetInt32().ShouldBe(0);
        nouraRow.GetProperty("lastBookingAt").GetDateTimeOffset().ShouldBe(At(day2, 0, 30));
        customers.ToString().ShouldNotContain("+966");

        var saraId = (await OkAsync(support.GetAsync("/api/v1/admin/customers?search=سارة", ct), ct)).GetProperty("items")[0].GetProperty("id").GetGuid();
        var saraDetail = await OkAsync(support.GetAsync($"/api/v1/admin/customers/{saraId}", ct), ct);
        saraDetail.GetProperty("bookings").GetInt32().ShouldBe(3);
        saraDetail.GetProperty("completed").GetInt32().ShouldBe(1);
        saraDetail.GetProperty("cancelled").GetInt32().ShouldBe(1);
        saraDetail.GetProperty("upcoming").GetInt32().ShouldBe(1);
        saraDetail.GetProperty("nextBookingAt").GetDateTimeOffset().ShouldBe(At(day2, 15));
        var saraBookings = await OkAsync(support.GetAsync($"/api/v1/admin/bookings?customerId={saraId}&sort=asc", ct), ct);
        saraBookings.GetProperty("items").EnumerateArray().Select(b => b.GetProperty("booking").GetProperty("id").GetGuid()).ShouldBe([cancelled, lateNight, tomorrowAfternoon]);
        afterMidnight.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task Bookings_AdminInterventions_TransitionAndReschedule_AreGuardedAudited_AndGoThroughTheOutbox()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "adm_bookings", ct);
        var (noura, _) = await NamedCustomerAsync(w.Factory, "نورة", ct);
        var booking = await OkAsync(BookAsync(noura, w.SlugA, w.Haircut, w.Faisal, At(Target, 10), ct), ct, HttpStatusCode.Created);
        var id = booking.GetProperty("id").GetGuid();
        await OkAsync(w.OwnerA.PostAsync($"/api/v1/shop/bookings/{id}/notes", new { text = "يفضّل المقص" }, ct), ct, HttpStatusCode.Created);

        using var support = await IdentityTestData.SignInNewStaffAsync(w.Factory, SystemRoles.Support, ct);
        var customerId = (await OkAsync(support.GetAsync($"/api/v1/admin/bookings/{id}", ct), ct)).GetProperty("booking").GetProperty("customerId").GetGuid();
        var list = await OkAsync(support.GetAsync($"/api/v1/admin/bookings?status=Pending&status=Confirmed&customerId={customerId}&channel=Online", ct), ct);
        list.GetProperty("total").GetInt32().ShouldBe(1);
        list.GetProperty("counts").GetProperty("confirmed").GetInt32().ShouldBe(1);
        (await OkAsync(support.GetAsync("/api/v1/admin/bookings?channel=WalkIn", ct), ct)).GetProperty("total").GetInt32().ShouldBe(0);
        var detail = await OkAsync(support.GetAsync($"/api/v1/admin/bookings/{id}", ct), ct);
        detail.GetProperty("notes").GetArrayLength().ShouldBe(1);
        detail.ToString().ShouldNotContain("+966");

        // Transitions: a reason every time, the shop's own rules, never "cancelled by the customer".
        var version = booking.GetProperty("version").GetUInt32();
        await FailsAsync(support.PostAsync($"/api/v1/admin/bookings/{id}/transitions", new { to = "Arrived", reason = "", version }, ct), HttpStatusCode.BadRequest, "validation.reason_required", ct);
        await FailsAsync(support.PostAsync($"/api/v1/admin/bookings/{id}/transitions", new { to = "Arrived", reason = "Customer called", version }, ct), HttpStatusCode.UnprocessableEntity, "booking.too_early", ct);
        await FailsAsync(support.PostAsync($"/api/v1/admin/bookings/{id}/transitions", new { to = "CancelledByCustomer", reason = "Customer called", version }, ct), HttpStatusCode.Conflict, "booking.invalid_transition", ct);
        (await w.OwnerA.PostAsync($"/api/v1/admin/bookings/{id}/transitions", new { to = "Arrived", reason = "x x x", version }, ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Reschedule choices: the booking's own time does not block it; both assigned professionals are offered.
        var options = await OkAsync(support.GetAsync($"/api/v1/admin/bookings/{id}/reschedule/options?date={Iso(Target)}", ct), ct);
        options.GetProperty("professionals").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ShouldBe([w.Faisal, w.Omar], ignoreOrder: true);
        options.GetProperty("slots").EnumerateArray().Select(s => s.GetProperty("startsAt").GetDateTimeOffset()).ShouldContain(At(Target, 10));
        await FailsAsync(support.GetAsync($"/api/v1/admin/bookings/{id}/reschedule/options?date={Iso(Target)}&professionalId={w.ProB}", ct), HttpStatusCode.UnprocessableEntity, "booking.professional_not_eligible", ct);

        await OkAsync(w.StaffA.PostAsync("/api/v1/shop/bookings/walk-in", new { serviceId = w.Haircut, professionalId = w.Omar, startsAt = At(Target, 12), customerName = "زائر" }, ct), ct, HttpStatusCode.Created);
        Task<HttpResponseMessage> Move(DateTimeOffset start, Guid? professionalId, string? reason, string? key) =>
            support.SendAsync(HttpMethod.Post, $"/api/v1/admin/bookings/{id}/reschedule", new { startsAt = start, professionalId, reason, version }, ct,
                headers: key is null ? null : Key(key));
        await FailsAsync(Move(At(Target, 12, 10), w.Omar, "Customer asked", null), HttpStatusCode.BadRequest, "idempotency.key_required", ct);
        await FailsAsync(Move(At(Target, 12, 10), w.Omar, "", Guid.NewGuid().ToString("N")), HttpStatusCode.BadRequest, "validation.reason_required", ct);
        await FailsAsync(Move(DateTimeOffset.UtcNow.AddHours(-1), w.Omar, "Customer asked", Guid.NewGuid().ToString("N")), HttpStatusCode.BadRequest, "validation.date_in_past", ct);
        await FailsAsync(Move(At(Target, 12, 10), w.Omar, "Customer asked", Guid.NewGuid().ToString("N")), HttpStatusCode.Conflict, "booking.slot_unavailable", ct);
        await FailsAsync(Move(At(Target, 14), w.ProB, "Customer asked", Guid.NewGuid().ToString("N")), HttpStatusCode.UnprocessableEntity, "booking.professional_not_eligible", ct);

        // Collision rules only: 14:07 is off the online grid but free at the desk.
        var key = Guid.NewGuid().ToString("N");
        using (var moved = await Move(At(Target, 14, 7), w.Omar, "Customer asked by phone", key))
        {
            moved.StatusCode.ShouldBe(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync(ct));
            var body = await moved.JsonAsync(ct);
            body.GetProperty("booking").GetProperty("startsAt").GetDateTimeOffset().ShouldBe(At(Target, 14, 7));
            body.GetProperty("booking").GetProperty("professional").GetProperty("id").GetGuid().ShouldBe(w.Omar);
            body.GetProperty("booking").GetProperty("status").GetString().ShouldBe("Confirmed");
            var last = body.GetProperty("history").EnumerateArray().Last();
            last.GetProperty("kind").GetString().ShouldBe("Rescheduled");
            last.GetProperty("actorType").GetString().ShouldBe("PlatformAdmin");
            last.GetProperty("reason").GetString().ShouldBe("Customer asked by phone");
        }

        using (var replay = await Move(At(Target, 14, 7), w.Omar, "Customer asked by phone", key))
        {
            replay.StatusCode.ShouldBe(HttpStatusCode.OK);
            replay.Headers.GetValues("Idempotent-Replayed").ShouldBe(["true"]);
        }

        await FailsAsync(Move(At(Target, 15), w.Omar, "Customer asked by phone", key), HttpStatusCode.UnprocessableEntity, "idempotency.key_reused", ct);
        (await OkAsync(noura.GetAsync($"/api/v1/me/bookings/{id}", ct), ct)).GetProperty("startsAt").GetDateTimeOffset().ShouldBe(At(Target, 14, 7));

        await using (var scope = w.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
            (await db.Set<OutboxMessage>().AsNoTracking().CountAsync(m => m.Type == "booking.rescheduled", ct)).ShouldBe(1, "one row for the move, none for the refusals or the replay");
        }

        var audit = await AuditAsync(w.Factory, id.ToString(), ct);
        audit.Select(a => a.Action).ShouldBe(["booking.rescheduled_by_admin"]);
        audit[0].Reason.ShouldBe("Customer asked by phone");
        audit[0].ShopId.ShouldBe(w.Shops.A.ShopId);

        // A cancellation on the shop's behalf through the general transition.
        var current = (await OkAsync(support.GetAsync($"/api/v1/admin/bookings/{id}", ct), ct)).GetProperty("booking").GetProperty("booking").GetProperty("version").GetUInt32();
        var cancelled = await OkAsync(support.PostAsync($"/api/v1/admin/bookings/{id}/transitions", new { to = "CancelledByShop", reason = "Shop closed that day", version = current }, ct), ct);
        cancelled.GetProperty("booking").GetProperty("status").GetString().ShouldBe("CancelledByShop");
        (await AuditAsync(w.Factory, id.ToString(), ct)).Select(a => a.Action).ShouldBe(["booking.rescheduled_by_admin", "booking.cancelled_by_admin"]);
        await FailsAsync(Move(At(Target, 16), w.Omar, "Too late now", Guid.NewGuid().ToString("N")), HttpStatusCode.Conflict, null, ct);
        noura.Dispose();
    }

    [Fact]
    public async Task PhoneReveal_RequiresPermission_AndAudits()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "adm_reveal", ct);
        var (noura, phone) = await NamedCustomerAsync(w.Factory, "نورة القحطاني", ct);
        noura.Dispose();
        using var support = await IdentityTestData.SignInNewStaffAsync(w.Factory, SystemRoles.Support, ct);

        var list = await OkAsync(support.GetAsync("/api/v1/admin/customers?search=القحطاني", ct), ct);
        var customerId = list.GetProperty("items")[0].GetProperty("id").GetGuid();
        list.ToString().ShouldNotContain(phone);
        var detail = await OkAsync(support.GetAsync($"/api/v1/admin/customers/{customerId}", ct), ct);
        detail.GetProperty("phoneMasked").GetString().ShouldEndWith(phone[^2..]);
        detail.ToString().ShouldNotContain(phone);
        detail.ToString().ShouldNotContain(phone[4..]);

        await FailsAsync(support.PostAsync($"/api/v1/admin/customers/{customerId}/contact/reveal", new { reason = "hi" }, ct), HttpStatusCode.BadRequest, "validation.reason_required", ct);
        await FailsAsync(support.PostAsync($"/api/v1/admin/customers/{Guid.NewGuid()}/contact/reveal", new { reason = "Complaint #12" }, ct), HttpStatusCode.NotFound, null, ct);
        using (var revealed = await support.PostAsync($"/api/v1/admin/customers/{customerId}/contact/reveal", new { reason = "Complaint #12" }, ct))
        {
            revealed.StatusCode.ShouldBe(HttpStatusCode.OK);
            revealed.Headers.CacheControl?.NoStore.ShouldBeTrue();
            (await revealed.JsonAsync(ct)).GetProperty("phone").GetString().ShouldBe(phone);
        }

        (await w.OwnerA.GetAsync("/api/v1/admin/customers", ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await w.OwnerA.PostAsync($"/api/v1/admin/customers/{customerId}/contact/reveal", new { reason = "Complaint #12" }, ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var audit = await AuditAsync(w.Factory, customerId.ToString(), ct);
        audit.Select(a => a.Action).ShouldBe(["customer.contact_revealed"]);
        audit[0].Reason.ShouldBe("Complaint #12");
        (audit[0].Summary ?? string.Empty).ShouldNotContain(phone[4..]);

        // Acceptance: Support cannot reveal once the grant is taken away (next request), and can again once granted.
        var supportRole = await RoleIdAsync(w.Admin, SystemRoles.Support, ct);
        var granted = await RolePermissionsAsync(w.Admin, SystemRoles.Support, ct);
        await OkAsync(w.Admin.PutAsync($"/api/v1/admin/roles/{supportRole}/permissions", new { permissions = granted.Where(p => p != Permissions.Admin.CustomersViewContact) }, ct), ct);
        (await support.PostAsync($"/api/v1/admin/customers/{customerId}/contact/reveal", new { reason = "Complaint #13" }, ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await support.GetAsync($"/api/v1/admin/customers/{customerId}", ct)).StatusCode.ShouldBe(HttpStatusCode.OK, "viewing the masked profile needs only Admin.Customers.View");
        await OkAsync(w.Admin.PutAsync($"/api/v1/admin/roles/{supportRole}/permissions", new { permissions = granted }, ct), ct);
        await OkAsync(support.PostAsync($"/api/v1/admin/customers/{customerId}/contact/reveal", new { reason = "Complaint #13" }, ct), ct);
        (await AuditAsync(w.Factory, customerId.ToString(), ct)).Count.ShouldBe(2);
        (await AuditAsync(w.Factory, supportRole.ToString(), ct)).Select(a => a.Action).ShouldBe(["role.permissions_changed", "role.permissions_changed"]);
    }

    [Fact]
    public async Task Roles_AndStaff_CannotEscalate_ManagedRolesAreProtected_AndEveryChangeIsAudited()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "adm_roles", ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        using var ops = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.OperationsManager, ct);

        (await ops.GetAsync("/api/v1/admin/roles", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ops.PostAsync("/api/v1/admin/roles", new { name = "Auditors" }, ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var auditors = (await OkAsync(admin.PostAsync("/api/v1/admin/roles", new { name = "Auditors" }, ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await FailsAsync(admin.PostAsync("/api/v1/admin/roles", new { name = " auditors " }, ct), HttpStatusCode.BadRequest, "validation.role_name_taken", ct);
        await FailsAsync(admin.PostAsync("/api/v1/admin/roles", new { name = "x" }, ct), HttpStatusCode.BadRequest, "validation.invalid", ct);
        await OkAsync(admin.PutAsync($"/api/v1/admin/roles/{auditors}/permissions", new { permissions = new[] { Permissions.Admin.AuditView, Permissions.Admin.DashboardView } }, ct), ct);
        await FailsAsync(admin.PutAsync($"/api/v1/admin/roles/{auditors}/permissions", new { permissions = new[] { Permissions.SuperAdmin.SubscriptionsOverride } }, ct), HttpStatusCode.BadRequest, "role.permission_not_grantable", ct);
        await FailsAsync(admin.PutAsync($"/api/v1/admin/roles/{auditors}/permissions", new { permissions = new[] { Permissions.Shop.BookingsRead } }, ct), HttpStatusCode.BadRequest, "role.permission_not_grantable", ct);
        await FailsAsync(admin.PutAsync($"/api/v1/admin/roles/{auditors}/permissions", new { permissions = new[] { "Admin.Everything" } }, ct), HttpStatusCode.BadRequest, "role.permission_not_grantable", ct);

        // Managed and seed roles.
        var superAdminRole = await RoleIdAsync(admin, SystemRoles.SuperAdmin, ct);
        await FailsAsync(admin.PutAsync($"/api/v1/admin/roles/{superAdminRole}/permissions", new { permissions = Array.Empty<string>() }, ct), HttpStatusCode.Conflict, "role.managed", ct);
        await FailsAsync(admin.PutAsync($"/api/v1/admin/roles/{await RoleIdAsync(admin, SystemRoles.ShopOwner, ct)}/permissions", new { permissions = Array.Empty<string>() }, ct), HttpStatusCode.NotFound, "role.not_found", ct);
        await FailsAsync(admin.PutAsync($"/api/v1/admin/roles/{await RoleIdAsync(admin, SystemRoles.Support, ct)}", new { name = "Helpdesk" }, ct), HttpStatusCode.Conflict, "role.managed", ct);
        await FailsAsync(admin.DeleteAsync($"/api/v1/admin/roles/{await RoleIdAsync(admin, SystemRoles.OperationsManager, ct)}", ct), HttpStatusCode.Conflict, "role.managed", ct);

        // An admin who manages roles grants only what they hold.
        var keepers = (await OkAsync(admin.PostAsync("/api/v1/admin/roles", new { name = "Role keepers" }, ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await OkAsync(admin.PutAsync($"/api/v1/admin/roles/{keepers}/permissions",
            new { permissions = new[] { Permissions.Admin.RolesView, Permissions.Admin.RolesManage, Permissions.Admin.AuditView } }, ct), ct);
        var keeperEmail = IdentityTestData.NewEmail("keeper");
        var keeperId = await IdentityTestData.CreateStaffAsync(factory, keeperEmail, SystemRoles.Support, ct);
        await FailsAsync(admin.PutAsync($"/api/v1/admin/staff/{keeperId}/roles", new { roles = new[] { SystemRoles.ShopOwner } }, ct), HttpStatusCode.BadRequest, "role.not_assignable", ct);
        await FailsAsync(admin.PutAsync($"/api/v1/admin/staff/{keeperId}/roles", new { roles = Array.Empty<string>() }, ct), HttpStatusCode.BadRequest, "validation.required", ct);
        var keeperStaff = await OkAsync(admin.PutAsync($"/api/v1/admin/staff/{keeperId}/roles", new { roles = new[] { "Role keepers" } }, ct), ct);
        keeperStaff.GetProperty("roles").EnumerateArray().Select(r => r.GetProperty("name").GetString()).ShouldBe(["Role keepers"]);
        using var keeper = await IdentityTestData.SignInStaffAsync(factory, keeperEmail, ct);
        await FailsAsync(keeper.PutAsync($"/api/v1/admin/roles/{auditors}/permissions",
            new { permissions = new[] { Permissions.Admin.AuditView, Permissions.Admin.SettingsEdit } }, ct), HttpStatusCode.Forbidden, "role.escalation", ct);
        await OkAsync(keeper.PutAsync($"/api/v1/admin/roles/{auditors}/permissions", new { permissions = new[] { Permissions.Admin.AuditView } }, ct), ct);
        (await keeper.PutAsync($"/api/v1/admin/staff/{keeperId}/roles", new { roles = new[] { "Auditors" } }, ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, "no Admin.Staff.Manage");

        // Staff assignment: never your own roles; SuperAdmin only by a SuperAdmin; no role with permissions you lack.
        var managers = (await OkAsync(admin.PostAsync("/api/v1/admin/roles", new { name = "Staff managers" }, ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await OkAsync(admin.PutAsync($"/api/v1/admin/roles/{managers}/permissions", new { permissions = new[] { Permissions.Admin.StaffManage, Permissions.Admin.RolesView } }, ct), ct);
        var managerEmail = IdentityTestData.NewEmail("manager");
        var managerId = await IdentityTestData.CreateStaffAsync(factory, managerEmail, SystemRoles.Support, ct);
        await OkAsync(admin.PutAsync($"/api/v1/admin/staff/{managerId}/roles", new { roles = new[] { "Staff managers" } }, ct), ct);
        using var manager = await IdentityTestData.SignInStaffAsync(factory, managerEmail, ct);
        await FailsAsync(manager.PutAsync($"/api/v1/admin/staff/{keeperId}/roles", new { roles = new[] { SystemRoles.SuperAdmin } }, ct), HttpStatusCode.Forbidden, "role.superadmin_only", ct);
        await FailsAsync(manager.PutAsync($"/api/v1/admin/staff/{keeperId}/roles", new { roles = new[] { "Auditors" } }, ct), HttpStatusCode.Forbidden, "role.escalation", ct);

        // The same rules hold for invitations, or an admin could invite an address they control with more rights.
        await FailsAsync(manager.PostAsync("/api/v1/admin/staff/invitations", new { email = IdentityTestData.NewEmail("self"), role = SystemRoles.SuperAdmin, locale = "en" }, ct),
            HttpStatusCode.Forbidden, "role.superadmin_only", ct);
        await FailsAsync(manager.PostAsync("/api/v1/admin/staff/invitations", new { email = IdentityTestData.NewEmail("self"), role = "Auditors", locale = "en" }, ct),
            HttpStatusCode.Forbidden, "role.escalation", ct);
        await FailsAsync(manager.PutAsync($"/api/v1/admin/staff/{managerId}/roles", new { roles = new[] { "Staff managers", "Auditors" } }, ct), HttpStatusCode.Conflict, "staff.self", ct);
        await OkAsync(manager.PutAsync($"/api/v1/admin/staff/{keeperId}/roles", new { roles = new[] { "Staff managers" } }, ct), ct);
        var adminId = (await OkAsync(admin.GetAsync("/api/v1/me", ct), ct)).GetProperty("id").GetGuid();
        await FailsAsync(manager.PostAsync($"/api/v1/admin/staff/{adminId}/disable", new { reason = "test" }, ct), HttpStatusCode.Forbidden, "role.superadmin_only", ct);
        await FailsAsync(admin.PutAsync($"/api/v1/admin/staff/{adminId}/roles", new { roles = new[] { SystemRoles.Support } }, ct), HttpStatusCode.Conflict, "staff.self", ct);

        // The staff list filters by role; a role still held cannot be deleted; an unused one can.
        var holders = await OkAsync(admin.GetAsync($"/api/v1/admin/staff?roleId={managers}", ct), ct);
        holders.GetProperty("items").EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).ShouldBe([keeperId, managerId], ignoreOrder: true);
        await FailsAsync(admin.DeleteAsync($"/api/v1/admin/roles/{managers}", ct), HttpStatusCode.Conflict, "role.in_use", ct);
        await OkAsync(admin.DeleteAsync($"/api/v1/admin/roles/{auditors}", ct), ct, HttpStatusCode.NoContent);
        await OkAsync(admin.PutAsync($"/api/v1/admin/roles/{keepers}", new { name = "Role editors" }, ct), ct);

        // Disabling a staff member ends their access on the next request; enabling restores it.
        await OkAsync(admin.PostAsync($"/api/v1/admin/staff/{managerId}/disable", new { reason = "Left the team" }, ct), ct);
        (await manager.GetAsync("/api/v1/admin/staff", ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        await OkAsync(admin.PostAsync($"/api/v1/admin/staff/{managerId}/enable", new { reason = "Back" }, ct), ct);
        using var managerAgain = await IdentityTestData.SignInStaffAsync(factory, managerEmail, ct);
        await OkAsync(managerAgain.GetAsync("/api/v1/admin/staff", ct), ct);

        (await AuditAsync(factory, auditors.ToString(), ct)).Select(a => a.Action)
            .ShouldBe(["role.created", "role.permissions_changed", "role.permissions_changed", "role.deleted"]);
        (await AuditAsync(factory, keepers.ToString(), ct)).Select(a => a.Action).ShouldBe(["role.created", "role.permissions_changed", "role.renamed"]);
        (await AuditAsync(factory, managerId.ToString(), ct)).Select(a => a.Action).ShouldBe(["staff.roles_changed", "staff.disabled", "staff.enabled"]);
        (await AuditAsync(factory, keeperId.ToString(), ct)).Select(a => a.Action).ShouldBe(["staff.roles_changed", "staff.roles_changed"]);
    }

    [Fact]
    public async Task Reviews_Moderation_FlagHidePublish_MovesTheRatingsOnce_AndIsAudited()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var w = await ArrangeAsync(postgres, "adm_reviews", ct, clock);
        var (noura, nouraPhone) = await NamedCustomerAsync(w.Factory, "نورة", ct);
        var (sara, saraPhone) = await NamedCustomerAsync(w.Factory, "سارة", ct);
        var first = (await OkAsync(BookAsync(noura, w.SlugA, w.Haircut, w.Faisal, At(Target, 10), ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var second = (await OkAsync(BookAsync(sara, w.SlugA, w.Haircut, w.Omar, At(Target, 10), ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        noura.Dispose();
        sara.Dispose();

        clock.SetUtcNow(At(Target, 10, 40).ToUniversalTime());
        using (var staff = await IdentityTestData.SignInStaffAsync(w.Factory, w.Shops.A.StaffEmail, ct))
        {
            foreach (var visit in new[] { first, second })
            {
                await ShopTransitionAsync(staff, visit, "Arrived", ct);
                await ShopTransitionAsync(staff, visit, "Completed", ct);
            }
        }

        using var nouraAgain = await IdentityTestData.SignInCustomerAsync(w.Factory, nouraPhone, ct);
        using var saraAgain = await IdentityTestData.SignInCustomerAsync(w.Factory, saraPhone, ct);
        await OkAsync(nouraAgain.PostAsync($"/api/v1/me/bookings/{first}/review", new { rating = 5, comment = "رائع، كلموني على ٠٥٥ ١٢٣ ٤٥٦٧" }, ct), ct, HttpStatusCode.Created);
        await OkAsync(saraAgain.PostAsync($"/api/v1/me/bookings/{second}/review", new { rating = 2, comment = "تأخر الموعد" }, ct), ct, HttpStatusCode.Created);

        using var support = await IdentityTestData.SignInNewStaffAsync(w.Factory, SystemRoles.Support, ct);
        using var ops = await IdentityTestData.SignInNewStaffAsync(w.Factory, SystemRoles.OperationsManager, ct);
        using var superAdmin = await IdentityTestData.SignInNewStaffAsync(w.Factory, SystemRoles.SuperAdmin, ct);
        using var anonymous = ApiSession.Create(w.Factory);
        async Task<JsonElement> Summary() => (await OkAsync(anonymous.GetAsync($"/api/v1/public/shops/{w.SlugA}/reviews", ct), ct)).GetProperty("summary");
        (await Summary()).GetProperty("count").GetInt32().ShouldBe(2);

        var queue = await OkAsync(support.GetAsync("/api/v1/admin/reviews", ct), ct);
        queue.GetProperty("counts").GetProperty("needsReview").GetInt32().ShouldBe(2);
        var phoneFlagged = await OkAsync(support.GetAsync("/api/v1/admin/reviews?flag=ContainsPhone", ct), ct);
        phoneFlagged.GetProperty("total").GetInt32().ShouldBe(1, "the PostgreSQL filter and the flag use the same pattern (Arabic-Indic digits with spaces)");
        var review = phoneFlagged.GetProperty("items")[0];
        var reviewId = review.GetProperty("id").GetGuid();
        review.GetProperty("flags").EnumerateArray().Select(f => f.GetString()).ShouldBe(["ContainsPhone"]);
        (await OkAsync(support.GetAsync("/api/v1/admin/reviews?flag=LowRating", ct), ct)).GetProperty("items")[0].GetProperty("rating").GetInt32().ShouldBe(2);
        using (var owner = await IdentityTestData.SignInStaffAsync(w.Factory, w.Shops.A.OwnerEmail, ct))
        {
            (await owner.GetAsync("/api/v1/admin/reviews", ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        // Support reports; moderators decide.
        var version = review.GetProperty("version").GetUInt32();
        await FailsAsync(support.PostAsync($"/api/v1/admin/reviews/{reviewId}/flag", new { reason = "no", version }, ct), HttpStatusCode.BadRequest, "validation.reason_required", ct);
        var flagged = await OkAsync(support.PostAsync($"/api/v1/admin/reviews/{reviewId}/flag", new { reason = "Contains a phone number", version }, ct), ct);
        flagged.GetProperty("flags").EnumerateArray().Select(f => f.GetString()).ShouldBe(["Reported", "ContainsPhone"]);
        version = flagged.GetProperty("version").GetUInt32();
        await FailsAsync(support.PostAsync($"/api/v1/admin/reviews/{reviewId}/flag", new { reason = "Contains a phone number", version }, ct), HttpStatusCode.Conflict, "review.already_flagged", ct);
        (await support.PostAsync($"/api/v1/admin/reviews/{reviewId}/hide", new { reason = "Contains a phone number", version }, ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Two moderators hide it at once: one wins, the totals move once.
        var hides = await Task.WhenAll(
            ops.PostAsync($"/api/v1/admin/reviews/{reviewId}/hide", new { reason = "Contains a phone number", version }, ct),
            superAdmin.PostAsync($"/api/v1/admin/reviews/{reviewId}/hide", new { reason = "Personal contact details", version }, ct));
        hides.Select(h => h.StatusCode).ShouldBe([HttpStatusCode.OK, HttpStatusCode.Conflict], ignoreOrder: true);
        foreach (var hide in hides)
        {
            hide.Dispose();
        }

        var summary = await Summary();
        summary.GetProperty("count").GetInt32().ShouldBe(1);
        summary.GetProperty("average").GetDecimal().ShouldBe(2m);
        (await OkAsync(anonymous.GetAsync($"/api/v1/public/shops/{w.SlugA}/reviews?professionalId={w.Faisal}", ct), ct)).GetProperty("summary").GetProperty("count").GetInt32().ShouldBe(0);
        var hidden = await OkAsync(ops.GetAsync("/api/v1/admin/reviews?queue=Hidden", ct), ct);
        hidden.GetProperty("total").GetInt32().ShouldBe(1);
        version = hidden.GetProperty("items")[0].GetProperty("version").GetUInt32();

        var published = await OkAsync(ops.PostAsync($"/api/v1/admin/reviews/{reviewId}/publish", new { version }, ct), ct);
        published.GetProperty("status").GetString().ShouldBe("Published");
        published.GetProperty("flagReason").ValueKind.ShouldBe(JsonValueKind.Null);
        (await Summary()).GetProperty("count").GetInt32().ShouldBe(2);
        (await Summary()).GetProperty("average").GetDecimal().ShouldBe(3.5m);
        await FailsAsync(ops.PostAsync($"/api/v1/admin/reviews/{reviewId}/publish", new { version = published.GetProperty("version").GetUInt32() }, ct), HttpStatusCode.Conflict, "review.nothing_to_publish", ct);

        (await AuditAsync(w.Factory, reviewId.ToString(), ct)).Select(a => a.Action).ShouldBe(["review.flagged", "review.hidden", "review.published"]);
    }

    [Fact]
    public async Task Audit_ListsNewestFirst_WithFilters_ActorNames_AndAKeysetCursor()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "adm_audit", ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var ids = new List<Guid>();
        foreach (var name in new[] { "Role one", "Role two", "Role three" })
        {
            ids.Add((await OkAsync(admin.PostAsync("/api/v1/admin/roles", new { name }, ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid());
        }

        using var ops = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.OperationsManager, ct);
        var page1 = await OkAsync(ops.GetAsync("/api/v1/admin/audit?action=role.created&pageSize=2", ct), ct);
        page1.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("entityId").GetString()).ShouldBe([ids[2].ToString(), ids[1].ToString()]);
        page1.GetProperty("items")[0].GetProperty("actorName").GetString().ShouldBe("موظف اختبار");
        page1.GetProperty("items")[0].GetProperty("actorType").GetString().ShouldBe("PlatformAdmin");
        var cursor = page1.GetProperty("nextCursor").GetString();
        cursor.ShouldNotBeNull();
        var page2 = await OkAsync(ops.GetAsync($"/api/v1/admin/audit?action=role.created&pageSize=2&cursor={cursor}", ct), ct);
        page2.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("entityId").GetString()).ShouldBe([ids[0].ToString()]);
        page2.GetProperty("nextCursor").ValueKind.ShouldBe(JsonValueKind.Null);

        var byEntity = await OkAsync(ops.GetAsync($"/api/v1/admin/audit?entityType=Role&entityId={ids[1]}", ct), ct);
        byEntity.GetProperty("items").GetArrayLength().ShouldBe(1);
        (await OkAsync(ops.GetAsync($"/api/v1/admin/audit?from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(5).ToString("O"))}", ct), ct))
            .GetProperty("items").GetArrayLength().ShouldBe(0);
        var facets = await OkAsync(ops.GetAsync("/api/v1/admin/audit/facets", ct), ct);
        facets.GetProperty("actions").EnumerateArray().Select(a => a.GetString()).ShouldContain("role.created");
        facets.GetProperty("entityTypes").EnumerateArray().Select(a => a.GetString()).ShouldContain("Role");

        using var support = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.Support, ct);
        (await support.GetAsync("/api/v1/admin/audit", ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
