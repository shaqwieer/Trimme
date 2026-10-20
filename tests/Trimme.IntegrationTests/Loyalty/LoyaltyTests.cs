using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Trimme.BuildingBlocks.Web.Jobs;
using Trimme.IntegrationTests.Bookings;
using Trimme.IntegrationTests.Identity;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.Modules.Identity.Domain;
using static Trimme.IntegrationTests.Bookings.BookingTestData;

namespace Trimme.IntegrationTests.Loyalty;

/// <summary>
/// The shop's stamp card (D-132): the admin sets the terms; every completed online booking at the shop, with any of its
/// professionals, earns a stamp; a full card becomes a reward the customer chooses when booking; it is released by a
/// cancellation and redeemed by the completed visit, which earns no stamp itself. The first visit can fill several
/// circles (D-133), and every salon keeps its own card.
/// </summary>
public sealed class LoyaltyTests(PostgresFixture postgres)
{
    private static DateOnly Target => TodayAt(DateTimeOffset.UtcNow).AddDays(2);

    private static object Terms(
        bool active = true, int visits = 2, string kind = "FreeService", int? percent = null, string? titleAr = "حلاقة مجانية", uint? version = null, int? firstVisit = null) =>
        new
        {
            isActive = active, requiredVisits = visits, firstVisitStamps = firstVisit, rewardKind = kind, discountPercent = percent, rewardTitleAr = titleAr,
            rewardTitleEn = "Free haircut", rewardValidityDays = (int?)null, version,
        };

    private static async Task ProcessOutboxAsync(TrimmeApiFactory factory, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
        for (var round = 0; round < 10 && await processor.ProcessBatchAsync(ct) > 0; round++)
        {
        }
    }

    private static async Task CompleteAsync(BookingWorld w, FakeTimeProvider clock, Guid bookingId, DateTimeOffset start, CancellationToken ct, string? staffEmail = null)
    {
        clock.SetUtcNow(start.AddMinutes(40).ToUniversalTime());
        using var staff = await IdentityTestData.SignInStaffAsync(w.Factory, staffEmail ?? w.Shops.A.StaffEmail, ct);
        foreach (var to in new[] { "Arrived", "Completed" })
        {
            var current = await OkAsync(staff.GetAsync($"/api/v1/shop/bookings/{bookingId}", ct), ct);
            await OkAsync(staff.PostAsync($"/api/v1/shop/bookings/{bookingId}/transitions", new { to, version = current.GetProperty("booking").GetProperty("version").GetUInt32() }, ct), ct);
        }

        await ProcessOutboxAsync(w.Factory, ct);
    }

    private static Task<HttpResponseMessage> BookWithRewardAsync(ApiSession customer, string slug, Guid serviceId, DateTimeOffset start, Guid rewardId, CancellationToken ct) =>
        customer.SendAsync(
            HttpMethod.Post, "/api/v1/bookings", new { shopSlug = slug, serviceId, professionalId = (Guid?)null, startsAt = start, loyaltyRewardId = rewardId }, ct, headers: Key());

    private static async Task<(ApiSession Session, string Phone)> NamedCustomerAsync(TrimmeApiFactory factory, string name, CancellationToken ct)
    {
        var phone = IdentityTestData.NewPhone();
        var session = await IdentityTestData.SignInCustomerAsync(factory, phone, ct);
        await OkAsync(session.PostAsync("/api/v1/auth/profile/complete", new { displayName = name, preferredLocale = "ar", termsAccepted = true }, ct), ct);
        return (session, phone);
    }

    private static Task<JsonElement> CardAsync(ApiSession customer, string slug, CancellationToken ct) => OkAsync(customer.GetAsync($"/api/v1/me/loyalty/shops/{slug}", ct), ct);

    [Fact]
    public async Task FullCard_BecomesAReward_HeldByABooking_ReleasedByCancel_RedeemedByTheVisit()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var w = await ArrangeAsync(postgres, "loyalty_card", ct, clock);
        await OkAsync(w.Admin.PutAsync($"/api/v1/admin/shops/{w.Shops.A.ShopId}/loyalty", Terms(), ct), ct);
        var slugB = (await OkAsync(w.Admin.GetAsync($"/api/v1/admin/shops/{w.Shops.B.ShopId}", ct), ct)).GetProperty("slug").GetString()!;

        var offer = await OkAsync(ApiSession.Create(w.Factory).GetAsync($"/api/v1/public/shops/{w.SlugA}/loyalty", ct), ct);
        offer.GetProperty("program").GetProperty("requiredVisits").GetInt32().ShouldBe(2);
        offer.GetProperty("program").GetProperty("rewardTitleAr").GetString().ShouldBe("حلاقة مجانية");

        var (noura, nouraPhone) = await NamedCustomerAsync(w.Factory, "نورة", ct);
        var (khalid, khalidPhone) = await NamedCustomerAsync(w.Factory, "خالد", ct);
        var first = (await OkAsync(BookAsync(noura, w.SlugA, w.Haircut, w.Faisal, At(Target, 10), ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var second = (await OkAsync(BookAsync(noura, w.SlugA, w.Haircut, w.Omar, At(Target, 11), ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        (await CardAsync(noura, w.SlugA, ct)).GetProperty("stamps").GetInt32().ShouldBe(0);

        // Two completed visits with two different professionals of the same shop fill the card.
        await CompleteAsync(w, clock, first, At(Target, 10), ct);
        using (var again = await IdentityTestData.SignInCustomerAsync(w.Factory, nouraPhone, ct))
        {
            (await CardAsync(again, w.SlugA, ct)).GetProperty("stamps").GetInt32().ShouldBe(1);
        }

        await CompleteAsync(w, clock, second, At(Target, 11), ct);
        using var nouraNow = await IdentityTestData.SignInCustomerAsync(w.Factory, nouraPhone, ct);
        var card = await CardAsync(nouraNow, w.SlugA, ct);
        card.GetProperty("stamps").GetInt32().ShouldBe(0);
        var reward = card.GetProperty("rewards").EnumerateArray().ShouldHaveSingleItem();
        reward.GetProperty("status").GetString().ShouldBe("Available");
        reward.GetProperty("code").GetString()!.Length.ShouldBe(8);
        var rewardId = reward.GetProperty("id").GetGuid();
        (await OkAsync(nouraNow.GetAsync("/api/v1/me/loyalty", ct), ct)).GetProperty("items").GetArrayLength().ShouldBe(1);

        // Nobody else can use it, and it is good only at its own shop.
        using (var khalidNow = await IdentityTestData.SignInCustomerAsync(w.Factory, khalidPhone, ct))
        {
            await FailsAsync(BookWithRewardAsync(khalidNow, w.SlugA, w.Haircut, At(Target, 15), rewardId, ct), HttpStatusCode.UnprocessableEntity, "loyalty.reward_unavailable", ct);
            (await CardAsync(khalidNow, w.SlugA, ct)).GetProperty("rewards").GetArrayLength().ShouldBe(0);
        }

        await FailsAsync(BookWithRewardAsync(nouraNow, slugB, w.ServiceB, At(Target, 15), rewardId, ct), HttpStatusCode.UnprocessableEntity, "loyalty.reward_unavailable", ct);

        // Chosen for a booking: held, shown to the shop, and not usable twice.
        var held = await OkAsync(BookWithRewardAsync(nouraNow, w.SlugA, w.Haircut, At(Target, 15), rewardId, ct), ct, HttpStatusCode.Created);
        held.GetProperty("loyaltyReward").GetProperty("titleAr").GetString().ShouldBe("حلاقة مجانية");
        var heldId = held.GetProperty("id").GetGuid();
        (await CardAsync(nouraNow, w.SlugA, ct)).GetProperty("rewards")[0].GetProperty("status").GetString().ShouldBe("Held");
        await FailsAsync(BookWithRewardAsync(nouraNow, w.SlugA, w.Haircut, At(Target, 17), rewardId, ct), HttpStatusCode.UnprocessableEntity, "loyalty.reward_unavailable", ct);
        using var owner = await IdentityTestData.SignInStaffAsync(w.Factory, w.Shops.A.OwnerEmail, ct);
        (await OkAsync(owner.GetAsync($"/api/v1/shop/bookings/{heldId}", ct), ct))
            .GetProperty("booking").GetProperty("loyaltyReward").GetProperty("titleEn").GetString().ShouldBe("Free haircut");

        // Cancelled: the reward comes back.
        await OkAsync(nouraNow.PostAsync($"/api/v1/me/bookings/{heldId}/cancel", new { reason = (string?)null, version = held.GetProperty("version").GetUInt32() }, ct), ct);
        await ProcessOutboxAsync(w.Factory, ct);
        (await CardAsync(nouraNow, w.SlugA, ct)).GetProperty("rewards")[0].GetProperty("status").GetString().ShouldBe("Available");

        // Used on a completed visit: redeemed, and that visit earns no stamp.
        var used = (await OkAsync(BookWithRewardAsync(nouraNow, w.SlugA, w.Haircut, At(Target, 16), rewardId, ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await CompleteAsync(w, clock, used, At(Target, 16), ct);
        using var nouraLater = await IdentityTestData.SignInCustomerAsync(w.Factory, nouraPhone, ct);
        var after = await CardAsync(nouraLater, w.SlugA, ct);
        after.GetProperty("stamps").GetInt32().ShouldBe(0);
        after.GetProperty("rewards").GetArrayLength().ShouldBe(0);

        using var admin = await IdentityTestData.SignInNewStaffAsync(w.Factory, SystemRoles.SuperAdmin, ct);
        var stats = (await OkAsync(admin.GetAsync($"/api/v1/admin/shops/{w.Shops.A.ShopId}/loyalty", ct), ct)).GetProperty("stats");
        stats.GetProperty("members").GetInt32().ShouldBe(1);
        stats.GetProperty("rewardsIssued").GetInt32().ShouldBe(1);
        stats.GetProperty("rewardsRedeemed").GetInt32().ShouldBe(1);
        noura.Dispose();
        khalid.Dispose();
    }

    [Fact]
    public async Task FirstVisit_FillsTheBonusCircles_ThenOnePerVisit_AndEachSalonKeepsItsOwnCard()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var w = await ArrangeAsync(postgres, "loyalty_bonus", ct, clock);
        // Salon A: four circles, and the first visit fills three of them. Salon B: three circles, no bonus.
        await OkAsync(w.Admin.PutAsync($"/api/v1/admin/shops/{w.Shops.A.ShopId}/loyalty", Terms(visits: 4, firstVisit: 3), ct), ct);
        await OkAsync(w.Admin.PutAsync($"/api/v1/admin/shops/{w.Shops.B.ShopId}/loyalty", Terms(visits: 3), ct), ct);
        var slugB = (await OkAsync(w.Admin.GetAsync($"/api/v1/admin/shops/{w.Shops.B.ShopId}", ct), ct)).GetProperty("slug").GetString()!;
        (await OkAsync(ApiSession.Create(w.Factory).GetAsync($"/api/v1/public/shops/{w.SlugA}/loyalty", ct), ct))
            .GetProperty("program").GetProperty("firstVisitStamps").GetInt32().ShouldBe(3);
        (await OkAsync(ApiSession.Create(w.Factory).GetAsync($"/api/v1/public/shops/{slugB}/loyalty", ct), ct))
            .GetProperty("program").GetProperty("firstVisitStamps").GetInt32().ShouldBe(1);

        var (noura, nouraPhone) = await NamedCustomerAsync(w.Factory, "نورة", ct);
        // In time order, as the fake clock only moves forward.
        var first = (await OkAsync(BookAsync(noura, w.SlugA, w.Haircut, w.Faisal, At(Target, 10), ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var atB = (await OkAsync(BookAsync(noura, slugB, w.ServiceB, w.ProB, At(Target, 11), ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var second = (await OkAsync(BookAsync(noura, w.SlugA, w.Haircut, w.Omar, At(Target, 12), ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var third = (await OkAsync(BookAsync(noura, w.SlugA, w.Haircut, w.Faisal, At(Target, 13), ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        noura.Dispose();

        async Task<JsonElement> CardsAsync()
        {
            using var session = await IdentityTestData.SignInCustomerAsync(w.Factory, nouraPhone, ct);
            return (await OkAsync(session.GetAsync("/api/v1/me/loyalty", ct), ct)).GetProperty("items");
        }

        int Stamps(JsonElement cards, string slug) =>
            cards.EnumerateArray().Single(c => c.GetProperty("shop").GetProperty("slug").GetString() == slug).GetProperty("stamps").GetInt32();

        // The first visit at A fills three circles; the first at B fills one: two salons, two cards.
        await CompleteAsync(w, clock, first, At(Target, 10), ct);
        Stamps(await CardsAsync(), w.SlugA).ShouldBe(3);
        await CompleteAsync(w, clock, atB, At(Target, 11), ct, w.Shops.B.OwnerEmail);
        var cards = await CardsAsync();
        cards.GetArrayLength().ShouldBe(2);
        Stamps(cards, w.SlugA).ShouldBe(3);
        Stamps(cards, slugB).ShouldBe(1);

        // The second visit at A fills the fourth circle: the card becomes a coupon and starts again.
        await CompleteAsync(w, clock, second, At(Target, 12), ct);
        cards = await CardsAsync();
        Stamps(cards, w.SlugA).ShouldBe(0);
        cards.EnumerateArray().Single(c => c.GetProperty("shop").GetProperty("slug").GetString() == w.SlugA)
            .GetProperty("rewards").EnumerateArray().ShouldHaveSingleItem().GetProperty("status").GetString().ShouldBe("Available");

        // Only the very first visit gets the bonus: the next card fills one circle per visit.
        await CompleteAsync(w, clock, third, At(Target, 13), ct);
        Stamps(await CardsAsync(), w.SlugA).ShouldBe(1);
        using var admin = await IdentityTestData.SignInNewStaffAsync(w.Factory, SystemRoles.SuperAdmin, ct);
        var stats = (await OkAsync(admin.GetAsync($"/api/v1/admin/shops/{w.Shops.A.ShopId}/loyalty", ct), ct)).GetProperty("stats");
        stats.GetProperty("members").GetInt32().ShouldBe(1);
        stats.GetProperty("openStamps").GetInt32().ShouldBe(1);
        stats.GetProperty("rewardsIssued").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Programme_IsSetByTheAdminOnly_Validated_AndReadOnlyForTheShop()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var w = await ArrangeAsync(postgres, "loyalty_admin", ct, clock);
        var url = $"/api/v1/admin/shops/{w.Shops.A.ShopId}/loyalty";

        await FailsAsync(w.OwnerA.PutAsync(url, Terms(), ct), HttpStatusCode.Forbidden, null, ct);
        (await OkAsync(w.Admin.GetAsync(url, ct), ct)).GetProperty("program").ValueKind.ShouldBe(JsonValueKind.Null);

        var invalid = await FailsAsync(w.Admin.PutAsync(url, Terms(visits: 1, kind: "Discount", titleAr: " "), ct), HttpStatusCode.BadRequest, null, ct);
        invalid.ShouldContain("requiredVisits");
        invalid.ShouldContain("discountPercent");
        invalid.ShouldContain("rewardTitleAr");

        // The first visit can fill fewer circles than the card has, never all of them (D-133).
        foreach (var firstVisit in new[] { 0, 5 })
        {
            (await FailsAsync(w.Admin.PutAsync(url, Terms(visits: 5, firstVisit: firstVisit), ct), HttpStatusCode.BadRequest, null, ct)).ShouldContain("firstVisitStamps");
        }

        var saved = await OkAsync(w.Admin.PutAsync(url, Terms(active: false, visits: 5, kind: "Discount", percent: 20), ct), ct);
        var version = saved.GetProperty("version").GetUInt32();
        await FailsAsync(w.Admin.PutAsync(url, Terms(visits: 6, version: version + 1), ct), HttpStatusCode.Conflict, null, ct);

        var shop = await OkAsync(w.StaffA.GetAsync("/api/v1/shop/loyalty", ct), ct);
        shop.GetProperty("program").GetProperty("discountPercent").GetInt32().ShouldBe(20);
        shop.GetProperty("program").GetProperty("isActive").GetBoolean().ShouldBeFalse();
        (await OkAsync(w.OwnerB.GetAsync("/api/v1/shop/loyalty", ct), ct)).GetProperty("program").ValueKind.ShouldBe(JsonValueKind.Null);

        // A switched-off programme is not offered and earns no stamps.
        (await OkAsync(ApiSession.Create(w.Factory).GetAsync($"/api/v1/public/shops/{w.SlugA}/loyalty", ct), ct))
            .GetProperty("program").ValueKind.ShouldBe(JsonValueKind.Null);
        var (noura, nouraPhone) = await NamedCustomerAsync(w.Factory, "نورة", ct);
        var visit = (await OkAsync(BookAsync(noura, w.SlugA, w.Haircut, w.Faisal, At(Target, 10), ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await CompleteAsync(w, clock, visit, At(Target, 10), ct);
        using var again = await IdentityTestData.SignInCustomerAsync(w.Factory, nouraPhone, ct);
        (await CardAsync(again, w.SlugA, ct)).GetProperty("stamps").GetInt32().ShouldBe(0);
        noura.Dispose();
    }
}
