using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Shouldly;
using Trimme.BuildingBlocks.Application.Privacy;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Jobs;
using Trimme.IntegrationTests.Bookings;
using Trimme.IntegrationTests.Identity;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.Modules.Identity.Domain;
using Trimme.Modules.Loyalty.Domain;
using static Trimme.IntegrationTests.Bookings.BookingTestData;

namespace Trimme.IntegrationTests.Loyalty;

/// <summary>
/// D-149, the loyalty add-on (the client's 6-2): the admin switches it on or off and SuperAdmin prices it; the owner sets
/// the rules; points are per mobile number per shop (an online account's, or a walk-in's typed number) and are earned only
/// when the visit is closed, with the barber recorded; a full card overflows to the next; a reward is held at booking,
/// used at the visit and given back on a cancellation or a no-show; the card is wiped or loses a point per period without
/// visits; the add-on's end stops earning and keeps the points; D-132 coupons stay usable.
/// </summary>
public sealed class LoyaltyTests(PostgresFixture postgres)
{
    /// <summary>Customers sign in again after the clock moves; the code limits per number and per address would refuse it.</summary>
    private static readonly Dictionary<string, string?> SignInAgain = new(StringComparer.Ordinal)
    {
        ["Identity:Otp:Limits:ResendCooldown"] = "00:00:00",
        ["Identity:Otp:Limits:MaxCodesPerNumber"] = "1000",
        ["RateLimiting:otp:PermitLimit"] = "1000",
        ["RateLimiting:auth:PermitLimit"] = "1000",
    };

    /// <summary>Sign-in codes off, so an account can move to another number (D-144) and customers sign in by number.</summary>
    private static readonly Dictionary<string, string?> CodesOff = new(SignInAgain, StringComparer.Ordinal) { ["Identity:Otp:Enabled"] = "false" };

    private static DateOnly TargetFrom(FakeTimeProvider clock) => TodayAt(clock.GetUtcNow()).AddDays(2);

    private static async Task<ApiSession> ByNumberAsync(TrimmeApiFactory factory, string phone, CancellationToken ct)
    {
        var session = ApiSession.Create(factory);
        await OkAsync(session.PostAsync("/api/v1/auth/phone/sign-in", new { phone, termsAccepted = true, locale = "ar" }, ct), ct);
        return session;
    }

    private static object Rules(
        int first = 3, int visit = 2, int reward = 4, string type = "AmountOff", Guid? service = null, decimal? amount = 20m, string inactivity = "None",
        int? days = null, uint? version = null) =>
        new
        {
            firstVisitPoints = first, visitPoints = visit, pointsForReward = reward, rewardType = type, rewardServiceId = service, rewardAmount = amount,
            inactivityRule = inactivity, inactivityDays = days, version,
        };

    private static async Task ProcessOutboxAsync(TrimmeApiFactory factory, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
        for (var round = 0; round < 10 && await processor.ProcessBatchAsync(ct) > 0; round++)
        {
        }
    }

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

    /// <summary>Every barber gets a login before the clock moves past the owners' sessions.</summary>
    private static async Task EnsureBarbersAsync(BookingWorld w, CancellationToken ct)
    {
        foreach (var professional in new[] { w.Faisal, w.Omar, w.ProB })
        {
            await w.EnsureBarberLoginAsync(professional, ct);
        }
    }

    /// <summary>The add-on priced once by SuperAdmin and switched on for the shops until <paramref name="days"/> from today.</summary>
    private static async Task AddOnAsync(BookingWorld w, FakeTimeProvider clock, CancellationToken ct, int days = 365, params Guid[] shopIds)
    {
        var today = TodayAt(clock.GetUtcNow());
        using var admin = await IdentityTestData.SignInNewStaffAsync(w.Factory, SystemRoles.SuperAdmin, ct);
        var prices = await OkAsync(admin.GetAsync("/api/v1/admin/loyalty-add-on/prices", ct), ct);
        if (prices.GetProperty("current").ValueKind == JsonValueKind.Null)
        {
            await OkAsync(admin.PostAsync("/api/v1/admin/loyalty-add-on/prices", new { amount = 99m, effectiveFrom = Iso(today) }, ct), ct);
        }

        foreach (var shopId in shopIds.Length == 0 ? [w.Shops.A.ShopId] : shopIds)
        {
            await OkAsync(admin.PostAsync($"/api/v1/admin/shops/{shopId}/subscription/loyalty-add-on", new { endDate = Iso(today.AddDays(days)), notes = "تجربة" }, ct), ct);
        }
    }

    private static async Task<ApiSession> MeAsync(TrimmeApiFactory factory, string phone, CancellationToken ct) =>
        await IdentityTestData.SignInCustomerAsync(factory, phone, ct);

    private static async Task<(ApiSession Session, string Phone)> NamedCustomerAsync(TrimmeApiFactory factory, string name, CancellationToken ct, string? phone = null)
    {
        phone ??= IdentityTestData.NewPhone();
        var session = await IdentityTestData.SignInCustomerAsync(factory, phone, ct);
        await OkAsync(session.PostAsync("/api/v1/auth/profile/complete", new { displayName = name, preferredLocale = "ar", termsAccepted = true }, ct), ct);
        return (session, phone);
    }

    private static async Task<JsonElement> CardAsync(TrimmeApiFactory factory, string phone, string slug, CancellationToken ct)
    {
        using var me = await MeAsync(factory, phone, ct);
        return await OkAsync(me.GetAsync($"/api/v1/me/loyalty/shops/{slug}", ct), ct);
    }

    /// <summary>The visit's own barber marks it «وصل» then «انتهت الخدمة» (or «ما حضر»), then the outbox runs.</summary>
    private static async Task CloseAsync(BookingWorld w, FakeTimeProvider clock, Guid bookingId, DateTimeOffset start, CancellationToken ct, bool noShow = false)
    {
        clock.SetUtcNow(start.AddMinutes(40).ToUniversalTime());
        using var staff = await w.BarberOfAsync(bookingId, ct);
        foreach (var to in noShow ? new[] { "NoShow" } : ["Arrived", "Completed"])
        {
            var current = await OkAsync(staff.GetAsync($"/api/v1/shop/bookings/{bookingId}", ct), ct);
            await OkAsync(staff.PostAsync($"/api/v1/shop/bookings/{bookingId}/transitions", new { to, version = current.GetProperty("booking").GetProperty("version").GetUInt32() }, ct), ct);
        }

        await ProcessOutboxAsync(w.Factory, ct);
    }

    private static Task<HttpResponseMessage> BookAsync(
        ApiSession customer, string slug, DateTimeOffset start, CancellationToken ct, Guid? serviceId = null, Guid[]? serviceIds = null, Guid? packageId = null,
        Guid? professionalId = null, bool useReward = false, Guid? couponId = null) =>
        customer.SendAsync(
            HttpMethod.Post, "/api/v1/bookings",
            new { shopSlug = slug, serviceId, serviceIds, packageId, professionalId, startsAt = start, note = (string?)null, useLoyaltyReward = useReward, loyaltyRewardId = couponId },
            ct, headers: Key());

    private static Task<HttpResponseMessage> PhoneBookingAsync(
        ApiSession barber, Guid serviceId, Guid professionalId, DateTimeOffset start, string? phone, CancellationToken ct, bool useReward = false) =>
        barber.SendAsync(HttpMethod.Post, "/api/v1/shop/bookings/walk-in",
            new { serviceId, professionalId, startsAt = start, customerName = "زائر", customerPhone = phone, channel = "Phone", useLoyaltyReward = useReward }, ct, headers: Key());

    [Fact]
    public async Task Points_AreEarnedOnlyWhenTheVisitIsClosed_WithTheBarber_AndAFullCardOverflowsToTheNext()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var w = await ArrangeAsync(postgres, "loyalty_earn", ct, clock, SignInAgain);
        await EnsureBarbersAsync(w, ct);
        await AddOnAsync(w, clock, ct);
        await OkAsync(w.OwnerA.PutAsync("/api/v1/shop/loyalty", Rules(first: 3, visit: 2, reward: 4), ct), ct);
        var target = TargetFrom(clock);

        var (noura, phone) = await NamedCustomerAsync(w.Factory, "نورة", ct);
        var first = (await OkAsync(BookAsync(noura, w.SlugA, At(target, 10), ct, serviceId: w.Haircut, professionalId: w.Faisal), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var second = (await OkAsync(BookAsync(noura, w.SlugA, At(target, 11), ct, serviceId: w.Haircut, professionalId: w.Omar), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var missed = (await OkAsync(BookAsync(noura, w.SlugA, At(target, 12), ct, serviceId: w.Beard, professionalId: w.Faisal), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var third = (await OkAsync(BookAsync(noura, w.SlugA, At(target, 13), ct, serviceId: w.Beard, professionalId: w.Omar), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        noura.Dispose();

        // Booking earns nothing; neither does «وصل».
        await ProcessOutboxAsync(w.Factory, ct);
        (await CardAsync(w.Factory, phone, w.SlugA, ct)).GetProperty("points").GetInt32().ShouldBe(0);

        // «انتهت الخدمة»: the first visit's 3 points, with the barber who closed it.
        await CloseAsync(w, clock, first, At(target, 10), ct);
        var card = await CardAsync(w.Factory, phone, w.SlugA, ct);
        card.GetProperty("active").GetBoolean().ShouldBeTrue();
        card.GetProperty("points").GetInt32().ShouldBe(3);
        card.GetProperty("rewardsReady").GetInt32().ShouldBe(0);
        card.GetProperty("pointsToNext").GetInt32().ShouldBe(1);
        var earned = card.GetProperty("history").EnumerateArray().Single();
        earned.GetProperty("kind").GetString().ShouldBe("Earned");
        earned.GetProperty("points").GetInt32().ShouldBe(3);
        earned.GetProperty("bookingId").GetGuid().ShouldBe(first);
        earned.GetProperty("barberName").GetString().ShouldNotBeNullOrWhiteSpace();
        (await ScalarAsync(w.Factory, "SELECT professional_id FROM loyalty.entries WHERE booking_id = @p", ct, first)).ShouldBe(w.Faisal);
        (await ScalarAsync(w.Factory, "SELECT closed_by_id IS NOT NULL FROM loyalty.entries WHERE booking_id = @p", ct, first)).ShouldBe(true);

        // «خلصت زيارتك»: the customer is told in-app how many points and how many are left.
        using (var me = await MeAsync(w.Factory, phone, ct))
        {
            var notice = (await OkAsync(me.GetAsync("/api/v1/me/notifications", ct), ct)).GetProperty("items").EnumerateArray()
                .Single(n => n.GetProperty("kind").GetString() == "loyalty.visit_completed");
            notice.GetProperty("parameters").GetProperty("pointsEarned").GetString().ShouldBe("3");
            notice.GetProperty("parameters").GetProperty("pointsToNext").GetString().ShouldBe("1");
        }

        // A later visit (another barber) gives 2: a full card of 4 and 1 on the next one; the outbox again changes nothing.
        await CloseAsync(w, clock, second, At(target, 11), ct);
        await ProcessOutboxAsync(w.Factory, ct);
        card = await CardAsync(w.Factory, phone, w.SlugA, ct);
        card.GetProperty("points").GetInt32().ShouldBe(5);
        card.GetProperty("rewardsReady").GetInt32().ShouldBe(1);
        card.GetProperty("currentCard").GetInt32().ShouldBe(1);
        card.GetProperty("pointsToNext").GetInt32().ShouldBe(3);

        // A no-show earns nothing; the next visit makes it 7: still one reward ready and 3 on the next card («+٣»).
        await CloseAsync(w, clock, missed, At(target, 12), ct, noShow: true);
        (await CardAsync(w.Factory, phone, w.SlugA, ct)).GetProperty("points").GetInt32().ShouldBe(5);
        await CloseAsync(w, clock, third, At(target, 13), ct);
        card = await CardAsync(w.Factory, phone, w.SlugA, ct);
        card.GetProperty("points").GetInt32().ShouldBe(7);
        card.GetProperty("rewardsReady").GetInt32().ShouldBe(1);
        card.GetProperty("currentCard").GetInt32().ShouldBe(3);
        card.GetProperty("history").GetArrayLength().ShouldBe(3);

        // The owner sees the figures; a barber does not.
        using var owner = await IdentityTestData.SignInStaffAsync(w.Factory, w.Shops.A.OwnerEmail, ct);
        var stats = (await OkAsync(owner.GetAsync("/api/v1/shop/loyalty", ct), ct)).GetProperty("stats");
        stats.GetProperty("members").GetInt32().ShouldBe(1);
        stats.GetProperty("pointsEarned").GetInt32().ShouldBe(7);
        stats.GetProperty("rewardsReady").GetInt32().ShouldBe(1);
        using var faisal = await w.BarberAsync(w.Faisal, ct);
        (await OkAsync(faisal.GetAsync("/api/v1/shop/loyalty", ct), ct)).GetProperty("stats").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task AReward_IsHeldAtBooking_UsedAtTheVisit_AndGivenBackOnACancellationOrANoShow()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var w = await ArrangeAsync(postgres, "loyalty_redeem", ct, clock, SignInAgain);
        await EnsureBarbersAsync(w, ct);
        await AddOnAsync(w, clock, ct);

        // A free haircut every 4 points; the first visit gives 4.
        await FailsAsync(w.OwnerA.PutAsync("/api/v1/shop/loyalty", Rules(first: 4, visit: 1, reward: 4, type: "FreeService", service: w.ServiceB, amount: null), ct),
            HttpStatusCode.BadRequest, "rewardServiceId", ct);
        await OkAsync(w.OwnerA.PutAsync("/api/v1/shop/loyalty", Rules(first: 4, visit: 1, reward: 4, type: "FreeService", service: w.Haircut, amount: null), ct), ct);
        var target = TargetFrom(clock);
        var (noura, phone) = await NamedCustomerAsync(w.Factory, "نورة", ct);
        var first = (await OkAsync(BookAsync(noura, w.SlugA, At(target, 10), ct, serviceId: w.Haircut, professionalId: w.Faisal), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        noura.Dispose();
        await CloseAsync(w, clock, first, At(target, 10), ct);
        (await CardAsync(w.Factory, phone, w.SlugA, ct)).GetProperty("rewardsReady").GetInt32().ShouldBe(1);

        using var me = await MeAsync(w.Factory, phone, ct);

        // One reward per booking, none on a package, and a free haircut needs a haircut in the booking.
        await FailsAsync(BookAsync(me, w.SlugA, At(target, 12), ct, packageId: w.Package, professionalId: w.Faisal, useReward: true),
            HttpStatusCode.BadRequest, "loyalty.reward_not_with_package", ct);
        await FailsAsync(BookAsync(me, w.SlugA, At(target, 12), ct, serviceId: w.Haircut, useReward: true, couponId: Guid.NewGuid()),
            HttpStatusCode.BadRequest, "useLoyaltyReward", ct);
        await FailsAsync(BookAsync(me, w.SlugA, At(target, 12), ct, serviceId: w.Beard, professionalId: w.Faisal, useReward: true),
            HttpStatusCode.BadRequest, "loyalty.reward_service_not_booked", ct);

        // «استخدم مكافأتي»: held at booking; the haircut's price comes off the amount due; a second booking cannot use it.
        var held = await OkAsync(BookAsync(me, w.SlugA, At(target, 12), ct, serviceIds: [w.Haircut, w.Beard], professionalId: w.Faisal, useReward: true), ct, HttpStatusCode.Created);
        held.GetProperty("loyaltyReward").GetProperty("discount").GetDecimal().ShouldBe(60m);
        held.GetProperty("amountDue").GetDecimal().ShouldBe(60m);
        var card = await OkAsync(me.GetAsync($"/api/v1/me/loyalty/shops/{w.SlugA}", ct), ct);
        card.GetProperty("points").GetInt32().ShouldBe(0);
        card.GetProperty("rewardsHeld").GetInt32().ShouldBe(1);
        await FailsAsync(BookAsync(me, w.SlugA, At(target, 13), ct, serviceId: w.Haircut, professionalId: w.Omar, useReward: true),
            HttpStatusCode.UnprocessableEntity, "loyalty.reward_unavailable", ct);
        using (var owner = await IdentityTestData.SignInStaffAsync(w.Factory, w.Shops.A.OwnerEmail, ct))
        {
            var forShop = (await OkAsync(owner.GetAsync($"/api/v1/shop/bookings/{held.GetProperty("id").GetGuid()}", ct), ct)).GetProperty("booking");
            forShop.GetProperty("loyaltyReward").GetProperty("discount").GetDecimal().ShouldBe(60m);
            forShop.GetProperty("amountDue").GetDecimal().ShouldBe(60m);
        }

        // Cancelled by the customer: the points come back.
        await OkAsync(me.PostAsync($"/api/v1/me/bookings/{held.GetProperty("id").GetGuid()}/cancel", new { reason = (string?)null, version = held.GetProperty("version").GetUInt32() }, ct), ct);
        await ProcessOutboxAsync(w.Factory, ct);
        card = await OkAsync(me.GetAsync($"/api/v1/me/loyalty/shops/{w.SlugA}", ct), ct);
        card.GetProperty("points").GetInt32().ShouldBe(4);
        card.GetProperty("rewardsHeld").GetInt32().ShouldBe(0);

        // A no-show gives them back too.
        var missed = (await OkAsync(BookAsync(me, w.SlugA, At(target, 14), ct, serviceId: w.Haircut, professionalId: w.Faisal, useReward: true), ct, HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        await CloseAsync(w, clock, missed, At(target, 14), ct, noShow: true);
        (await CardAsync(w.Factory, phone, w.SlugA, ct)).GetProperty("points").GetInt32().ShouldBe(4);

        // Used at a completed visit: its points leave the card, and that visit earns none.
        using var later = await MeAsync(w.Factory, phone, ct);
        var redeemed = (await OkAsync(BookAsync(later, w.SlugA, At(target, 16), ct, serviceId: w.Haircut, professionalId: w.Faisal, useReward: true), ct, HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        await CloseAsync(w, clock, redeemed, At(target, 16), ct);
        card = await CardAsync(w.Factory, phone, w.SlugA, ct);
        card.GetProperty("points").GetInt32().ShouldBe(0);
        var history = card.GetProperty("history").EnumerateArray().ToList();
        history[0].GetProperty("kind").GetString().ShouldBe("Redeemed");
        history[0].GetProperty("points").GetInt32().ShouldBe(-4);
        history.Count(h => h.GetProperty("bookingId").ValueKind != JsonValueKind.Null && h.GetProperty("bookingId").GetGuid() == redeemed).ShouldBe(1);
        (await ScalarAsync(w.Factory, "SELECT status FROM loyalty.redemptions WHERE booking_id = @p", ct, redeemed)).ShouldBe("Consumed");
        (await ScalarAsync(w.Factory, "SELECT count(*) FROM loyalty.redemptions WHERE status = 'Returned'", ct)).ShouldBe(2L);

        // The owner's report: the free visit brings in nothing.
        using var ownerNow = await IdentityTestData.SignInStaffAsync(w.Factory, w.Shops.A.OwnerEmail, ct);
        var report = await OkAsync(ownerNow.GetAsync($"/api/v1/shop/reports?from={Iso(target)}&to={Iso(target)}", ct), ct);
        var visit = report.GetProperty("visits").EnumerateArray().Single(v => v.GetProperty("id").GetGuid() == redeemed);
        visit.GetProperty("expectedIncome").GetDecimal().ShouldBe(0m);
        visit.GetProperty("loyaltyReward").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Points_FollowTheNumber_AcrossAccounts_AndAWalkInsTypedNumber_WithoutShowingIt()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var w = await ArrangeAsync(postgres, "loyalty_number", ct, clock, CodesOff);
        await EnsureBarbersAsync(w, ct);
        await AddOnAsync(w, clock, ct);
        await OkAsync(w.OwnerA.PutAsync("/api/v1/shop/loyalty", Rules(first: 2, visit: 1, reward: 3, amount: 10m), ct), ct);
        var target = TargetFrom(clock);
        var phone = IdentityTestData.NewPhone();

        // Two phone bookings with the number typed: the first gives 2, the second 1.
        using (var faisal = await w.BarberAsync(w.Faisal, ct))
        {
            var noNumber = (await OkAsync(PhoneBookingAsync(faisal, w.Haircut, w.Faisal, At(target, 9), null, ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
            var firstVisit = (await OkAsync(PhoneBookingAsync(faisal, w.Haircut, w.Faisal, At(target, 10), phone, ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
            var secondVisit = (await OkAsync(PhoneBookingAsync(faisal, w.Haircut, w.Faisal, At(target, 11), phone, ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
            await CloseAsync(w, clock, noNumber, At(target, 9), ct);
            (await ScalarAsync(w.Factory, "SELECT count(*) FROM loyalty.entries", ct)).ShouldBe(0L, "a booking without a number earns nothing");
            await CloseAsync(w, clock, firstVisit, At(target, 10), ct);

            // An account with that number sees them; the barber's check says no reward yet.
            using var account = await ByNumberAsync(w.Factory, phone, ct);
            (await OkAsync(account.GetAsync($"/api/v1/me/loyalty/shops/{w.SlugA}", ct), ct)).GetProperty("points").GetInt32().ShouldBe(2);
            using var barber = await w.BarberAsync(w.Faisal, ct);
            (await OkAsync(barber.PostAsync("/api/v1/shop/loyalty/lookup", new { phone }, ct), ct)).GetProperty("rewardReady").GetBoolean().ShouldBeFalse();

            await CloseAsync(w, clock, secondVisit, At(target, 11), ct);

            // The account moves to another number: its card there is empty. A new account with the old number finds the points.
            using var moved = await ByNumberAsync(w.Factory, phone, ct);
            await OkAsync(moved.PutAsync("/api/v1/me/profile", new { displayName = "خالد", preferredLocale = "ar", phone = IdentityTestData.NewPhone() }, ct), ct);
            (await OkAsync(moved.GetAsync($"/api/v1/me/loyalty/shops/{w.SlugA}", ct), ct)).GetProperty("points").GetInt32().ShouldBe(0);
            using var second = await ByNumberAsync(w.Factory, phone, ct);
            var shared = await OkAsync(second.GetAsync("/api/v1/me/loyalty", ct), ct);
            shared.GetProperty("items").EnumerateArray().Single().GetProperty("points").GetInt32().ShouldBe(3);
        }

        // The barber's check: a reward is ready, and what it is — nothing else.
        using var desk = await w.BarberAsync(w.Faisal, ct);
        var lookup = await desk.PostAsync("/api/v1/shop/loyalty/lookup", new { phone }, ct);
        var lookupJson = await lookup.Content.ReadAsStringAsync(ct);
        var found = JsonDocument.Parse(lookupJson).RootElement;
        found.GetProperty("rewardReady").GetBoolean().ShouldBeTrue();
        found.GetProperty("rewardAmount").GetDecimal().ShouldBe(10m);
        await FailsAsync(desk.PostAsync("/api/v1/shop/loyalty/lookup", new { phone = "123" }, ct), HttpStatusCode.BadRequest, "phone", ct);

        // A phone booking uses it with the typed number; without a number it cannot.
        await FailsAsync(PhoneBookingAsync(desk, w.Haircut, w.Faisal, At(target, 13), null, ct, useReward: true), HttpStatusCode.BadRequest, "customerPhone", ct);
        var withReward = await OkAsync(PhoneBookingAsync(desk, w.Haircut, w.Faisal, At(target, 13), phone, ct, useReward: true), ct, HttpStatusCode.Created);
        withReward.GetProperty("loyaltyReward").GetProperty("discount").GetDecimal().ShouldBe(10m);
        withReward.GetProperty("amountDue").GetDecimal().ShouldBe(50m);

        // No number and no member key in anything the shop reads (the barber's own view of an open phone booking shows the
        // typed number by design, D-147; it never shows the member key).
        await using var scope = w.Factory.Services.CreateAsyncScope();
        var key = scope.ServiceProvider.GetRequiredService<IPersonalDataProtector>().LookupHash(phone, PersonalDataPurposes.LoyaltyMember);
        (await (await desk.GetAsync($"/api/v1/shop/bookings/{withReward.GetProperty("id").GetGuid()}", ct)).Content.ReadAsStringAsync(ct)).ShouldNotContain(key, Case.Insensitive);
        using var owner = await IdentityTestData.SignInStaffAsync(w.Factory, w.Shops.A.OwnerEmail, ct);
        var payloads = new List<string>
        {
            lookupJson,
            await (await owner.GetAsync($"/api/v1/shop/bookings/{withReward.GetProperty("id").GetGuid()}", ct)).Content.ReadAsStringAsync(ct),
            await (await owner.GetAsync("/api/v1/shop/loyalty", ct)).Content.ReadAsStringAsync(ct),
            await (await owner.GetAsync("/api/v1/shop/notifications", ct)).Content.ReadAsStringAsync(ct),
            await (await owner.GetAsync($"/api/v1/shop/bookings?from={Iso(target)}&to={Iso(target)}", ct)).Content.ReadAsStringAsync(ct),
        };
        foreach (var payload in payloads)
        {
            payload.ShouldNotContain(key, Case.Insensitive);
            payload.ShouldNotContain(phone[4..]);
        }
    }

    [Fact]
    public async Task WithoutVisits_OneShopWipesTheCard_AndAnotherTakesAPointPerPeriod()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var w = await ArrangeAsync(postgres, "loyalty_decay", ct, clock, SignInAgain);
        await EnsureBarbersAsync(w, ct);
        await AddOnAsync(w, clock, ct, 365, w.Shops.A.ShopId, w.Shops.B.ShopId);
        await OkAsync(w.OwnerA.PutAsync("/api/v1/shop/loyalty", Rules(first: 3, visit: 2, reward: 10, inactivity: "Wipe", days: 30), ct), ct);
        await OkAsync(w.OwnerB.PutAsync("/api/v1/shop/loyalty", Rules(first: 3, visit: 2, reward: 10, inactivity: "DecayPerPeriod", days: 30), ct), ct);
        await FailsAsync(w.OwnerA.PutAsync("/api/v1/shop/loyalty", Rules(inactivity: "Wipe", days: 3), ct), HttpStatusCode.BadRequest, "inactivityDays", ct);
        var slugB = (await OkAsync(w.Admin.GetAsync($"/api/v1/admin/shops/{w.Shops.B.ShopId}", ct), ct)).GetProperty("slug").GetString()!;
        var target = TargetFrom(clock);

        var (noura, phone) = await NamedCustomerAsync(w.Factory, "نورة", ct);
        var atA = (await OkAsync(BookAsync(noura, w.SlugA, At(target, 10), ct, serviceId: w.Haircut, professionalId: w.Faisal), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var atB = (await OkAsync(BookAsync(noura, slugB, At(target, 11), ct, serviceId: w.ServiceB, professionalId: w.ProB), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        noura.Dispose();
        await CloseAsync(w, clock, atA, At(target, 10), ct);
        await CloseAsync(w, clock, atB, At(target, 11), ct);

        // A month without visits: the card at A is wiped, B's loses one point; two months: B's loses another.
        clock.Advance(TimeSpan.FromDays(31));
        (await CardAsync(w.Factory, phone, w.SlugA, ct)).GetProperty("points").GetInt32().ShouldBe(0);
        (await CardAsync(w.Factory, phone, slugB, ct)).GetProperty("points").GetInt32().ShouldBe(2);
        clock.Advance(TimeSpan.FromDays(31));
        (await CardAsync(w.Factory, phone, slugB, ct)).GetProperty("points").GetInt32().ShouldBe(1);

        // A new visit at A starts from zero with the later-visit points; the history says the card was wiped.
        var day = TodayAt(clock.GetUtcNow()).AddDays(2);
        using (var me = await MeAsync(w.Factory, phone, ct))
        {
            var back = (await OkAsync(BookAsync(me, w.SlugA, At(day, 10), ct, serviceId: w.Haircut, professionalId: w.Faisal), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
            await CloseAsync(w, clock, back, At(day, 10), ct);
        }

        var card = await CardAsync(w.Factory, phone, w.SlugA, ct);
        card.GetProperty("points").GetInt32().ShouldBe(2);
        card.GetProperty("history").EnumerateArray().Select(h => (h.GetProperty("kind").GetString(), h.GetProperty("points").GetInt32()))
            .ShouldBe([("Earned", 2), ("Wiped", -3), ("Earned", 3)]);
    }

    [Fact]
    public async Task TheAddOnsEnd_StopsEarningAndRedeeming_AndKeepsThePoints()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var w = await ArrangeAsync(postgres, "loyalty_end", ct, clock, SignInAgain);
        await EnsureBarbersAsync(w, ct);
        await AddOnAsync(w, clock, ct);
        await OkAsync(w.OwnerA.PutAsync("/api/v1/shop/loyalty", Rules(first: 3, visit: 1, reward: 3, amount: 15m), ct), ct);
        var target = TargetFrom(clock);
        var (noura, phone) = await NamedCustomerAsync(w.Factory, "نورة", ct);
        var first = (await OkAsync(BookAsync(noura, w.SlugA, At(target, 10), ct, serviceId: w.Haircut, professionalId: w.Faisal), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var afterStop = (await OkAsync(BookAsync(noura, w.SlugA, At(target, 11), ct, serviceId: w.Haircut, professionalId: w.Faisal), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        noura.Dispose();
        await CloseAsync(w, clock, first, At(target, 10), ct);

        // The admin switches it off: nothing is earned or redeemed, and the points stay.
        using (var admin = await IdentityTestData.SignInNewStaffAsync(w.Factory, SystemRoles.SuperAdmin, ct))
        {
            (await OkAsync(admin.PostAsync($"/api/v1/admin/shops/{w.Shops.A.ShopId}/subscription/loyalty-add-on/stop", null, ct), ct)).GetProperty("active").GetBoolean().ShouldBeFalse();
            await FailsAsync(admin.PostAsync($"/api/v1/admin/shops/{w.Shops.A.ShopId}/subscription/loyalty-add-on/stop", null, ct), HttpStatusCode.Conflict, "loyalty_add_on.not_active", ct);
        }

        await CloseAsync(w, clock, afterStop, At(target, 11), ct);
        var card = await CardAsync(w.Factory, phone, w.SlugA, ct);
        card.GetProperty("active").GetBoolean().ShouldBeFalse();
        card.GetProperty("points").GetInt32().ShouldBe(3);
        card.GetProperty("rewardsReady").GetInt32().ShouldBe(1);
        (await OkAsync(ApiSession.Create(w.Factory).GetAsync($"/api/v1/public/shops/{w.SlugA}/loyalty", ct), ct)).GetProperty("active").GetBoolean().ShouldBeFalse();
        using (var me = await MeAsync(w.Factory, phone, ct))
        {
            await FailsAsync(BookAsync(me, w.SlugA, At(target, 14), ct, serviceId: w.Haircut, professionalId: w.Faisal, useReward: true),
                HttpStatusCode.UnprocessableEntity, "loyalty.not_active", ct);
        }

        // Switched on again for two days: the points are there, and a reward can be used.
        await AddOnAsync(w, clock, ct, days: 1);
        (await CardAsync(w.Factory, phone, w.SlugA, ct)).GetProperty("active").GetBoolean().ShouldBeTrue();
        using (var me = await MeAsync(w.Factory, phone, ct))
        {
            await OkAsync(BookAsync(me, w.SlugA, At(target, 14), ct, serviceId: w.Haircut, professionalId: w.Faisal, useReward: true), ct, HttpStatusCode.Created);
        }

        // After its last day it stops by itself.
        clock.Advance(TimeSpan.FromDays(3));
        (await OkAsync(ApiSession.Create(w.Factory).GetAsync($"/api/v1/public/shops/{w.SlugA}/loyalty", ct), ct)).GetProperty("active").GetBoolean().ShouldBeFalse();
        (await CardAsync(w.Factory, phone, w.SlugA, ct)).GetProperty("active").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task TheAddOn_IsPricedBySuperAdmin_SwitchedByTheAdmin_AndShownOnTheSubscriptionPage_AndTheRulesAreTheOwners()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var w = await ArrangeAsync(postgres, "loyalty_addon", ct, clock, SignInAgain);
        var today = TodayAt(clock.GetUtcNow());
        var on = $"/api/v1/admin/shops/{w.Shops.A.ShopId}/subscription/loyalty-add-on";

        // No price yet; a shop without a subscription cannot have it.
        await FailsAsync(w.Admin.PostAsync(on, new { endDate = Iso(today.AddDays(30)) }, ct), HttpStatusCode.UnprocessableEntity, "loyalty_add_on.no_price", ct);
        using (var support = await IdentityTestData.SignInNewStaffAsync(w.Factory, SystemRoles.Support, ct))
        {
            await FailsAsync(support.PostAsync("/api/v1/admin/loyalty-add-on/prices", new { amount = 99m, effectiveFrom = Iso(today) }, ct), HttpStatusCode.Forbidden, null, ct);
        }

        var prices = await OkAsync(w.Admin.PostAsync("/api/v1/admin/loyalty-add-on/prices", new { amount = 99m, effectiveFrom = Iso(today) }, ct), ct);
        prices.GetProperty("current").GetProperty("versionNumber").GetInt32().ShouldBe(1);
        await FailsAsync(w.Admin.PostAsync("/api/v1/admin/loyalty-add-on/prices", new { amount = 120m, effectiveFrom = Iso(today.AddDays(-1)) }, ct),
            HttpStatusCode.BadRequest, "validation.date_in_past", ct);
        var bare = await Trimme.IntegrationTests.Tenancy.ShopTestData.CreateShopAsync(w.Admin, $"bare-{Guid.NewGuid():N}"[..20], ct);
        await FailsAsync(w.Admin.PostAsync($"/api/v1/admin/shops/{bare}/subscription/loyalty-add-on", new { endDate = Iso(today.AddDays(30)) }, ct),
            HttpStatusCode.Conflict, "loyalty_add_on.no_subscription", ct);

        // The owner neither switches it nor prices it; the admin does, until an end date, at the price in force.
        await FailsAsync(w.OwnerA.PostAsync(on, new { endDate = Iso(today.AddDays(30)) }, ct), HttpStatusCode.Forbidden, null, ct);
        await FailsAsync(w.Admin.PostAsync(on, new { endDate = Iso(today.AddDays(-1)) }, ct), HttpStatusCode.BadRequest, "endDate", ct);
        var addOn = await OkAsync(w.Admin.PostAsync(on, new { endDate = Iso(today.AddDays(30)), notes = "شهر" }, ct), ct);
        addOn.GetProperty("active").GetBoolean().ShouldBeTrue();
        addOn.GetProperty("daysRemaining").GetInt32().ShouldBe(31);
        addOn.GetProperty("periods")[0].GetProperty("amount").GetDecimal().ShouldBe(99m);

        // Switched on again while in force: it continues after the current end.
        var renewed = await OkAsync(w.Admin.PostAsync(on, new { endDate = Iso(today.AddDays(60)) }, ct), ct);
        renewed.GetProperty("endDate").GetString().ShouldBe(Iso(today.AddDays(60)));
        renewed.GetProperty("periods")[0].GetProperty("startDate").GetString().ShouldBe(Iso(today.AddDays(31)));

        // The owner's subscription page shows it, and this month's WhatsApp messages (no allowance: unlimited).
        var mine = await OkAsync(w.OwnerA.GetAsync("/api/v1/shop/subscription", ct), ct);
        mine.GetProperty("loyaltyAddOn").GetProperty("active").GetBoolean().ShouldBeTrue();
        mine.GetProperty("loyaltyAddOn").GetProperty("endDate").GetString().ShouldBe(Iso(today.AddDays(60)));
        mine.GetProperty("loyaltyAddOn").GetProperty("periods").EnumerateArray()
            .ShouldAllBe(p => p.GetProperty("notes").ValueKind == JsonValueKind.Null); // the admin's «شهر» stays on the platform side
        mine.GetProperty("messageUsage").GetProperty("used").GetInt32().ShouldBe(0);
        mine.GetProperty("messageUsage").GetProperty("allowance").ValueKind.ShouldBe(JsonValueKind.Null);
        (await OkAsync(w.Admin.GetAsync($"/api/v1/admin/shops/{w.Shops.A.ShopId}/subscription", ct), ct)).GetProperty("loyaltyAddOn").GetProperty("periods").GetArrayLength().ShouldBe(2);

        // The rules are the owner's (barbers cannot change them); the admin can set them too. Out-of-range values are refused.
        using var faisal = await w.BarberAsync(w.Faisal, ct);
        await FailsAsync(faisal.PutAsync("/api/v1/shop/loyalty", Rules(), ct), HttpStatusCode.Forbidden, null, ct);
        var invalid = await FailsAsync(w.OwnerA.PutAsync("/api/v1/shop/loyalty", Rules(first: 0, visit: 0, reward: 1, amount: 4m), ct), HttpStatusCode.BadRequest, null, ct);
        foreach (var field in new[] { "firstVisitPoints", "visitPoints", "pointsForReward", "rewardAmount" })
        {
            invalid.ShouldContain(field);
        }

        var saved = await OkAsync(w.OwnerA.PutAsync("/api/v1/shop/loyalty", Rules(), ct), ct);
        saved.GetProperty("addOnActive").GetBoolean().ShouldBeTrue();
        var byAdmin = await OkAsync(w.Admin.PutAsync($"/api/v1/admin/shops/{w.Shops.A.ShopId}/loyalty",
            Rules(reward: 6, amount: 1000m, version: saved.GetProperty("version").GetUInt32()), ct), ct);
        byAdmin.GetProperty("rules").GetProperty("pointsForReward").GetInt32().ShouldBe(6);
        await FailsAsync(w.OwnerA.PutAsync("/api/v1/shop/loyalty", Rules(version: saved.GetProperty("version").GetUInt32()), ct), HttpStatusCode.Conflict, null, ct);
        var offer = await OkAsync(ApiSession.Create(w.Factory).GetAsync($"/api/v1/public/shops/{w.SlugA}/loyalty", ct), ct);
        offer.GetProperty("active").GetBoolean().ShouldBeTrue();
        offer.GetProperty("rules").GetProperty("rewardAmount").GetDecimal().ShouldBe(1000m);

        // Switched off; everything is audited.
        (await OkAsync(w.Admin.PostAsync($"{on}/stop", null, ct), ct)).GetProperty("periods").EnumerateArray()
            .ShouldAllBe(p => p.GetProperty("stoppedOn").GetString() == Iso(today) || p.GetProperty("stoppedOn").GetString() == Iso(today.AddDays(31)));
        foreach (var action in new[] { "loyalty_add_on.price_added", "loyalty_add_on.switched_on", "loyalty_add_on.stopped", "loyalty.rules_saved" })
        {
            ((long)(await ScalarAsync(w.Factory, "SELECT count(*) FROM administration.audit_entries WHERE action = @p", ct, action))!).ShouldBeGreaterThan(0, action);
        }
    }

    [Fact]
    public async Task ACouponIssuedBeforeTheAddOn_IsStillHeld_ReleasedAndRedeemed_AndItsVisitEarnsNoPoints()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var w = await ArrangeAsync(postgres, "loyalty_coupon", ct, clock, SignInAgain);
        await EnsureBarbersAsync(w, ct);
        await AddOnAsync(w, clock, ct);
        await OkAsync(w.OwnerA.PutAsync("/api/v1/shop/loyalty", Rules(first: 2, visit: 1, reward: 5), ct), ct);
        var target = TargetFrom(clock);
        var (noura, phone) = await NamedCustomerAsync(w.Factory, "نورة", ct);
        var customerId = (await OkAsync(noura.GetAsync("/api/v1/me", ct), ct)).GetProperty("id").GetGuid();

        // A D-132 coupon, issued under the old programme.
        Guid couponId;
        await using (var scope = w.Factory.Services.CreateAsyncScope())
        {
            using var system = scope.ServiceProvider.GetRequiredService<ISystemDataScope>().Begin();
            var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
            var program = LoyaltyProgram.Create(
                EntityId.New<LoyaltyProgramId>(), new ShopId(w.Shops.A.ShopId), new LoyaltyTerms(true, 2, LoyaltyRewardKind.FreeService, null, "حلاقة مجانية", "Free haircut", null),
                clock.GetUtcNow()).Value;
            var coupon = LoyaltyReward.Issue(EntityId.New<LoyaltyRewardId>(), program, customerId, LoyaltyCard.NewCode(), clock.GetUtcNow());
            db.Add(coupon);
            await db.SaveChangesAsync(ct);
            couponId = coupon.Id.Value;
        }

        (await OkAsync(noura.GetAsync($"/api/v1/me/loyalty/shops/{w.SlugA}", ct), ct)).GetProperty("coupons").EnumerateArray().Single().GetProperty("status").GetString().ShouldBe("Available");
        var held = await OkAsync(BookAsync(noura, w.SlugA, At(target, 10), ct, serviceId: w.Haircut, professionalId: w.Faisal, couponId: couponId), ct, HttpStatusCode.Created);
        held.GetProperty("loyaltyReward").GetProperty("titleAr").GetString().ShouldBe("حلاقة مجانية");
        await OkAsync(noura.PostAsync($"/api/v1/me/bookings/{held.GetProperty("id").GetGuid()}/cancel", new { reason = (string?)null, version = held.GetProperty("version").GetUInt32() }, ct), ct);
        await ProcessOutboxAsync(w.Factory, ct);
        (await OkAsync(noura.GetAsync($"/api/v1/me/loyalty/shops/{w.SlugA}", ct), ct)).GetProperty("coupons")[0].GetProperty("status").GetString().ShouldBe("Available");

        var used = (await OkAsync(BookAsync(noura, w.SlugA, At(target, 11), ct, serviceId: w.Haircut, professionalId: w.Faisal, couponId: couponId), ct, HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        noura.Dispose();
        await CloseAsync(w, clock, used, At(target, 11), ct);
        var card = await CardAsync(w.Factory, phone, w.SlugA, ct);
        card.GetProperty("coupons").GetArrayLength().ShouldBe(0);
        card.GetProperty("points").GetInt32().ShouldBe(0);
        (await ScalarAsync(w.Factory, "SELECT status FROM loyalty.rewards WHERE id = @p", ct, couponId)).ShouldBe("Redeemed");
    }
}
