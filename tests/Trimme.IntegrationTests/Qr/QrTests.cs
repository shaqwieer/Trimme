using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Trimme.BuildingBlocks.Application.Qr;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.IntegrationTests.Identity;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.Modules.Administration.Domain;
using Trimme.Modules.Bookings.Domain;
using Trimme.Modules.Identity.Domain;
using Trimme.Modules.QrAnalytics.Domain;
using Trimme.Modules.QrAnalytics.Infrastructure.Seeding;
using static Trimme.IntegrationTests.Bookings.BookingTestData;

namespace Trimme.IntegrationTests.Qr;

/// <summary>
/// R-QR-01, R-QR-02, R-AD-09, R-CUS-13 (D-114): unique codes that resolve to a shop or professional page, admin management
/// (audited, no delete), privacy-preserving scans, first-party attribution within the window and only to the same shop,
/// the shop's own read-only view, and analytics that match the seeded data.
/// </summary>
public sealed class QrTests(PostgresFixture postgres)
{
    private static DateOnly Target => TodayAt(DateTimeOffset.UtcNow).AddDays(2);

    private static Task<HttpResponseMessage> CreateCodeAsync(ApiSession admin, Guid shopId, Guid? professionalId, CancellationToken ct, string? label = null) =>
        admin.PostAsync("/api/v1/admin/qr/codes", new { shopId, professionalId, label }, ct);

    private static Task<HttpResponseMessage> ScanAsync(ApiSession visitor, string code, CancellationToken ct, string userAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) Mobile/15E148") =>
        visitor.SendAsync(HttpMethod.Post, $"/api/v1/public/qr/{code}/visits", new { locale = "ar" }, ct, headers: new Dictionary<string, string> { ["User-Agent"] = userAgent });

    private static async Task<T> InDbAsync<T>(TrimmeApiFactory factory, Func<TrimmeDbContext, Task<T>> read)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        using var system = scope.ServiceProvider.GetRequiredService<ISystemDataScope>().Begin();
        return await read(scope.ServiceProvider.GetRequiredService<TrimmeDbContext>());
    }

    [Fact]
    public async Task Codes_AreUniqueAndResolveToTheirTarget_AdminsSwitchThemOffAndOn_Audited_AndDownloadThem()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "qr_codes", ct);
        using var anonymous = ApiSession.Create(w.Factory);

        var shopCode = await OkAsync(CreateCodeAsync(w.Admin, w.Shops.A.ShopId, null, ct, "واجهة المحل"), ct, HttpStatusCode.Created);
        var code = shopCode.GetProperty("code").GetString()!;
        code.Length.ShouldBe(QrCodeFormat.Length);
        code.ShouldAllBe(c => QrCodeFormat.Alphabet.Contains(c));
        shopCode.GetProperty("url").GetString().ShouldEndWith($"/q/{code}");
        shopCode.GetProperty("targetType").GetString().ShouldBe("Shop");
        shopCode.GetProperty("label").GetString().ShouldBe("واجهة المحل");

        // R-QR-01: a professional code opens that professional; only an active professional of the same shop.
        var proCode = await OkAsync(CreateCodeAsync(w.Admin, w.Shops.A.ShopId, w.Faisal, ct), ct, HttpStatusCode.Created);
        await FailsAsync(CreateCodeAsync(w.Admin, w.Shops.A.ShopId, w.ProB, ct), HttpStatusCode.NotFound, "professional.not_found", ct);
        await FailsAsync(CreateCodeAsync(w.Admin, Guid.NewGuid(), null, ct), HttpStatusCode.NotFound, "shop.not_found", ct);
        await FailsAsync(CreateCodeAsync(w.Admin, w.Shops.A.ShopId, null, ct, new string('x', 81)), HttpStatusCode.BadRequest, "validation.too_long", ct);

        var target = await OkAsync(anonymous.GetAsync($"/api/v1/public/qr/{code.ToUpperInvariant()}", ct), ct);
        target.GetProperty("targetType").GetString().ShouldBe("Shop");
        target.GetProperty("shopSlug").GetString().ShouldBe(w.SlugA);
        var proTarget = await OkAsync(anonymous.GetAsync($"/api/v1/public/qr/{proCode.GetProperty("code").GetString()}", ct), ct);
        proTarget.GetProperty("targetType").GetString().ShouldBe("Professional");
        proTarget.GetProperty("professionalId").GetGuid().ShouldBe(w.Faisal);
        proTarget.GetProperty("professionalSlug").GetString().ShouldNotBeNullOrEmpty();
        await FailsAsync(anonymous.GetAsync("/api/v1/public/qr/zzzzzzzz", ct), HttpStatusCode.NotFound, "qr.not_found", ct);
        await FailsAsync(anonymous.GetAsync("/api/v1/public/qr/not-a-code", ct), HttpStatusCode.NotFound, "qr.not_found", ct);

        // Many codes, all distinct; the database refuses a duplicate whatever the application does.
        var codes = new HashSet<string>(StringComparer.Ordinal) { code, proCode.GetProperty("code").GetString()! };
        for (var i = 0; i < 20; i++)
        {
            codes.Add((await OkAsync(CreateCodeAsync(w.Admin, w.Shops.B.ShopId, null, ct), ct, HttpStatusCode.Created)).GetProperty("code").GetString()!);
        }

        codes.Count.ShouldBe(22);
        await Should.ThrowAsync<DbUpdateException>(() => InDbAsync(w.Factory, async db =>
        {
            var copy = QrCodeLink.Create(new QrCodeLinkId(Guid.CreateVersion7()), new ShopId(w.Shops.B.ShopId), code, null, null, null, DateTimeOffset.UtcNow).Value;
            db.Add(copy);
            return await db.SaveChangesAsync(ct);
        }));

        // Switched off: the code stops resolving and scanning; its figures are kept. A stale version is 409.
        var id = shopCode.GetProperty("id").GetGuid();
        var version = shopCode.GetProperty("version").GetUInt32();
        var off = await OkAsync(w.Admin.PostAsync($"/api/v1/admin/qr/codes/{id}/deactivate", new { version }, ct), ct);
        off.GetProperty("isActive").GetBoolean().ShouldBeFalse();
        await FailsAsync(w.Admin.PostAsync($"/api/v1/admin/qr/codes/{id}/activate", new { version }, ct), HttpStatusCode.Conflict, null, ct);
        await FailsAsync(anonymous.GetAsync($"/api/v1/public/qr/{code}", ct), HttpStatusCode.NotFound, "qr.not_found", ct);
        await FailsAsync(ScanAsync(anonymous, code, ct), HttpStatusCode.NotFound, "qr.not_found", ct);
        await FailsAsync(w.Admin.PostAsync($"/api/v1/admin/qr/codes/{id}/deactivate", new { version = off.GetProperty("version").GetUInt32() }, ct), HttpStatusCode.Conflict, "qr.already_inactive", ct);
        await OkAsync(w.Admin.PostAsync($"/api/v1/admin/qr/codes/{id}/activate", new { version = off.GetProperty("version").GetUInt32() }, ct), ct);
        await OkAsync(anonymous.GetAsync($"/api/v1/public/qr/{code}", ct), ct);

        (await InDbAsync(w.Factory, db => db.Set<AuditEntry>().AsNoTracking().Where(e => e.EntityId == id.ToString()).OrderBy(e => e.Sequence).Select(e => e.Action).ToListAsync(ct)))
            .ShouldBe(["qr.created", "qr.deactivated", "qr.activated"]);

        // Files: PNG, SVG and a vector PDF of the printed URL; anything else is 400.
        using var png = await w.Admin.GetAsync($"/api/v1/admin/qr/codes/{id}/image?format=png&size=8", ct);
        png.StatusCode.ShouldBe(HttpStatusCode.OK);
        png.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
        (await png.Content.ReadAsByteArrayAsync(ct))[..8].ShouldBe(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        using var svg = await w.Admin.GetAsync($"/api/v1/admin/qr/codes/{id}/image?format=svg", ct);
        (await svg.Content.ReadAsStringAsync(ct)).ShouldStartWith("<svg xmlns=\"http://www.w3.org/2000/svg\"");
        using var pdf = await w.Admin.GetAsync($"/api/v1/admin/qr/codes/{id}/image?format=pdf", ct);
        pdf.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        var pdfText = Encoding.ASCII.GetString(await pdf.Content.ReadAsByteArrayAsync(ct));
        pdfText.ShouldStartWith("%PDF-1.4");
        pdfText.TrimEnd().ShouldEndWith("%%EOF");
        await FailsAsync(w.Admin.GetAsync($"/api/v1/admin/qr/codes/{id}/image?format=gif", ct), HttpStatusCode.BadRequest, null, ct);

        // Permissions: shop users and customers never manage codes; Support may view but not create.
        (await w.OwnerA.GetAsync("/api/v1/admin/qr/codes", ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var support = await IdentityTestData.SignInNewStaffAsync(w.Factory, SystemRoles.Support, ct);
        (await support.GetAsync("/api/v1/admin/qr/codes", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CreateCodeAsync(support, w.Shops.A.ShopId, null, ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var list = await OkAsync(w.Admin.GetAsync($"/api/v1/admin/qr/codes?shopId={w.Shops.A.ShopId}", ct), ct);
        list.GetProperty("total").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task Scans_KeepNoIpAddress_ReloadsCountOnce_AndCreditOnlySameShopBookingsWithinTheWindow()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var w = await ArrangeAsync(postgres, "qr_attr", ct, clock);
        var created = await OkAsync(CreateCodeAsync(w.Admin, w.Shops.A.ShopId, null, ct), ct, HttpStatusCode.Created);
        var code = created.GetProperty("code").GetString()!;
        var codeId = created.GetProperty("id").GetGuid();

        using var noura = await CustomerAsync(w.Factory, "نورة", ct);
        using (var scan = await ScanAsync(noura, code, ct))
        {
            scan.StatusCode.ShouldBe(HttpStatusCode.NoContent, await scan.Content.ReadAsStringAsync(ct));
            var setCookie = scan.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith(QrAttributionCookie.Name, StringComparison.Ordinal));
            setCookie.ShouldContain("httponly", Case.Insensitive);
            setCookie.ShouldContain("secure", Case.Insensitive);
            setCookie.ShouldContain("path=/api/v1", Case.Insensitive);
            setCookie.ShouldContain("samesite=lax", Case.Insensitive);
        }

        var visitId = Guid.Parse(noura.Cookie(QrAttributionCookie.Name, "/api/v1/bookings")!);

        // A reload reuses the visit: one scan, one visit id.
        (await ScanAsync(noura, code, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        Guid.Parse(noura.Cookie(QrAttributionCookie.Name, "/api/v1/bookings")!).ShouldBe(visitId);

        // R-QR-02: no IP column at all, a 32-character day-scoped hash, the device class and the language only.
        var visits = await InDbAsync(w.Factory, db => db.Set<QrVisit>().AsNoTracking().ToListAsync(ct));
        visits.Count.ShouldBe(1);
        visits[0].Id.Value.ShouldBe(visitId);
        visits[0].VisitorHash.Length.ShouldBe(QrVisit.VisitorHashLength);
        visits[0].Device.ShouldBe(QrDeviceClass.Mobile);
        visits[0].Locale.ShouldBe("ar");
        var columns = await InDbAsync(w.Factory, db => db.Database
            .SqlQueryRaw<string>("SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_schema = 'qr' AND table_name = 'qr_visits'")
            .ToListAsync(ct));
        columns.ShouldBe(["id", "link_id", "shop_id", "visited_at", "visitor_hash", "device", "locale"], ignoreOrder: true);

        // The booking at the scanned shop is credited; the cookie is not part of the idempotency identity.
        var booking = await OkAsync(BookAsync(noura, w.SlugA, w.Haircut, null, At(Target, 10), ct, key: "qr-1"), ct, HttpStatusCode.Created);
        var bookingId = booking.GetProperty("id").GetGuid();
        noura.Cookies.Add(new Cookie(QrAttributionCookie.Name, Guid.NewGuid().ToString("N"), QrAttributionCookie.Path, "localhost") { Secure = true, HttpOnly = true });
        (await OkAsync(BookAsync(noura, w.SlugA, w.Haircut, null, At(Target, 10), ct, key: "qr-1"), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid().ShouldBe(bookingId);
        noura.Cookies.Add(new Cookie(QrAttributionCookie.Name, visitId.ToString("N"), QrAttributionCookie.Path, "localhost") { Secure = true, HttpOnly = true });

        // Same cookie, another shop: an ordinary booking.
        var atB = await OkAsync(BookAsync(noura, (await OkAsync(w.Admin.GetAsync($"/api/v1/admin/shops/{w.Shops.B.ShopId}", ct), ct)).GetProperty("slug").GetString()!, w.ServiceB, null, At(Target, 11), ct),
            ct, HttpStatusCode.Created);
        var stored = await InDbAsync(w.Factory, db => db.Set<Booking>().AsNoTracking().ToDictionaryAsync(b => b.Id.Value, ct));
        stored[bookingId].QrLinkId.ShouldBe(new QrCodeLinkId(codeId));
        stored[bookingId].QrVisitId.ShouldBe(visitId);
        stored[atB.GetProperty("id").GetGuid()].QrLinkId.ShouldBeNull();

        // The shop sees the source («رمز QR»), never who scanned.
        var shopView = await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/bookings/{bookingId}", ct), ct);
        shopView.GetProperty("booking").GetProperty("viaQr").GetBoolean().ShouldBeTrue();

        // A walk-in by a staff browser carrying the cookie is never credited.
        w.StaffA.Cookies.Add(new Cookie(QrAttributionCookie.Name, visitId.ToString("N"), QrAttributionCookie.Path, "localhost") { Secure = true, HttpOnly = true });
        var walkIn = await OkAsync(w.StaffA.SendAsync(HttpMethod.Post, "/api/v1/shop/bookings/walk-in",
            new { serviceId = w.Haircut, professionalId = w.Faisal, startsAt = At(Target, 15), customerName = "زائر" }, ct, headers: Key()), ct, HttpStatusCode.Created);
        (await InDbAsync(w.Factory, db => db.Set<Booking>().AsNoTracking().SingleAsync(b => b.Id == new BookingId(walkIn.GetProperty("id").GetGuid()), ct))).QrLinkId.ShouldBeNull();

        // The shop's own view: its code with one scan and one booking; another shop sees nothing of it; staff have no access.
        var own = await OkAsync(w.OwnerA.GetAsync("/api/v1/shop/qr/codes", ct), ct);
        var ownCode = own.GetProperty("items").EnumerateArray().Single();
        ownCode.GetProperty("visits").GetInt32().ShouldBe(1);
        ownCode.GetProperty("bookings").GetInt32().ShouldBe(1);
        own.GetProperty("totals").GetProperty("conversionRate").GetDouble().ShouldBe(100);
        (await OkAsync(w.OwnerB.GetAsync("/api/v1/shop/qr/codes", ct), ct)).GetProperty("items").GetArrayLength().ShouldBe(0);
        await FailsAsync(w.OwnerB.GetAsync($"/api/v1/shop/qr/codes/{codeId}", ct), HttpStatusCode.NotFound, "qr.not_found", ct);
        await FailsAsync(w.OwnerB.GetAsync($"/api/v1/shop/qr/codes/{codeId}/image", ct), HttpStatusCode.NotFound, "qr.not_found", ct);
        (await w.StaffA.GetAsync("/api/v1/shop/qr/codes", ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Eight days later the scan no longer credits a booking (the default window is 7 days).
        clock.Advance(TimeSpan.FromDays(8));
        using var later = await CustomerAsync(w.Factory, "خالد", ct);
        later.Cookies.Add(new Cookie(QrAttributionCookie.Name, visitId.ToString("N"), QrAttributionCookie.Path, "localhost") { Secure = true, HttpOnly = true });
        var late = await OkAsync(BookAsync(later, w.SlugA, w.Haircut, null, At(Target.AddDays(8), 10), ct), ct, HttpStatusCode.Created);
        (await InDbAsync(w.Factory, db => db.Set<Booking>().AsNoTracking().SingleAsync(b => b.Id == new BookingId(late.GetProperty("id").GetGuid()), ct))).QrLinkId.ShouldBeNull();

        // Admin analytics over the scan's day: one scan, one credited booking, 100 % conversion, per shop.
        using var admin = await IdentityTestData.SignInNewStaffAsync(w.Factory, SystemRoles.SuperAdmin, ct);
        var day = Iso(TodayAt(clock.GetUtcNow().AddDays(-8)));
        var analytics = await OkAsync(admin.GetAsync($"/api/v1/admin/qr/analytics?from={day}&to={day}", ct), ct);
        analytics.GetProperty("totals").GetProperty("visits").GetInt32().ShouldBe(1);
        analytics.GetProperty("totals").GetProperty("bookings").GetInt32().ShouldBe(1);
        analytics.GetProperty("totals").GetProperty("conversionRate").GetDouble().ShouldBe(100);
        analytics.GetProperty("byShop").EnumerateArray().Single().GetProperty("shopId").GetGuid().ShouldBe(w.Shops.A.ShopId);
        await FailsAsync(admin.GetAsync($"/api/v1/admin/qr/analytics?from={day}&to={Iso(TodayAt(clock.GetUtcNow()).AddDays(-400))}", ct), HttpStatusCode.BadRequest, null, ct);
    }

    [Fact]
    public async Task DemoSeed_CodesResolve_AndTheAnalyticsMatchTheSeededScansAndBookings()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await TrimmeApiFactory.CreateMigratedAsync(postgres, "qr_seed", ct);
        for (var run = 0; run < 2; run++)
        {
            await using var seedScope = factory.Services.CreateAsyncScope();
            foreach (var seeder in seedScope.ServiceProvider.GetServices<IDevSeeder>().OrderBy(s => s.Order))
            {
                await seeder.SeedAsync(seedScope.ServiceProvider, ct);
            }
        }

        // The scans the seeder promises: 0–4 a day per code over 27 days (the retired code only until it was switched off),
        // plus the three scans that demo bookings are credited to.
        var expected = DemoQr.Codes.Select((c, index) => Enumerable.Range(c.IsActive ? 1 : 21, c.IsActive ? DemoQrSeeder.Days : DemoQrSeeder.Days - 20)
            .Sum(daysAgo => DemoQrSeeder.ScansOn(index, daysAgo))).Sum() + DemoQr.Bookings.Count;

        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var analytics = await OkAsync(admin.GetAsync("/api/v1/admin/qr/analytics", ct), ct);
        var totals = analytics.GetProperty("totals");
        totals.GetProperty("visits").GetInt32().ShouldBe(expected);
        totals.GetProperty("bookings").GetInt32().ShouldBe(DemoQr.Bookings.Count);
        totals.GetProperty("convertedVisits").GetInt32().ShouldBe(DemoQr.Bookings.Count);
        analytics.GetProperty("byShop").GetArrayLength().ShouldBe(2);

        var list = await OkAsync(admin.GetAsync("/api/v1/admin/qr/codes", ct), ct);
        list.GetProperty("total").GetInt32().ShouldBe(DemoQr.Codes.Count);
        using var anonymous = ApiSession.Create(factory);
        var sultan = await OkAsync(anonymous.GetAsync($"/api/v1/public/qr/{DemoQr.AlAsalaSultan.Code}", ct), ct);
        sultan.GetProperty("targetType").GetString().ShouldBe("Professional");
        sultan.GetProperty("shopSlug").GetString().ShouldBe(DemoData.AlAsala.Slug);
        await FailsAsync(anonymous.GetAsync($"/api/v1/public/qr/{DemoQr.BarberHouseRetired.Code}", ct), HttpStatusCode.NotFound, "qr.not_found", ct);
    }
}
