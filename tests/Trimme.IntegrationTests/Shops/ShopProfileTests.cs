using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.IntegrationTests.Identity;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.IntegrationTests.Tenancy;
using Trimme.Modules.Administration.Domain;
using Trimme.Modules.Identity.Domain;
using Trimme.Tests.Shared;

namespace Trimme.IntegrationTests.Shops;

/// <summary>
/// Shop profile, admin edit policy, exact location and database-stored images (R-SHP-01/02/03, R-SD-09, D-064).
/// </summary>
public sealed class ShopProfileTests(PostgresFixture postgres)
{
    private static object Profile(string nameAr, string nameEn, uint version, string? descriptionAr = null, bool verified = false, string? phone = null) => new
    {
        nameAr,
        nameEn,
        descriptionAr,
        descriptionEn = (string?)null,
        category = "Barbershop",
        publicPhone = phone,
        amenities = new[] { "Parking" },
        isVerified = verified,
        version,
    };

    private static object OwnProfile(string nameAr, string nameEn, uint version, string? descriptionAr = null) => new
    {
        nameAr,
        nameEn,
        descriptionAr,
        descriptionEn = (string?)null,
        category = "Barbershop",
        publicPhone = (string?)null,
        amenities = Array.Empty<string>(),
        version,
    };

    [Fact]
    public async Task Admin_EditsProfile_WithOptimisticConcurrency_AndAudit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "shop_profile", ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shopId = await ShopTestData.CreateShopAsync(admin, "profile-shop", ct);

        var detail = await (await admin.GetAsync($"/api/v1/admin/shops/{shopId}", ct)).JsonAsync(ct);
        var version = detail.GetProperty("version").GetUInt32();
        detail.GetProperty("editableFields").EnumerateArray().Select(f => f.GetString())
            .ShouldBe(["Description", "PublicPhone", "Amenities", "Logo", "Cover", "Gallery"], ignoreOrder: true);

        using var saved = await admin.PutAsync($"/api/v1/admin/shops/{shopId}", Profile("صالون الملقا", "Al Malqa Salon", version, "حلاقة كلاسيكية", verified: true, phone: "011 456 7890"), ct);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(ct));
        var body = await saved.JsonAsync(ct);
        body.GetProperty("nameAr").GetString().ShouldBe("صالون الملقا");
        body.GetProperty("isVerified").GetBoolean().ShouldBeTrue();
        body.GetProperty("publicPhone").GetString().ShouldBe("+966114567890");
        body.GetProperty("version").GetUInt32().ShouldNotBe(version);

        // The same (now stale) version loses.
        using var stale = await admin.PutAsync($"/api/v1/admin/shops/{shopId}", Profile("قديم", "Stale", version), ct);
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await stale.ErrorCodeAsync(ct)).ShouldBe("resource.concurrency_conflict");

        using var invalidPhone = await admin.PutAsync($"/api/v1/admin/shops/{shopId}", Profile("س", "S", body.GetProperty("version").GetUInt32(), phone: "12"), ct);
        (await invalidPhone.JsonAsync(ct)).GetProperty("errors").GetProperty("publicPhone")[0].GetString().ShouldBe("validation.phone_invalid");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
        var audit = await db.Set<AuditEntry>().Where(e => e.EntityId == shopId.ToString() && e.Action == "shop.profile_updated").ToListAsync(ct);
        audit.Count.ShouldBe(1);
        audit[0].Summary!.ShouldContain("Name");
        audit[0].Summary!.ShouldNotContain("966", Case.Insensitive);
    }

    [Fact]
    public async Task ShopLocation_StoredAsGeography_WithGistIndex_AndUsedBySpatialQuery()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "shop_location", ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var malqa = await ShopTestData.CreateShopAsync(admin, "near-malqa", ct);
        var olaya = await ShopTestData.CreateShopAsync(admin, "near-olaya", ct);

        using (var set = await admin.PutAsync($"/api/v1/admin/shops/{malqa}/location", new
        {
            latitude = 24.8123,
            longitude = 46.6011,
            addressLine = "طريق أنس بن مالك",
            district = "الملقا",
            city = "الرياض",
            formattedAddress = "طريق أنس بن مالك، حي الملقا، الرياض",
            source = "Manual",
        }, ct))
        {
            set.StatusCode.ShouldBe(HttpStatusCode.OK, await set.Content.ReadAsStringAsync(ct));
            var location = (await set.JsonAsync(ct)).GetProperty("location");
            location.GetProperty("latitude").GetDouble().ShouldBe(24.8123);
            location.GetProperty("longitude").GetDouble().ShouldBe(46.6011);
            location.GetProperty("source").GetString().ShouldBe("Manual");
        }

        using (await admin.PutAsync($"/api/v1/admin/shops/{olaya}/location", new { latitude = 24.6930, longitude = 46.6850, source = "Geocoded" }, ct))
        {
        }

        using (var invalid = await admin.PutAsync($"/api/v1/admin/shops/{olaya}/location", new { latitude = 124.0, longitude = 46.0, source = "Manual" }, ct))
        {
            (await invalid.JsonAsync(ct)).GetProperty("errors").GetProperty("latitude")[0].GetString().ShouldBe("validation.coordinate_invalid");
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync(ct);

        async Task<string?> Scalar(string sql)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            return (await command.ExecuteScalarAsync(ct))?.ToString();
        }

        (await Scalar("SELECT format_type(atttypid, atttypmod) FROM pg_attribute WHERE attrelid = 'shops.shops'::regclass AND attname = 'location'"))
            .ShouldBe("geography(Point,4326)");
        (await Scalar("SELECT indexdef FROM pg_indexes WHERE schemaname = 'shops' AND indexname = 'ix_shops_location'"))!.ShouldContain("USING gist");

        // Axis order: X is longitude, Y is latitude.
        (await Scalar($"SELECT ST_Y(location::geometry) || ',' || ST_X(location::geometry) FROM shops.shops WHERE id = '{malqa}'")).ShouldBe("24.8123,46.6011");

        // Spatial smoke query: from Hittin, Al Malqa is nearer than Al Olaya (distances in metres).
        (await Scalar("""
            SELECT slug FROM shops.shops WHERE location IS NOT NULL
            ORDER BY ST_Distance(location, ST_SetSRID(ST_MakePoint(46.6010, 24.7630), 4326)::geography) LIMIT 1
            """)).ShouldBe("near-malqa");
        double.Parse((await Scalar($"SELECT ST_Distance(location, ST_SetSRID(ST_MakePoint(46.6010, 24.7630), 4326)::geography) FROM shops.shops WHERE id = '{malqa}'"))!, System.Globalization.CultureInfo.InvariantCulture)
            .ShouldBeInRange(5_000, 6_000);
    }

    [Fact]
    public async Task Shop_CannotEditLockedField()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "shop_policy", ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shops = await ShopTestData.CreateTwoShopsAsync(factory, admin, ct);
        using var owner = await IdentityTestData.SignInStaffAsync(factory, shops.A.OwnerEmail, ct);
        using var staff = await IdentityTestData.SignInStaffAsync(factory, shops.A.StaffEmail, ct);

        var own = await (await owner.GetAsync("/api/v1/shop/profile", ct)).JsonAsync(ct);
        own.GetProperty("id").GetGuid().ShouldBe(shops.A.ShopId);
        var nameAr = own.GetProperty("nameAr").GetString()!;
        var nameEn = own.GetProperty("nameEn").GetString()!;
        var version = own.GetProperty("version").GetUInt32();

        // The name is locked by default: changing it is refused, whatever else changes.
        using (var locked = await owner.PutAsync("/api/v1/shop/profile", OwnProfile("اسم جديد", nameEn, version), ct))
        {
            locked.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            var problem = await locked.JsonAsync(ct);
            problem.GetProperty("errorCode").GetString().ShouldBe("shop.profile_field_locked");
            problem.GetProperty("fields")[0].GetString().ShouldBe("Name");
        }

        // An open field (description), with the locked name sent unchanged, is accepted.
        using (var open = await owner.PutAsync("/api/v1/shop/profile", OwnProfile(nameAr, nameEn, version, "وصف المحل"), ct))
        {
            open.StatusCode.ShouldBe(HttpStatusCode.OK, await open.Content.ReadAsStringAsync(ct));
            version = (await open.JsonAsync(ct)).GetProperty("version").GetUInt32();
        }

        // Staff have no Shop.Profile.Edit.
        using (var asStaff = await staff.PutAsync("/api/v1/shop/profile", OwnProfile(nameAr, nameEn, version, "x"), ct))
        {
            asStaff.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await asStaff.ErrorCodeAsync(ct)).ShouldBe("auth.forbidden");
        }

        // Location is locked until the admin opens it.
        var point = new { latitude = 24.8, longitude = 46.6, source = "Device" };
        using (var lockedLocation = await owner.PutAsync("/api/v1/shop/location", point, ct))
        {
            lockedLocation.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using (var policy = await admin.PutAsync($"/api/v1/admin/shops/{shops.A.ShopId}/editable-policy", new { editableFields = new[] { "Name", "Location" } }, ct))
        {
            policy.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // The policy change is a change of the shop row: the owner's old version is now stale and must be re-read.
        using (var stale = await owner.PutAsync("/api/v1/shop/profile", OwnProfile("اسم جديد", nameEn, version, "وصف المحل"), ct))
        {
            stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }

        version = (await (await owner.GetAsync("/api/v1/shop/profile", ct)).JsonAsync(ct)).GetProperty("version").GetUInt32();
        using (var renamed = await owner.PutAsync("/api/v1/shop/profile", OwnProfile("اسم جديد", nameEn, version, "وصف المحل"), ct))
        {
            renamed.StatusCode.ShouldBe(HttpStatusCode.OK, await renamed.Content.ReadAsStringAsync(ct));
        }

        using (var located = await owner.PutAsync("/api/v1/shop/location", point, ct))
        {
            located.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await located.JsonAsync(ct)).GetProperty("location").GetProperty("source").GetString().ShouldBe("Device");
        }

        // Description is now locked (the new policy no longer lists it).
        var current = await (await owner.GetAsync("/api/v1/shop/profile", ct)).JsonAsync(ct);
        using (var nowLocked = await owner.PutAsync("/api/v1/shop/profile", OwnProfile("اسم جديد", nameEn, current.GetProperty("version").GetUInt32(), "وصف آخر"), ct))
        {
            nowLocked.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        // Shop B's data is untouched and B's owner sees only B.
        using var otherOwner = await IdentityTestData.SignInStaffAsync(factory, shops.B.OwnerEmail, ct);
        var other = await (await otherOwner.GetAsync("/api/v1/shop/profile", ct)).JsonAsync(ct);
        other.GetProperty("id").GetGuid().ShouldBe(shops.B.ShopId);
        other.GetProperty("location").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task SuspendedShop_HasNoProfileAccess_EvenThoughTheClaimRemains()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "shop_suspended_profile", ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shops = await ShopTestData.CreateTwoShopsAsync(factory, admin, ct);
        using var owner = await IdentityTestData.SignInStaffAsync(factory, shops.A.OwnerEmail, ct);
        (await owner.GetAsync("/api/v1/shop/profile", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        using (await admin.PostAsync($"/api/v1/admin/shops/{shops.A.ShopId}/suspend", new { }, ct))
        {
        }

        // Guard (handoff item 2): shop data follows ICurrentTenant, which is empty while suspended; /shop/me still reports the status.
        (await owner.GetAsync("/api/v1/shop/profile", ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using (var edit = await owner.PutAsync("/api/v1/shop/profile", OwnProfile("س", "S", 0), ct))
        {
            edit.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        (await (await owner.GetAsync("/api/v1/shop/professionals", ct)).JsonAsync(ct)).GetArrayLength().ShouldBe(0);
        (await (await owner.GetAsync("/api/v1/shop/me", ct)).JsonAsync(ct)).GetProperty("status").GetString().ShouldBe("Suspended");
    }

    [Fact]
    public async Task Images_AreStoredInTheDatabase_ValidatedByContent_AndServedImmutable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "shop_media", ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shopId = await ShopTestData.CreateShopAsync(admin, "media-shop", ct);
        using var anonymous = ApiSession.Create(factory);

        using var uploaded = await admin.UploadAsync(HttpMethod.Put, $"/api/v1/admin/shops/{shopId}/logo", TestImages.Png(256, 256, withTextChunk: true), ct);
        uploaded.StatusCode.ShouldBe(HttpStatusCode.OK, await uploaded.Content.ReadAsStringAsync(ct));
        var logoUrl = (await uploaded.JsonAsync(ct)).GetProperty("logoUrl").GetString()!;
        logoUrl.ShouldStartWith("/api/v1/media/");

        using (var served = await anonymous.GetAsync(logoUrl, ct))
        {
            served.StatusCode.ShouldBe(HttpStatusCode.OK);
            served.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
            served.Headers.CacheControl!.ToString().ShouldContain("immutable");
            served.Headers.ETag.ShouldNotBeNull();
            var bytes = await served.Content.ReadAsByteArrayAsync(ct);
            bytes.ShouldBe(TestImages.Png(256, 256), "stored without the text metadata chunk");

            using var conditional = new HttpRequestMessage(HttpMethod.Get, logoUrl);
            conditional.Headers.IfNoneMatch.Add(served.Headers.ETag!);
            using var notModified = await anonymous.Client.SendAsync(conditional, ct);
            notModified.StatusCode.ShouldBe(HttpStatusCode.NotModified);
        }

        // Content decides, not the declared type or name.
        using (var html = await admin.UploadAsync(HttpMethod.Put, $"/api/v1/admin/shops/{shopId}/cover", Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>"), ct, "cover.png"))
        {
            (await html.JsonAsync(ct)).GetProperty("errors").GetProperty("file")[0].GetString().ShouldBe("validation.image_type");
        }

        using (var svg = await admin.UploadAsync(HttpMethod.Put, $"/api/v1/admin/shops/{shopId}/cover", Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"), ct, "cover.svg", "image/svg+xml"))
        {
            (await svg.JsonAsync(ct)).GetProperty("errors").GetProperty("file")[0].GetString().ShouldBe("validation.image_type");
        }

        using (var small = await admin.UploadAsync(HttpMethod.Put, $"/api/v1/admin/shops/{shopId}/cover", TestImages.Png(40, 40), ct))
        {
            (await small.JsonAsync(ct)).GetProperty("errors").GetProperty("file")[0].GetString().ShouldBe("validation.image_too_small");
        }

        using (var huge = await admin.UploadAsync(HttpMethod.Put, $"/api/v1/admin/shops/{shopId}/cover", new byte[6 * 1024 * 1024], ct))
        {
            huge.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        }

        using (var jpeg = await admin.UploadAsync(HttpMethod.Put, $"/api/v1/admin/shops/{shopId}/cover", TestImages.Jpeg(1600, 900), ct, "cover.jpg", "image/jpeg"))
        {
            jpeg.StatusCode.ShouldBe(HttpStatusCode.OK);
            var coverUrl = (await jpeg.JsonAsync(ct)).GetProperty("coverUrl").GetString()!;
            var stored = await (await anonymous.GetAsync(coverUrl, ct)).Content.ReadAsByteArrayAsync(ct);
            TestImages.Contains(stored, TestImages.ExifMarker).ShouldBeFalse("EXIF (with GPS) is stripped before storage");
        }

        // Replacing deletes the old image in the same transaction.
        using var replaced = await admin.UploadAsync(HttpMethod.Put, $"/api/v1/admin/shops/{shopId}/logo", TestImages.Png(300, 300), ct);
        (await replaced.JsonAsync(ct)).GetProperty("logoUrl").GetString().ShouldNotBe(logoUrl);
        (await anonymous.GetAsync(logoUrl, ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var removed = await admin.DeleteAsync($"/api/v1/admin/shops/{shopId}/logo", ct);
        (await removed.JsonAsync(ct)).GetProperty("logoUrl").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
        (await db.Set<Trimme.BuildingBlocks.Infrastructure.Media.StoredMedia>().CountAsync(ct)).ShouldBe(1, "only the cover remains; replaced and removed images are deleted");
    }

    [Fact]
    public async Task CrossShop_GalleryImageCannotBeRemovedByAnotherShop()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "shop_gallery", ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shops = await ShopTestData.CreateTwoShopsAsync(factory, admin, ct);
        using var ownerA = await IdentityTestData.SignInStaffAsync(factory, shops.A.OwnerEmail, ct);
        using var ownerB = await IdentityTestData.SignInStaffAsync(factory, shops.B.OwnerEmail, ct);

        using var added = await ownerA.UploadAsync(HttpMethod.Post, "/api/v1/shop/profile/gallery", TestImages.Png(800, 600), ct);
        added.StatusCode.ShouldBe(HttpStatusCode.OK, await added.Content.ReadAsStringAsync(ct));
        var image = (await added.JsonAsync(ct)).GetProperty("gallery")[0];
        var imageId = image.GetProperty("id").GetGuid();

        using (var foreign = await ownerB.DeleteAsync($"/api/v1/shop/profile/gallery/{imageId}", ct))
        {
            foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        using var anonymous = ApiSession.Create(factory);
        (await anonymous.GetAsync(image.GetProperty("url").GetString()!, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        using (var own = await ownerA.DeleteAsync($"/api/v1/shop/profile/gallery/{imageId}", ct))
        {
            own.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await own.JsonAsync(ct)).GetProperty("gallery").GetArrayLength().ShouldBe(0);
        }
    }

    [Fact]
    public async Task PublicShopPage_ShowsOnlyActiveShops_ToEveryCaller()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "shop_public", ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shops = await ShopTestData.CreateTwoShopsAsync(factory, admin, ct);
        await ShopTestData.CreateShopAsync(admin, "draft-shop", ct, activate: false);
        var slugA = (await (await admin.GetAsync($"/api/v1/admin/shops/{shops.A.ShopId}", ct)).JsonAsync(ct)).GetProperty("slug").GetString()!;
        using (await admin.PutAsync($"/api/v1/admin/shops/{shops.A.ShopId}/location", new { latitude = 24.8123, longitude = 46.6011, district = "الملقا", source = "Manual" }, ct))
        {
        }

        using var anonymous = ApiSession.Create(factory);
        var page = await (await anonymous.GetAsync($"/api/v1/public/shops/{slugA}", ct)).JsonAsync(ct);
        page.GetProperty("location").GetProperty("district").GetString().ShouldBe("الملقا");
        page.TryGetProperty("editableFields", out _).ShouldBeFalse();
        page.GetProperty("location").TryGetProperty("confirmedBy", out _).ShouldBeFalse();

        (await anonymous.GetAsync("/api/v1/public/shops/draft-shop", ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A signed-in owner of shop B sees shop A's public page like anyone else (the public scope ignores the tenant).
        using var ownerB = await IdentityTestData.SignInStaffAsync(factory, shops.B.OwnerEmail, ct);
        (await ownerB.GetAsync($"/api/v1/public/shops/{slugA}", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        using (await admin.PostAsync($"/api/v1/admin/shops/{shops.A.ShopId}/suspend", new { }, ct))
        {
        }

        (await anonymous.GetAsync($"/api/v1/public/shops/{slugA}", ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Geocoding_UsesTheServerSideFakeProvider()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "shop_geo", ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);

        var results = await (await admin.GetAsync("/api/v1/admin/geo/search?q=%D8%A7%D9%84%D9%85%D9%84%D9%82%D8%A7&lang=ar", ct)).JsonAsync(ct);
        results.GetArrayLength().ShouldBe(1);
        results[0].GetProperty("district").GetString().ShouldBe("الملقا");

        var reverse = await (await admin.GetAsync("/api/v1/admin/geo/reverse?lat=24.7631&lng=46.6012&lang=en", ct)).JsonAsync(ct);
        reverse.GetProperty("district").GetString().ShouldBe("Hittin");
        reverse.GetProperty("latitude").GetDouble().ShouldBe(24.7631);

        (await admin.GetAsync("/api/v1/admin/geo/reverse?lat=51.5&lng=-0.12", ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await admin.GetAsync("/api/v1/admin/geo/search?q=a", ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
