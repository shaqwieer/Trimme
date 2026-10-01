using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Trimme.BuildingBlocks.Application.Bookings;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Caching;
using Trimme.IntegrationTests.Bookings;
using Trimme.IntegrationTests.Identity;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.IntegrationTests.Tenancy;
using Trimme.Modules.Identity.Domain;
using Trimme.Modules.Reviews.Application;
using Trimme.Modules.Reviews.Domain;
using Trimme.Modules.Services.Domain;
using static Trimme.IntegrationTests.Bookings.BookingTestData;

namespace Trimme.IntegrationTests.Discovery;

/// <summary>
/// R-CUS-03/04/05/06, R-WEB-10 data, D-090…D-093: nearby search on PostGIS (nearest first, radius, only listed shops),
/// every filter and sort, the public shop, status, professional and review endpoints, the multi-shop public scope and
/// the public response cache.
/// </summary>
public sealed class DiscoveryTests(PostgresFixture postgres)
{
    /// <summary>Where the searches start (Riyadh).</summary>
    private const string Origin = "lat=24.77&lng=46.639";

    private static DateOnly Target => TodayAt(DateTimeOffset.UtcNow).AddDays(2);

    private static string[] Slugs(JsonElement search) =>
        [.. search.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("slug").GetString()!)];

    private static Task<JsonElement> SearchAsync(ApiSession session, string query, CancellationToken ct) =>
        OkAsync(session.GetAsync($"/api/v1/public/shops/search?{query}", ct), ct);

    [Fact]
    public async Task NearbySearch_IsNearestFirst_WithinTheRadius_AndListsOnlyVisibleShopsWithOffers()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var d = await DiscoveryWorld.ArrangeAsync(postgres, "disc_near", ct);
        using var visitor = ApiSession.Create(d.W.Factory);

        // A (0.5 km), B (1.1 km), C (6.9 km); the far shop, the unsubscribed one and the one without services are not listed.
        var near = await SearchAsync(visitor, $"{Origin}&radiusKm=10", ct);
        Slugs(near).ShouldBe([d.SlugA, d.SlugB, d.SlugC]);
        var distances = near.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("distanceKm").GetDouble()).ToArray();
        distances.ShouldBe(distances.Order().ToArray());
        distances[0].ShouldBeLessThan(1);
        distances[2].ShouldBeInRange(5, 9);
        near.GetProperty("total").GetInt32().ShouldBe(3);
        near.GetProperty("sort").GetString().ShouldBe("Distance");
        near.GetProperty("radiusKm").GetDouble().ShouldBe(10);

        Slugs(await SearchAsync(visitor, $"{Origin}&radiusKm=1", ct)).ShouldBe([d.SlugA]);
        Slugs(await SearchAsync(visitor, "lat=21.5433&lng=39.1728&radiusKm=5", ct)).ShouldBe([d.SlugFar]);

        // Without a location: by city, sorted by rating, no distance.
        var riyadh = await SearchAsync(visitor, $"city={Uri.EscapeDataString("الرياض")}", ct);
        Slugs(riyadh).ShouldBe([d.SlugA, d.SlugB, d.SlugC], ignoreOrder: true);
        riyadh.GetProperty("sort").GetString().ShouldBe("Rating");
        riyadh.GetProperty("items")[0].GetProperty("distanceKm").ValueKind.ShouldBe(JsonValueKind.Null);

        // Pages.
        var second = await SearchAsync(visitor, $"{Origin}&pageSize=1&page=2", ct);
        Slugs(second).ShouldBe([d.SlugB]);
        second.GetProperty("total").GetInt32().ShouldBe(3);

        await FailsAsync(visitor.GetAsync("/api/v1/public/shops/search?lat=95&lng=46", ct), HttpStatusCode.BadRequest, "validation.failed", ct);
        await FailsAsync(visitor.GetAsync("/api/v1/public/shops/search?lat=24.7", ct), HttpStatusCode.BadRequest, "lng", ct);
        await FailsAsync(visitor.GetAsync($"/api/v1/public/shops/search?{Origin}&radiusKm=500", ct), HttpStatusCode.BadRequest, "radiusKm", ct);
    }

    [Fact]
    public async Task Search_Filters_ByTextCategoryPriceOpenNowVerifiedAndBookableToday_AndSortsByRatingOrEarliest()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var d = await DiscoveryWorld.ArrangeAsync(postgres, "disc_filter", ct);
        using var visitor = ApiSession.Create(d.W.Factory);

        // Text: a service name with Arabic spelling variants (ة → ه) finds the shop and says which service matched.
        var skin = await SearchAsync(visitor, $"{Origin}&q={Uri.EscapeDataString("بشره")}", ct);
        Slugs(skin).ShouldBe([d.SlugC]);
        skin.GetProperty("items")[0].GetProperty("matchedOffer").GetProperty("nameAr").GetString().ShouldBe("تنظيف بشرة");
        Slugs(await SearchAsync(visitor, $"{Origin}&q={d.SlugA.ToUpperInvariant()}", ct)).ShouldBe([d.SlugA]);

        // Category: the cheapest offer in it is matched and priced (never a global price).
        var beard = await SearchAsync(visitor, $"{Origin}&categoryId={d.BeardCategory}", ct);
        Slugs(beard).ShouldBe([d.SlugA, d.SlugB]);
        beard.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("pinPrice").GetDecimal()).ShouldBe([40m, 25m]);

        // Price range on the pin price (the lowest price when nothing is matched): A 40, B 25, C 30.
        var all = await SearchAsync(visitor, Origin, ct);
        all.GetProperty("priceRange").GetProperty("min").GetDecimal().ShouldBe(25m);
        all.GetProperty("priceRange").GetProperty("max").GetDecimal().ShouldBe(40m);
        Slugs(await SearchAsync(visitor, $"{Origin}&minPrice=35", ct)).ShouldBe([d.SlugA]);
        Slugs(await SearchAsync(visitor, $"{Origin}&maxPrice=30", ct)).ShouldBe([d.SlugB, d.SlugC]);
        await FailsAsync(visitor.GetAsync($"/api/v1/public/shops/search?{Origin}&minPrice=50&maxPrice=10", ct), HttpStatusCode.BadRequest, "minPrice", ct);

        // Open now at 10:00 (A and B open 09:00–21:00; C has no hours).
        var open = await SearchAsync(visitor, $"{Origin}&openNow=true", ct);
        Slugs(open).ShouldBe([d.SlugA, d.SlugB]);
        var a = open.GetProperty("items")[0];
        a.GetProperty("isOpenNow").GetBoolean().ShouldBeTrue();
        a.GetProperty("closesAt").GetDateTimeOffset().ShouldBe(At(Target, 21));

        // Verified only.
        Slugs(await SearchAsync(visitor, $"{Origin}&verified=true", ct)).ShouldBe([d.SlugC]);

        // Bookable today: the probe finds 11:00 (lead time 60 minutes) at A and B; C cannot be booked.
        var today = await SearchAsync(visitor, $"{Origin}&bookableToday=true", ct);
        Slugs(today).ShouldBe([d.SlugA, d.SlugB]);
        today.GetProperty("items")[0].GetProperty("earliestSlotAt").GetDateTimeOffset().ShouldBe(At(Target, 11));

        // The shop's shortest service belongs to a professional who is away: the others' earliest time still counts (D-091).
        var quick = (await OkAsync(d.OwnerA.PostAsync("/api/v1/shop/services", new { nameAr = "تشذيب سريع", price = 45m, durationMinutes = 10, onlineBookable = true }, ct), ct, HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        await OkAsync(d.Admin.PutAsync($"/api/v1/admin/professionals/{d.W.Omar}/services", new { serviceIds = new[] { d.W.Haircut, d.W.Beard, quick } }, ct), ct);
        await OkAsync(
            d.OwnerA.PostAsync("/api/v1/shop/schedule/time-off", new { professionalId = d.W.Omar, kind = "Vacation", startDate = Iso(Target), endDate = Iso(Target.AddDays(1)) }, ct),
            ct, HttpStatusCode.Created);
        var stillBookable = await SearchAsync(visitor, $"{Origin}&bookableToday=true", ct);
        Slugs(stillBookable).ShouldContain(d.SlugA);
        stillBookable.GetProperty("items")[0].GetProperty("earliestSlotAt").GetDateTimeOffset().ShouldBe(At(Target, 11));

        // Earliest first: C (no slot) goes last; rating first once C has the best stored rating.
        Slugs(await SearchAsync(visitor, $"{Origin}&sort=Earliest", ct)).Last().ShouldBe(d.SlugC);
        await d.RateAsync(d.ShopC, 5, ct);
        await d.RateAsync(d.ShopA, 3, ct);
        var rated = await SearchAsync(visitor, $"{Origin}&sort=Rating", ct);
        Slugs(rated).ShouldBe([d.SlugC, d.SlugA, d.SlugB]);
        rated.GetProperty("items")[0].GetProperty("rating").GetDecimal().ShouldBe(5m);
        rated.GetProperty("items")[0].GetProperty("reviewCount").GetInt32().ShouldBe(1);

        // Popular categories around the point: the lowest price of each category among nearby shops (DV-S11).
        var popular = await OkAsync(visitor.GetAsync($"/api/v1/public/categories/popular?{Origin}", ct), ct);
        var beardTile = popular.EnumerateArray().Single(c => c.GetProperty("categoryId").GetGuid() == d.BeardCategory);
        beardTile.GetProperty("minPrice").GetDecimal().ShouldBe(25m);
        beardTile.GetProperty("shopCount").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task PublicPages_ShopStatusProfessionalAndReviews_AreComplete_Live_AndCarryNoContactData()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var d = await DiscoveryWorld.ArrangeAsync(postgres, "disc_pages", ct);
        using var visitor = ApiSession.Create(d.W.Factory);

        var shop = await OkAsync(visitor.GetAsync($"/api/v1/public/shops/{d.SlugA}", ct), ct);
        shop.GetProperty("minPrice").GetDecimal().ShouldBe(40m);
        shop.GetProperty("rating").GetProperty("count").GetInt32().ShouldBe(0);
        shop.GetProperty("openingHours").GetArrayLength().ShouldBe(7);
        shop.GetProperty("cancellationCutoffMinutes").GetInt32().ShouldBe(120);
        shop.GetProperty("listedInDiscovery").GetBoolean().ShouldBeTrue();
        shop.GetProperty("location").GetProperty("district").GetString().ShouldBe("الملقا");

        var status = await OkAsync(visitor.GetAsync($"/api/v1/public/shops/{d.SlugA}/status", ct), ct);
        status.GetProperty("isOpenNow").GetBoolean().ShouldBeTrue();
        status.GetProperty("acceptsOnlineBookings").GetBoolean().ShouldBeTrue();
        status.GetProperty("professionals").EnumerateArray().Select(p => p.GetProperty("nextAvailableAt").GetDateTimeOffset())
            .ShouldAllBe(at => at == At(Target, 11));

        // The professional page: the services they do (their shop's prices), rating, and their next free times.
        var faisal = await OkAsync(visitor.GetAsync($"/api/v1/public/shops/{d.SlugA}/professionals/faisal", ct), ct);
        faisal.GetProperty("shopSlug").GetString().ShouldBe(d.SlugA);
        faisal.GetProperty("offers").EnumerateArray().Select(o => o.GetProperty("nameAr").GetString()).ShouldBe(["حلاقة", "لحية", "باقة"], ignoreOrder: true);
        var next = await OkAsync(visitor.GetAsync($"/api/v1/public/shops/{d.SlugA}/professionals/faisal/next-slots", ct), ct);
        next.GetProperty("date").GetString().ShouldBe(Iso(Target));
        next.GetProperty("slots").EnumerateArray().Select(s => s.GetProperty("localTime").GetString()).ShouldBe(["11:00", "11:05", "11:10"]);
        next.GetProperty("remainingCount").GetInt32().ShouldBeGreaterThan(0);
        (await visitor.GetAsync($"/api/v1/public/shops/{d.SlugA}/professionals/salem", ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound, "another shop's professional");

        // A completed visit and its review: the public list shows first name + initial; totals move with it.
        var bookingId = await d.CompletedVisitAsync(ct);
        await d.ReviewAsync(bookingId, 4, "تجربة ممتازة", ct);
        var reviews = await OkAsync(visitor.GetAsync($"/api/v1/public/shops/{d.SlugA}/reviews", ct), ct);
        reviews.GetProperty("summary").GetProperty("count").GetInt32().ShouldBe(1);
        reviews.GetProperty("summary").GetProperty("histogram").EnumerateArray().Select(x => x.GetInt32()).ShouldBe([0, 0, 0, 1, 0]);
        var review = reviews.GetProperty("reviews").GetProperty("items")[0];
        review.GetProperty("authorName").GetString().ShouldBe("سعد ع.");
        review.GetProperty("itemNameAr").GetString().ShouldBe("حلاقة");
        (await OkAsync(visitor.GetAsync($"/api/v1/public/shops/{d.SlugA}", ct), ct)).GetProperty("rating").GetProperty("average").GetDecimal().ShouldBe(4m);
        (await OkAsync(visitor.GetAsync($"/api/v1/public/shops/{d.SlugA}/reviews?professionalId={d.W.Faisal}", ct), ct))
            .GetProperty("summary").GetProperty("count").GetInt32().ShouldBe(1);
        (await OkAsync(visitor.GetAsync($"/api/v1/public/shops/{d.SlugA}/reviews?professionalId={d.W.Omar}", ct), ct))
            .GetProperty("summary").GetProperty("count").GetInt32().ShouldBe(0);
        var top = await OkAsync(visitor.GetAsync($"/api/v1/public/professionals/top?{Origin}", ct), ct);
        top.EnumerateArray().Select(p => p.GetProperty("slug").GetString()).ShouldBe(["faisal"], "only professionals with stored ratings");
        top[0].GetProperty("shopSlug").GetString().ShouldBe(d.SlugA);

        // Paused (D-013): the page stays, discovery hides it, the status says why.
        await OkAsync(d.OwnerB.PostAsync("/api/v1/shop/online-booking/pause", new { reason = "صيانة" }, ct), ct);
        var paused = await OkAsync(visitor.GetAsync($"/api/v1/public/shops/{d.SlugB}/status", ct), ct);
        paused.GetProperty("acceptsOnlineBookings").GetBoolean().ShouldBeFalse();
        paused.GetProperty("blockedReason").GetString().ShouldBe("shop.paused");
        (await OkAsync(visitor.GetAsync($"/api/v1/public/shops/{d.SlugB}", ct), ct)).GetProperty("listedInDiscovery").GetBoolean().ShouldBeFalse();
        Slugs(await SearchAsync(visitor, Origin, ct)).ShouldNotContain(d.SlugB);
        (await OkAsync(visitor.GetAsync($"/api/v1/public/sitemap", ct), ct)).GetProperty("shops").EnumerateArray()
            .Select(s => s.GetProperty("slug").GetString()).ShouldBe([d.SlugA, d.SlugC, d.SlugFar], ignoreOrder: true);

        // Stats and areas count listed shops only.
        var stats = await OkAsync(visitor.GetAsync("/api/v1/public/stats", ct), ct);
        stats.GetProperty("shopCount").GetInt32().ShouldBe(3, "A and C in Riyadh and the far shop");
        stats.GetProperty("reviewCount").GetInt32().ShouldBe(1);
        var areas = await OkAsync(visitor.GetAsync("/api/v1/public/areas", ct), ct);
        areas.GetProperty("areas").EnumerateArray().Select(x => x.GetProperty("district").GetString()).ShouldContain("الملقا");

        // No contact data in any public payload (R-PRO-02, spec §7).
        foreach (var path in new[]
                 {
                     $"/api/v1/public/shops/{d.SlugA}", $"/api/v1/public/shops/{d.SlugA}/status", $"/api/v1/public/shops/{d.SlugA}/reviews",
                     $"/api/v1/public/shops/{d.SlugA}/professionals/faisal", $"/api/v1/public/shops/{d.SlugA}/professionals/faisal/next-slots",
                     $"/api/v1/public/shops/search?{Origin}", "/api/v1/public/stats", "/api/v1/public/sitemap",
                 })
        {
            using var response = await visitor.GetAsync(path, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            body.ShouldNotContain("+9665", customMessage: path);
            body.ShouldNotContain("whatsapp", Case.Insensitive, path);
            body.ShouldNotContain("customerId", Case.Insensitive, path);
        }
    }

    [Fact]
    public async Task PublicResponses_AreCachedForAnonymousReaders_AndEvictedByAnyPublicContentSave()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var d = await DiscoveryWorld.ArrangeAsync(postgres, "disc_cache", ct);
        using var visitor = ApiSession.Create(d.W.Factory);
        var path = $"/api/v1/public/shops/{d.SlugA}";
        (await OkAsync(visitor.GetAsync(path, ct), ct)).GetProperty("descriptionAr").ValueKind.ShouldBe(JsonValueKind.Null);

        // A write that bypasses the application is not seen: the anonymous response is served from the cache...
        await using (var scope = d.W.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<TrimmeDbContext>().Database
                .ExecuteSqlAsync($"UPDATE shops.shops SET description_ar = 'خارج التطبيق' WHERE id = {d.W.Shops.A.ShopId}", ct);
        }

        (await OkAsync(visitor.GetAsync(path, ct), ct)).GetProperty("descriptionAr").ValueKind.ShouldBe(JsonValueKind.Null);

        // ...but a signed-in reader is never served from it (authorization-sensitive responses are not cached).
        using var customer = await CustomerAsync(d.W.Factory, "ريم", ct);
        (await OkAsync(customer.GetAsync(path, ct), ct)).GetProperty("descriptionAr").GetString().ShouldBe("خارج التطبيق");

        // HTTP caching (Phase 17): anonymous answers, fresh or replayed from the cache, may be kept briefly by browsers
        // and shared caches; a signed-in reader's answer and a time-dependent endpoint never.
        using (var cached = await visitor.GetAsync(path, ct))
        {
            cached.Headers.CacheControl!.ToString().ShouldBe(PublicCache.HttpCacheControl);
        }

        using (var signedIn = await customer.GetAsync(path, ct))
        {
            signedIn.Headers.CacheControl!.NoStore.ShouldBeTrue();
        }

        using (var status = await visitor.GetAsync($"{path}/status", ct))
        {
            status.Headers.CacheControl!.NoStore.ShouldBeTrue("open now and the next times change by the minute");
        }

        // A save of public content through the application evicts it at once (D-093).
        var profile = await OkAsync(d.OwnerA.GetAsync("/api/v1/shop/profile", ct), ct);
        await OkAsync(d.OwnerA.PutAsync("/api/v1/shop/profile", new
        {
            nameAr = profile.GetProperty("nameAr").GetString(),
            nameEn = profile.GetProperty("nameEn").GetString(),
            descriptionAr = "وصف جديد",
            descriptionEn = (string?)null,
            category = profile.GetProperty("category").GetString(),
            publicPhone = (string?)null,
            amenities = Array.Empty<string>(),
            version = profile.GetProperty("version").GetUInt32(),
        }, ct), ct);
        (await OkAsync(visitor.GetAsync(path, ct), ct)).GetProperty("descriptionAr").GetString().ShouldBe("وصف جديد");
    }

    [Fact]
    public async Task PublicScopeForManyShops_ShowsExactlyThoseShopsRows_AndIsReadOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var d = await DiscoveryWorld.ArrangeAsync(postgres, "disc_scope", ct);
        await using var scope = d.W.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
        var published = scope.ServiceProvider.GetRequiredService<IPublicDataScope>();
        var a = new ShopId(d.W.Shops.A.ShopId);
        var c = new ShopId(d.ShopC);

        (await db.Set<ShopService>().CountAsync(ct)).ShouldBe(0, "no scope: an anonymous caller sees no shop-owned rows");
        using (published.BeginMany([a, c]))
        {
            var shops = await db.Set<ShopService>().Select(s => s.ShopId).Distinct().ToListAsync(ct);
            shops.ShouldBe([a, c], ignoreOrder: true);
            db.Add(ShopService.Create(new ShopServiceId(Guid.NewGuid()), a, CatalogText.Create("x", null, null, null), null, 10m, 10, true, 0, DateTimeOffset.UtcNow).Value);
            await Should.ThrowAsync<TenantViolationException>(() => db.SaveChangesAsync(ct));
            db.ChangeTracker.Clear();
        }

        (await db.Set<ShopService>().CountAsync(ct)).ShouldBe(0, "closing the scope restores isolation");
        Should.Throw<ArgumentException>(() => published.BeginMany([.. Enumerable.Range(0, IPublicDataScope.MaxShops + 1).Select(_ => new ShopId(Guid.NewGuid()))]));
    }

    /// <summary>
    /// The booking world (A and B, open 09:00–21:00, subscribed) at 10:00 Riyadh two days from now, plus: locations; a beard
    /// category offered by A (40) and B (25); shop C (verified, a 30 SAR skin service, no hours); a far shop in Jeddah;
    /// and two shops that must not be listed (no subscription; no services).
    /// </summary>
    private sealed record DiscoveryWorld(
        BookingWorld W, ApiSession OwnerA, ApiSession OwnerB, ApiSession Admin, Guid BeardCategory, Guid ShopC, string SlugC, string SlugFar) : IAsyncDisposable
    {
        public string SlugA => W.SlugA;

        public string SlugB { get; init; } = string.Empty;

        public Guid ShopA => W.Shops.A.ShopId;

        public static async Task<DiscoveryWorld> ArrangeAsync(PostgresFixture postgres, string prefix, CancellationToken ct)
        {
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            var w = await BookingTestData.ArrangeAsync(postgres, prefix, ct, clock);
            clock.SetUtcNow(At(Target, 10).ToUniversalTime());
            var admin = await IdentityTestData.SignInNewStaffAsync(w.Factory, SystemRoles.SuperAdmin, ct);
            var ownerA = await IdentityTestData.SignInStaffAsync(w.Factory, w.Shops.A.OwnerEmail, ct);
            var ownerB = await IdentityTestData.SignInStaffAsync(w.Factory, w.Shops.B.OwnerEmail, ct);

            async Task LocateAsync(Guid shopId, double lat, double lng, string district, string city = "الرياض") =>
                await OkAsync(admin.PutAsync($"/api/v1/admin/shops/{shopId}/location", new
                {
                    latitude = lat, longitude = lng, addressLine = (string?)null, district, city, formattedAddress = $"حي {district}، {city}", source = "Manual",
                }, ct), ct);

            async Task<(Guid Id, string Slug, ApiSession Owner)> ShopAsync(string name, bool subscribe)
            {
                var slug = $"{name}-{Guid.NewGuid():N}"[..20];
                var id = await ShopTestData.CreateShopAsync(admin, slug, ct);
                var email = IdentityTestData.NewEmail($"{name}-owner");
                await ShopTestData.CreateShopUserAsync(w.Factory, id, email, SystemRoles.ShopOwner, ct);
                if (subscribe)
                {
                    await SubscribeAsync(admin, id, ct, name);
                }

                return (id, slug, await IdentityTestData.SignInStaffAsync(w.Factory, email, ct));
            }

            async Task ServiceAsync(ApiSession owner, string name, decimal price, Guid? categoryId = null) =>
                await OkAsync(owner.PostAsync("/api/v1/shop/services", new { nameAr = name, price, durationMinutes = 30, onlineBookable = true, categoryId }, ct), ct, HttpStatusCode.Created);

            var beard = (await OkAsync(admin.PostAsync("/api/v1/admin/service-categories", new { nameAr = "اللحية", nameEn = "Beard", icon = "user", displayOrder = 1 }, ct), ct, HttpStatusCode.Created))
                .GetProperty("id").GetGuid();
            await ServiceAsync(ownerA, "تحديد اللحية", 40m, beard);
            await ServiceAsync(ownerB, "تهذيب لحية", 25m, beard);
            await LocateAsync(w.Shops.A.ShopId, 24.7743, 46.6380, "الملقا");
            await LocateAsync(w.Shops.B.ShopId, 24.7600, 46.6400, "حطين");

            var c = await ShopAsync("gamma", subscribe: true);
            await ServiceAsync(c.Owner, "تنظيف بشرة", 30m);
            await LocateAsync(c.Id, 24.8000, 46.7000, "النرجس");
            var detail = await OkAsync(admin.GetAsync($"/api/v1/admin/shops/{c.Id}", ct), ct);
            await OkAsync(admin.PutAsync($"/api/v1/admin/shops/{c.Id}", new
            {
                nameAr = detail.GetProperty("nameAr").GetString(), nameEn = detail.GetProperty("nameEn").GetString(), descriptionAr = (string?)null,
                descriptionEn = (string?)null, category = "Barbershop", publicPhone = (string?)null, amenities = Array.Empty<string>(), isVerified = true,
                version = detail.GetProperty("version").GetUInt32(),
            }, ct), ct);

            var far = await ShopAsync("jeddah", subscribe: true);
            await ServiceAsync(far.Owner, "حلاقة", 50m);
            await LocateAsync(far.Id, 21.5433, 39.1728, "الروضة", "جدة");

            var unsubscribed = await ShopAsync("nosub", subscribe: false);
            await ServiceAsync(unsubscribed.Owner, "حلاقة", 10m);
            await LocateAsync(unsubscribed.Id, 24.7710, 46.6395, "الملقا");

            var empty = await ShopAsync("empty", subscribe: true);
            await LocateAsync(empty.Id, 24.7705, 46.6392, "الملقا");

            foreach (var owner in new[] { c.Owner, far.Owner, unsubscribed.Owner, empty.Owner })
            {
                owner.Dispose();
            }

            var slugB = (await OkAsync(admin.GetAsync($"/api/v1/admin/shops/{w.Shops.B.ShopId}", ct), ct)).GetProperty("slug").GetString()!;
            return new DiscoveryWorld(w, ownerA, ownerB, admin, beard, c.Id, c.Slug, far.Slug) { SlugB = slugB };
        }

        /// <summary>Stores a rating total directly (the review command arrives in Phase 12).</summary>
        public async Task RateAsync(Guid shopId, int stars, CancellationToken ct)
        {
            await using var scope = W.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
            var aggregate = RatingAggregate.For(RatingSubjectKind.Shop, shopId, new ShopId(shopId));
            aggregate.Apply(stars, 1, DateTimeOffset.UtcNow);
            db.Add(aggregate);
            await db.SaveChangesAsync(ct);
        }

        /// <summary>A walk-in with Faisal that starts now (Arrived) and is completed.</summary>
        public async Task<Guid> CompletedVisitAsync(CancellationToken ct)
        {
            var walkIn = await OkAsync(
                OwnerA.PostAsync("/api/v1/shop/bookings/walk-in", new { serviceId = W.Haircut, professionalId = W.Faisal, customerName = "سعد العتيبي" }, ct),
                ct, HttpStatusCode.Created);
            var id = walkIn.GetProperty("id").GetGuid();
            await OkAsync(OwnerA.PostAsync($"/api/v1/shop/bookings/{id}/transitions", new { to = "Completed", version = walkIn.GetProperty("version").GetUInt32() }, ct), ct);
            return id;
        }

        /// <summary>Writes a review of a completed booking with the Reviews module's own rules (as Phase 12's command will).</summary>
        public async Task ReviewAsync(Guid bookingId, int stars, string comment, CancellationToken ct)
        {
            await using var scope = W.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
            using var system = scope.ServiceProvider.GetRequiredService<ISystemDataScope>().Begin();
            var booking = await scope.ServiceProvider.GetRequiredService<IBookingReviewSource>().FindAsync(bookingId, ct);
            booking.ShouldNotBeNull();
            booking.CompletedAt.ShouldNotBeNull();
            var review = Review.Create(new ReviewId(Guid.NewGuid()), RatingBook.ToReviewed(booking), stars, comment, DateTimeOffset.UtcNow).Value;
            db.Add(review);
            await RatingBook.ApplyAsync(db, review, 1, DateTimeOffset.UtcNow, ct);
            await db.SaveChangesAsync(ct);
        }

        public async ValueTask DisposeAsync()
        {
            OwnerA.Dispose();
            OwnerB.Dispose();
            Admin.Dispose();
            await W.DisposeAsync();
        }
    }
}
