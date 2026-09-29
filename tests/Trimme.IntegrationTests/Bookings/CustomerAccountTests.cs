using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Scheduling;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.IntegrationTests.Identity;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.Modules.Bookings.Application;
using Trimme.Modules.Bookings.Domain;
using Trimme.Modules.Customers.Domain;
using Trimme.Modules.Reviews.Domain;
using static Trimme.IntegrationTests.Bookings.BookingTestData;

namespace Trimme.IntegrationTests.Bookings;

/// <summary>
/// The customer's account (Phase 12, D-097/D-098): R-CUS-09 reviews of their own completed visits, once, within the
/// window, with exact rating totals under concurrency (R-RVW-01); R-CUS-10 favorites that only their owner sees or
/// changes; R-BKG-09 reschedule availability for their own active bookings.
/// </summary>
public sealed class CustomerAccountTests(PostgresFixture postgres)
{
    private static DateOnly Target => TodayAt(DateTimeOffset.UtcNow).AddDays(2);

    private static async Task<(ApiSession Session, string Phone)> NamedCustomerAsync(TrimmeApiFactory factory, string name, CancellationToken ct)
    {
        var phone = IdentityTestData.NewPhone();
        var session = await IdentityTestData.SignInCustomerAsync(factory, phone, ct);
        await OkAsync(session.PostAsync("/api/v1/auth/profile/complete", new { displayName = name, preferredLocale = "ar", termsAccepted = true }, ct), ct);
        return (session, phone);
    }

    private static Task<HttpResponseMessage> ReviewAsync(ApiSession customer, Guid bookingId, int rating, CancellationToken ct, string[]? tags = null, string? comment = null) =>
        customer.PostAsync($"/api/v1/me/bookings/{bookingId}/review", new { rating, tags, comment }, ct);

    private static async Task<JsonElement> TransitionAsync(ApiSession staff, Guid bookingId, string to, CancellationToken ct)
    {
        var current = await OkAsync(staff.GetAsync($"/api/v1/shop/bookings/{bookingId}", ct), ct);
        return await OkAsync(staff.PostAsync($"/api/v1/shop/bookings/{bookingId}/transitions", new { to, version = current.GetProperty("booking").GetProperty("version").GetUInt32() }, ct), ct);
    }

    /// <summary>Completed visits with Faisal at shop A, one hour apart, ending before <paramref name="before"/> (written directly, as the demo seed does).</summary>
    private static async Task<List<Guid>> SeedCompletedAsync(BookingWorld w, Guid customerId, string name, int count, DateTimeOffset before, CancellationToken ct)
    {
        await using var scope = w.Factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        using var system = services.GetRequiredService<ISystemDataScope>().Begin();
        var db = services.GetRequiredService<TrimmeDbContext>();
        var shop = new ShopId(w.Shops.A.ShopId);
        var offer = (await services.GetRequiredService<IBookableOfferCatalog>().FindAsync(shop, w.Haircut, null, ct)).ShouldNotBeNull();
        var faisal = (await services.GetRequiredService<IProfessionalDirectory>().FindAsync(new ProfessionalId(w.Faisal), ct)).ShouldNotBeNull();
        var ids = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var id = Guid.CreateVersion7();
            var start = before.AddHours(-(i + 1));
            db.Add(Booking.Seeded(
                new BookingId(id), shop, customerId, name, new BookedProfessional(faisal.Id, faisal.NameAr, faisal.NameEn), BookingMapping.Snapshot(offer),
                start, BookingChannel.Online, [BookingStatus.Confirmed, BookingStatus.Arrived, BookingStatus.Completed], null, start.AddDays(-1)));
            ids.Add(id);
        }

        await db.SaveChangesAsync(ct);
        return ids;
    }

    private static async Task<Guid> MeAsync(ApiSession session, CancellationToken ct) =>
        (await OkAsync(session.GetAsync("/api/v1/me", ct), ct)).GetProperty("id").GetGuid();

    [Fact]
    public async Task Review_IsForTheCustomersOwnCompletedVisit_OnceWithinTheWindow_AndUpdatesTheRatings()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var w = await ArrangeAsync(postgres, "acct_review", ct, clock);
        var (noura, nouraPhone) = await NamedCustomerAsync(w.Factory, "نورة السبيعي", ct);
        var (khalid, _) = await NamedCustomerAsync(w.Factory, "خالد", ct);
        var visit = (await OkAsync(BookAsync(noura, w.SlugA, w.Haircut, w.Faisal, At(Target, 10), ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var later = (await OkAsync(BookAsync(noura, w.SlugA, w.Haircut, w.Faisal, At(Target, 12), ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();

        // An upcoming visit cannot be reviewed.
        await FailsAsync(ReviewAsync(noura, visit, 5, ct), HttpStatusCode.Conflict, "review.booking_not_completed", ct);
        noura.Dispose();
        khalid.Dispose();

        // The visit happens and the shop completes it.
        clock.SetUtcNow(At(Target, 10, 40).ToUniversalTime());
        using (var staff = await IdentityTestData.SignInStaffAsync(w.Factory, w.Shops.A.StaffEmail, ct))
        {
            await TransitionAsync(staff, visit, "Arrived", ct);
            (await TransitionAsync(staff, visit, "Completed", ct)).GetProperty("status").GetString().ShouldBe("Completed");
        }

        using var nouraAgain = await IdentityTestData.SignInCustomerAsync(w.Factory, nouraPhone, ct);
        using var khalidAgain = await BookingTestData.CustomerAsync(w.Factory, "خالد", ct);
        var mine = await OkAsync(nouraAgain.GetAsync($"/api/v1/me/bookings/{visit}", ct), ct);
        mine.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()).ShouldBe(["Review"]);
        mine.GetProperty("reviewDeadline").GetDateTimeOffset().ShouldBe(At(Target, 10, 40).AddDays(7), TimeSpan.FromMinutes(1));

        // Another customer's booking is simply not found; bad input is refused before anything is written.
        await FailsAsync(ReviewAsync(khalidAgain, visit, 5, ct), HttpStatusCode.NotFound, "booking.not_found", ct);
        await FailsAsync(ReviewAsync(nouraAgain, Guid.NewGuid(), 5, ct), HttpStatusCode.NotFound, "booking.not_found", ct);
        await FailsAsync(ReviewAsync(nouraAgain, visit, 6, ct), HttpStatusCode.BadRequest, null, ct);
        await FailsAsync(ReviewAsync(nouraAgain, visit, 4, ct, comment: new string('x', Review.MaxCommentLength + 1)), HttpStatusCode.BadRequest, null, ct);

        var review = await OkAsync(ReviewAsync(nouraAgain, visit, 4, ct, ["Quality", "Punctuality", "Quality"], "  ممتاز  "), ct, HttpStatusCode.Created);
        review.GetProperty("rating").GetInt32().ShouldBe(4);
        review.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ShouldBe(["Punctuality", "Quality"]);
        review.GetProperty("comment").GetString().ShouldBe("ممتاز");
        await FailsAsync(ReviewAsync(nouraAgain, visit, 5, ct), HttpStatusCode.Conflict, "review.already_exists", ct);

        // Published at once with the first name and initial; the shop and professional totals follow.
        using var anonymous = ApiSession.Create(w.Factory);
        var shopReviews = await OkAsync(anonymous.GetAsync($"/api/v1/public/shops/{w.SlugA}/reviews", ct), ct);
        shopReviews.GetProperty("summary").GetProperty("count").GetInt32().ShouldBe(1);
        shopReviews.GetProperty("summary").GetProperty("average").GetDecimal().ShouldBe(4m);
        shopReviews.GetProperty("reviews").GetProperty("items")[0].GetProperty("authorName").GetString().ShouldBe("نورة س.");
        shopReviews.ToString().ShouldNotContain("+966");
        (await OkAsync(anonymous.GetAsync($"/api/v1/public/shops/{w.SlugA}/reviews?professionalId={w.Faisal}", ct), ct))
            .GetProperty("summary").GetProperty("count").GetInt32().ShouldBe(1);
        (await OkAsync(anonymous.GetAsync($"/api/v1/public/shops/{w.SlugA}/reviews?professionalId={w.Omar}", ct), ct))
            .GetProperty("summary").GetProperty("count").GetInt32().ShouldBe(0);

        var reviewed = await OkAsync(nouraAgain.GetAsync($"/api/v1/me/bookings/{visit}", ct), ct);
        reviewed.GetProperty("reviewRating").GetInt32().ShouldBe(4);
        reviewed.GetProperty("allowedActions").GetArrayLength().ShouldBe(0);

        // The second visit is completed too, but the review window (7 days) passes first.
        clock.SetUtcNow(At(Target, 12, 40).ToUniversalTime());
        using (var staff = await IdentityTestData.SignInStaffAsync(w.Factory, w.Shops.A.StaffEmail, ct))
        {
            await TransitionAsync(staff, later, "Arrived", ct);
            await TransitionAsync(staff, later, "Completed", ct);
        }

        clock.SetUtcNow(At(Target, 12, 40).AddDays(8).ToUniversalTime());
        using var nouraLate = await IdentityTestData.SignInCustomerAsync(w.Factory, nouraPhone, ct);
        (await OkAsync(nouraLate.GetAsync($"/api/v1/me/bookings/{later}", ct), ct)).GetProperty("allowedActions").GetArrayLength().ShouldBe(0);
        await FailsAsync(ReviewAsync(nouraLate, later, 5, ct), HttpStatusCode.UnprocessableEntity, "review.window_closed", ct);
    }

    [Fact]
    public async Task Reviews_SubmittedTwiceAtOnce_OrInParallel_KeepOneReviewPerVisit_AndExactTotals()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "acct_review_race", ct);
        var (noura, _) = await NamedCustomerAsync(w.Factory, "نورة", ct);
        using var _ = noura;
        var visits = await SeedCompletedAsync(w, await MeAsync(noura, ct), "نورة", 7, DateTimeOffset.UtcNow.AddHours(-2), ct);

        // A double tap: exactly one 201, the other 409 review.already_exists (the unique booking index decides).
        var twice = await Task.WhenAll(ReviewAsync(noura, visits[0], 5, ct), ReviewAsync(noura, visits[0], 5, ct));
        var codes = twice.Select(r => r.StatusCode).Order().ToList();
        codes.ShouldBe([HttpStatusCode.Created, HttpStatusCode.Conflict]);
        (await twice.Single(r => r.StatusCode == HttpStatusCode.Conflict).Content.ReadAsStringAsync(ct)).ShouldContain("review.already_exists");
        foreach (var response in twice)
        {
            response.Dispose();
        }

        // Six visits reviewed at once: the atomic upsert loses no update (R-RVW-01).
        var stars = new[] { 1, 2, 3, 4, 5, 5 };
        var parallel = await Task.WhenAll(visits.Skip(1).Select((id, i) => ReviewAsync(noura, id, stars[i], ct)));
        parallel.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Created);
        foreach (var response in parallel)
        {
            response.Dispose();
        }

        await using var scope = w.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
        var totals = await db.Set<RatingAggregate>().AsNoTracking().Where(a => a.ShopId == new ShopId(w.Shops.A.ShopId)).ToListAsync(ct);
        foreach (var subject in new[] { RatingSubjectKind.Shop, RatingSubjectKind.Professional })
        {
            var row = totals.Single(a => a.Subject == subject);
            row.Count.ShouldBe(7);
            row.Sum.ShouldBe(5 + stars.Sum());
            row.Histogram.ShouldBe([1, 1, 1, 1, 3]);
        }

        using var system = scope.ServiceProvider.GetRequiredService<ISystemDataScope>().Begin();
        (await db.Set<Review>().CountAsync(ct)).ShouldBe(7);
    }

    [Fact]
    public async Task Favorites_AreTheCustomersOwn_Idempotent_AndOnlyForListedShopsAndActiveProfessionals()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "acct_favorites", ct);
        await LocateAsync(w, w.Shops.A.ShopId, "الملقا", ct);
        var (a, _) = await NamedCustomerAsync(w.Factory, "نورة", ct);
        var (b, _) = await NamedCustomerAsync(w.Factory, "خالد", ct);
        using var customerA = a;
        using var customerB = b;
        var shopA = w.Shops.A.ShopId;

        await OkAsync(customerA.PutAsync($"/api/v1/me/favorites/shops/{shopA}", null, ct), ct, HttpStatusCode.NoContent);
        await OkAsync(customerA.PutAsync($"/api/v1/me/favorites/shops/{shopA}", null, ct), ct, HttpStatusCode.NoContent);
        await OkAsync(customerA.PutAsync($"/api/v1/me/favorites/professionals/{w.Faisal}", new { shopId = shopA }, ct), ct, HttpStatusCode.NoContent);

        // Not listed in discovery: shop B has no location; an unknown shop; a professional under the wrong shop.
        await FailsAsync(customerA.PutAsync($"/api/v1/me/favorites/shops/{w.Shops.B.ShopId}", null, ct), HttpStatusCode.NotFound, "shop.not_found", ct);
        await FailsAsync(customerA.PutAsync($"/api/v1/me/favorites/shops/{Guid.NewGuid()}", null, ct), HttpStatusCode.NotFound, "shop.not_found", ct);
        await FailsAsync(customerA.PutAsync($"/api/v1/me/favorites/professionals/{w.ProB}", new { shopId = shopA }, ct), HttpStatusCode.NotFound, "professional.not_found", ct);

        var list = await OkAsync(customerA.GetAsync("/api/v1/me/favorites", ct), ct);
        list.GetProperty("shops").GetArrayLength().ShouldBe(1);
        list.GetProperty("shops")[0].GetProperty("slug").GetString().ShouldBe(w.SlugA);
        list.GetProperty("professionals")[0].GetProperty("id").GetGuid().ShouldBe(w.Faisal);
        list.GetProperty("professionals")[0].GetProperty("shopSlug").GetString().ShouldBe(w.SlugA);
        list.GetProperty("shopIds").EnumerateArray().Select(e => e.GetGuid()).ShouldBe([shopA]);
        list.ToString().ShouldNotContain("+966");

        // Customer B sees none of A's favorites, and removing "them" changes nothing for A.
        var other = await OkAsync(customerB.GetAsync("/api/v1/me/favorites", ct), ct);
        other.GetProperty("shopIds").GetArrayLength().ShouldBe(0);
        other.GetProperty("professionalIds").GetArrayLength().ShouldBe(0);
        await OkAsync(customerB.DeleteAsync($"/api/v1/me/favorites/shops/{shopA}", ct), ct, HttpStatusCode.NoContent);
        await OkAsync(customerB.DeleteAsync($"/api/v1/me/favorites/professionals/{w.Faisal}", ct), ct, HttpStatusCode.NoContent);
        (await OkAsync(customerA.GetAsync("/api/v1/me/favorites", ct), ct)).GetProperty("professionalIds").GetArrayLength().ShouldBe(1);

        // The data layer refuses a favorite written for someone else (D-085).
        await using (var scope = w.Factory.Services.CreateAsyncScope())
        {
            var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<TrimmeDbContext>>();
            var contributors = scope.ServiceProvider.GetRequiredService<IEnumerable<IModelContributor>>();
            await using var asB = new TrimmeDbContext(options, contributors, tenant: null, new FixedCustomer(await MeAsync(customerB, ct)));
            asB.Add(Favorite.ForShop(new FavoriteId(Guid.CreateVersion7()), await MeAsync(customerA, ct), new ShopId(shopA), DateTimeOffset.UtcNow));
            await Should.ThrowAsync<TenantViolationException>(() => asB.SaveChangesAsync(ct));
        }

        // A disabled professional leaves the list and cannot be saved; the saved row stays for when they return.
        await OkAsync(w.Admin.PostAsync($"/api/v1/admin/professionals/{w.Faisal}/disable", new { reason = "إجازة" }, ct), ct);
        await FailsAsync(customerB.PutAsync($"/api/v1/me/favorites/professionals/{w.Faisal}", new { shopId = shopA }, ct), HttpStatusCode.NotFound, "professional.not_found", ct);
        var afterDisable = await OkAsync(customerA.GetAsync("/api/v1/me/favorites", ct), ct);
        afterDisable.GetProperty("professionals").GetArrayLength().ShouldBe(0);
        afterDisable.GetProperty("professionalIds").GetArrayLength().ShouldBe(1);

        // A suspended shop leaves the list too.
        await OkAsync(w.Admin.PostAsync($"/api/v1/admin/shops/{shopA}/suspend", new { reason = "مراجعة" }, ct), ct);
        (await OkAsync(customerA.GetAsync("/api/v1/me/favorites", ct), ct)).GetProperty("shops").GetArrayLength().ShouldBe(0);

        await OkAsync(customerA.DeleteAsync($"/api/v1/me/favorites/shops/{shopA}", ct), ct, HttpStatusCode.NoContent);
        await OkAsync(customerA.DeleteAsync($"/api/v1/me/favorites/shops/{shopA}", ct), ct, HttpStatusCode.NoContent);
        (await OkAsync(customerA.GetAsync("/api/v1/me/favorites", ct), ct)).GetProperty("shopIds").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task RescheduleAvailability_IsForTheCustomersOwnActiveBookings_AndTheBookingsOwnTimeIsFree()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "acct_reschedule", ct);
        var (noura, _) = await NamedCustomerAsync(w.Factory, "نورة", ct);
        var (khalid, _) = await NamedCustomerAsync(w.Factory, "خالد", ct);
        using var mine = noura;
        using var other = khalid;
        var ten = At(Target, 10);
        var booking = await OkAsync(BookAsync(mine, w.SlugA, w.Haircut, w.Faisal, ten, ct), ct, HttpStatusCode.Created);
        var id = booking.GetProperty("id").GetGuid();

        // Khalid takes 11:00 with Faisal: that start is not offered for Noura's move.
        await OkAsync(BookAsync(other, w.SlugA, w.Haircut, w.Faisal, At(Target, 11), ct), ct, HttpStatusCode.Created);

        var slots = await OkAsync(mine.GetAsync($"/api/v1/me/bookings/{id}/reschedule/slots?date={Iso(Target)}", ct), ct);
        slots.GetProperty("bookable").GetBoolean().ShouldBeTrue();
        var starts = slots.GetProperty("slots").EnumerateArray().Select(s => s.GetProperty("startsAt").GetDateTimeOffset()).ToList();
        starts.ShouldContain(ten, "the booking's own time does not block its move");
        starts.ShouldContain(ten.AddMinutes(15), "a few minutes later overlaps only the booking itself");
        starts.ShouldNotContain(At(Target, 11));
        starts.ShouldNotContain(At(Target, 10, 45), "it would overlap Khalid's 11:00");

        var dates = await OkAsync(mine.GetAsync($"/api/v1/me/bookings/{id}/reschedule/dates?from={Iso(Target)}&to={Iso(Target.AddDays(2))}", ct), ct);
        dates.GetProperty("dates").EnumerateArray().Select(d => d.GetProperty("date").GetString()).ShouldBe([Iso(Target), Iso(Target.AddDays(1)), Iso(Target.AddDays(2))]);
        dates.GetProperty("dates")[0].GetProperty("slotCount").GetInt32().ShouldBe(starts.Count);
        await FailsAsync(mine.GetAsync($"/api/v1/me/bookings/{id}/reschedule/dates?from={Iso(Target)}&to={Iso(Target.AddDays(40))}", ct), HttpStatusCode.BadRequest, "validation.out_of_range", ct);

        // Only the customer's own active bookings.
        await FailsAsync(other.GetAsync($"/api/v1/me/bookings/{id}/reschedule/slots?date={Iso(Target)}", ct), HttpStatusCode.NotFound, "booking.not_found", ct);
        await OkAsync(mine.PostAsync($"/api/v1/me/bookings/{id}/cancel", new { reason = (string?)null, version = booking.GetProperty("version").GetUInt32() }, ct), ct);
        await FailsAsync(mine.GetAsync($"/api/v1/me/bookings/{id}/reschedule/slots?date={Iso(Target)}", ct), HttpStatusCode.Conflict, null, ct);
    }

    private static async Task LocateAsync(BookingWorld w, Guid shopId, string district, CancellationToken ct) =>
        await OkAsync(w.Admin.PutAsync($"/api/v1/admin/shops/{shopId}/location", new
        {
            latitude = 24.7743, longitude = 46.6380, addressLine = (string?)null, district, city = "الرياض", formattedAddress = $"حي {district}، الرياض", source = "Manual",
        }, ct), ct);

    private sealed class FixedCustomer(Guid id) : ICurrentCustomer
    {
        public Guid? CustomerId { get; } = id;
    }
}
