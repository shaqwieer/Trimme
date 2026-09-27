using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.IntegrationTests.Identity;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.IntegrationTests.Tenancy;
using Trimme.Modules.Administration.Domain;
using Trimme.Modules.Identity.Domain;

namespace Trimme.IntegrationTests.Services;

/// <summary>
/// Shop-owned services and packages, admin categories/moderation/override, professional–service assignment and the
/// published catalogue (R-SVC-01..05, R-NEG-06, R-TEN-08).
/// </summary>
public sealed class CatalogTests(PostgresFixture postgres)
{
    private static object Service(string nameAr, decimal price, int duration, string? nameEn = null, Guid? categoryId = null, uint? version = null) =>
        version is { } v
            ? new { nameAr, nameEn, descriptionAr = (string?)null, descriptionEn = (string?)null, categoryId, price, durationMinutes = duration, onlineBookable = true, version = v }
            : new { nameAr, nameEn, descriptionAr = (string?)null, descriptionEn = (string?)null, categoryId, price, durationMinutes = duration, onlineBookable = true };

    private static async Task<JsonElement> CreateServiceAsync(ApiSession owner, string nameAr, decimal price, int duration, CancellationToken ct)
    {
        using var created = await owner.PostAsync("/api/v1/shop/services", Service(nameAr, price, duration), ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(ct));
        return await created.JsonAsync(ct);
    }

    private static async Task<JsonElement> CreatePackageAsync(ApiSession owner, IEnumerable<Guid> serviceIds, CancellationToken ct, decimal price = 85m)
    {
        using var created = await owner.PostAsync("/api/v1/shop/packages", new { nameAr = "باقة", nameEn = (string?)null, price, durationMinutes = 50, serviceIds }, ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(ct));
        return await created.JsonAsync(ct);
    }

    private static async Task<string> SlugAsync(ApiSession admin, Guid shopId, CancellationToken ct) =>
        (await (await admin.GetAsync($"/api/v1/admin/shops/{shopId}", ct)).JsonAsync(ct)).GetProperty("slug").GetString()!;

    [Fact]
    public async Task Shop_ServiceCrud_Flow()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "svc_crud", ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shops = await ShopTestData.CreateTwoShopsAsync(factory, admin, ct);
        using var owner = await IdentityTestData.SignInStaffAsync(factory, shops.A.OwnerEmail, ct);
        using var otherOwner = await IdentityTestData.SignInStaffAsync(factory, shops.B.OwnerEmail, ct);
        using var staff = await IdentityTestData.SignInStaffAsync(factory, shops.A.StaffEmail, ct);

        var cut = await CreateServiceAsync(owner, "حلاقة شعر", 60.5m, 35, ct);
        var beard = await CreateServiceAsync(owner, "تهذيب لحية", 35m, 20, ct);
        var theirs = await CreateServiceAsync(otherOwner, "حلاقة شعر", 60.5m, 35, ct);
        cut.GetProperty("nameEn").ValueKind.ShouldBe(JsonValueKind.Null, "English is optional (D-070)");
        cut.GetProperty("currency").GetString().ShouldBe("SAR");
        cut.GetProperty("displayOrder").GetInt32().ShouldBe(1);
        beard.GetProperty("displayOrder").GetInt32().ShouldBe(2);
        var cutId = cut.GetProperty("id").GetGuid();

        // The shop changes its own price and duration; only its own record changes.
        using (var updated = await owner.PutAsync($"/api/v1/shop/services/{cutId}", Service("حلاقة شعر", 65m, 40, "Haircut", version: cut.GetProperty("version").GetUInt32()), ct))
        {
            updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync(ct));
            var body = await updated.JsonAsync(ct);
            body.GetProperty("price").GetDecimal().ShouldBe(65m);
            body.GetProperty("durationMinutes").GetInt32().ShouldBe(40);
            body.GetProperty("nameEn").GetString().ShouldBe("Haircut");
        }

        var theirsNow = await (await otherOwner.GetAsync($"/api/v1/shop/services/{theirs.GetProperty("id").GetGuid()}", ct)).JsonAsync(ct);
        theirsNow.GetProperty("price").GetDecimal().ShouldBe(60.5m);
        theirsNow.GetProperty("durationMinutes").GetInt32().ShouldBe(35);

        // Stale version, invalid price/duration, staff without Shop.Services.Manage.
        (await owner.PutAsync($"/api/v1/shop/services/{cutId}", Service("س", 1m, 5, version: cut.GetProperty("version").GetUInt32()), ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using (var invalid = await owner.PostAsync("/api/v1/shop/services", Service("س", 10.005m, 7), ct))
        {
            var errors = (await invalid.JsonAsync(ct)).GetProperty("errors");
            errors.GetProperty("price")[0].GetString().ShouldBe("validation.price_invalid");
            errors.GetProperty("durationMinutes")[0].GetString().ShouldBe("validation.duration_invalid");
        }

        (await staff.PostAsync("/api/v1/shop/services", Service("س", 10m, 10), ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await staff.GetAsync("/api/v1/shop/services", ct)).StatusCode.ShouldBe(HttpStatusCode.OK, "staff can read the list (walk-ins)");

        // Off, on, reorder (full set required), archive.
        (await (await owner.PostAsync($"/api/v1/shop/services/{cutId}/deactivate", new { }, ct)).JsonAsync(ct)).GetProperty("isActive").GetBoolean().ShouldBeFalse();
        (await (await owner.PostAsync($"/api/v1/shop/services/{cutId}/activate", new { }, ct)).JsonAsync(ct)).GetProperty("isActive").GetBoolean().ShouldBeTrue();
        var beardId = beard.GetProperty("id").GetGuid();
        using (var reordered = await owner.PutAsync("/api/v1/shop/services/order", new { orderedIds = new[] { beardId, cutId } }, ct))
        {
            (await reordered.JsonAsync(ct)).EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).ShouldBe([beardId, cutId]);
        }

        using (var partial = await owner.PutAsync("/api/v1/shop/services/order", new { orderedIds = new[] { cutId } }, ct))
        {
            (await partial.JsonAsync(ct)).GetProperty("errors").GetProperty("orderedIds")[0].GetString().ShouldBe("validation.order_mismatch");
        }

        (await owner.PostAsync($"/api/v1/shop/services/{cutId}/archive", new { }, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterArchive = await owner.PutAsync($"/api/v1/shop/services/{cutId}", Service("س", 10m, 10, version: 0), ct);
        afterArchive.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await afterArchive.ErrorCodeAsync(ct)).ShouldBe("catalog.archived");
        (await (await owner.GetAsync("/api/v1/shop/services", ct)).JsonAsync(ct)).GetArrayLength().ShouldBe(1);
        (await (await owner.GetAsync("/api/v1/shop/services?includeArchived=true", ct)).JsonAsync(ct)).GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task Service_InPackage_CannotBeDeleted_OnlyArchived()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "svc_delete", ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shops = await ShopTestData.CreateTwoShopsAsync(factory, admin, ct);
        using var owner = await IdentityTestData.SignInStaffAsync(factory, shops.A.OwnerEmail, ct);

        var a = (await CreateServiceAsync(owner, "أ", 10m, 10, ct)).GetProperty("id").GetGuid();
        var b = (await CreateServiceAsync(owner, "ب", 10m, 10, ct)).GetProperty("id").GetGuid();
        var unused = (await CreateServiceAsync(owner, "ج", 10m, 10, ct)).GetProperty("id").GetGuid();
        await CreatePackageAsync(owner, [a, b], ct);

        using (var inUse = await owner.DeleteAsync($"/api/v1/shop/services/{a}", ct))
        {
            inUse.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await inUse.ErrorCodeAsync(ct)).ShouldBe("service.in_use");
        }

        (await owner.DeleteAsync($"/api/v1/shop/services/{unused}", ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.GetAsync($"/api/v1/shop/services/{unused}", ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.PostAsync($"/api/v1/shop/services/{a}/archive", new { }, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CrossShop_ServiceAndPackage_EveryVerb_Is404_AndNothingChanges()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "svc_crossshop", ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shops = await ShopTestData.CreateTwoShopsAsync(factory, admin, ct);
        using var ownerA = await IdentityTestData.SignInStaffAsync(factory, shops.A.OwnerEmail, ct);
        using var ownerB = await IdentityTestData.SignInStaffAsync(factory, shops.B.OwnerEmail, ct);

        var s1 = await CreateServiceAsync(ownerA, "أ", 50m, 30, ct);
        var s2 = await CreateServiceAsync(ownerA, "ب", 30m, 20, ct);
        var serviceId = s1.GetProperty("id").GetGuid();
        var package = await CreatePackageAsync(ownerA, [serviceId, s2.GetProperty("id").GetGuid()], ct);
        var packageId = package.GetProperty("id").GetGuid();
        var mine = (await CreateServiceAsync(ownerB, "خ", 20m, 10, ct)).GetProperty("id").GetGuid();

        var attempts = new (HttpMethod Method, string Path, object? Body)[]
        {
            (HttpMethod.Get, $"/api/v1/shop/services/{serviceId}", null),
            (HttpMethod.Put, $"/api/v1/shop/services/{serviceId}", Service("مخترق", 1m, 5, version: s1.GetProperty("version").GetUInt32())),
            (HttpMethod.Post, $"/api/v1/shop/services/{serviceId}/activate", new { }),
            (HttpMethod.Post, $"/api/v1/shop/services/{serviceId}/deactivate", new { }),
            (HttpMethod.Post, $"/api/v1/shop/services/{serviceId}/archive", new { }),
            (HttpMethod.Delete, $"/api/v1/shop/services/{serviceId}", null),
            (HttpMethod.Get, $"/api/v1/shop/packages/{packageId}", null),
            (HttpMethod.Put, $"/api/v1/shop/packages/{packageId}", new { nameAr = "م", price = 1m, durationMinutes = 5, serviceIds = new[] { mine, serviceId }, version = package.GetProperty("version").GetUInt32() }),
            (HttpMethod.Post, $"/api/v1/shop/packages/{packageId}/activate", new { }),
            (HttpMethod.Post, $"/api/v1/shop/packages/{packageId}/deactivate", new { }),
            (HttpMethod.Post, $"/api/v1/shop/packages/{packageId}/archive", new { }),
        };
        foreach (var (method, path, body) in attempts)
        {
            using var response = await ownerB.SendAsync(method, path, body, ct);
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound, $"{method} {path}");
        }

        // Foreign ids smuggled into B's own requests are refused too.
        using (var foreignItems = await ownerB.PostAsync("/api/v1/shop/packages", new { nameAr = "م", price = 1m, durationMinutes = 5, serviceIds = new[] { mine, serviceId } }, ct))
        {
            (await foreignItems.JsonAsync(ct)).GetProperty("errors").GetProperty("serviceIds")[0].GetString().ShouldBe("validation.invalid");
        }

        using (var foreignOrder = await ownerB.PutAsync("/api/v1/shop/services/order", new { orderedIds = new[] { mine, serviceId } }, ct))
        {
            foreignOrder.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        (await (await ownerB.GetAsync("/api/v1/shop/services", ct)).JsonAsync(ct)).EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).ShouldBe([mine]);

        // Shop A's data is untouched.
        var still = await (await ownerA.GetAsync($"/api/v1/shop/services/{serviceId}", ct)).JsonAsync(ct);
        still.GetProperty("nameAr").GetString().ShouldBe("أ");
        still.GetProperty("price").GetDecimal().ShouldBe(50m);
        still.GetProperty("isActive").GetBoolean().ShouldBeTrue();
        still.GetProperty("isArchived").GetBoolean().ShouldBeFalse();
        (await (await ownerA.GetAsync($"/api/v1/shop/packages/{packageId}", ct)).JsonAsync(ct)).GetProperty("isActive").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Packages_ConcurrencyOnItemsOnlyEdits_AndPublishedOnlyWhenEveryItemIsAvailable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "svc_packages", ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shops = await ShopTestData.CreateTwoShopsAsync(factory, admin, ct);
        using var owner = await IdentityTestData.SignInStaffAsync(factory, shops.A.OwnerEmail, ct);
        var slug = await SlugAsync(admin, shops.A.ShopId, ct);

        var a = (await CreateServiceAsync(owner, "أ", 10m, 10, ct)).GetProperty("id").GetGuid();
        var b = (await CreateServiceAsync(owner, "ب", 10m, 10, ct)).GetProperty("id").GetGuid();
        var c = (await CreateServiceAsync(owner, "ج", 10m, 10, ct)).GetProperty("id").GetGuid();
        var package = await CreatePackageAsync(owner, [a, b], ct);
        var packageId = package.GetProperty("id").GetGuid();
        var version = package.GetProperty("version").GetUInt32();
        package.GetProperty("isBookable").GetBoolean().ShouldBeTrue();

        // Two editors change only the items from the same version: the second loses.
        object Edit(Guid[] items) => new { nameAr = "باقة", nameEn = (string?)null, price = 85m, durationMinutes = 50, serviceIds = items, version };
        (await owner.PutAsync($"/api/v1/shop/packages/{packageId}", Edit([a, c]), ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.PutAsync($"/api/v1/shop/packages/{packageId}", Edit([b, c]), ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await (await owner.GetAsync($"/api/v1/shop/packages/{packageId}", ct)).JsonAsync(ct)).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("serviceId").GetGuid()).ShouldBe([a, c]);

        using var anonymous = ApiSession.Create(factory);
        (await (await anonymous.GetAsync($"/api/v1/public/shops/{slug}/packages", ct)).JsonAsync(ct)).GetArrayLength().ShouldBe(1);

        // An item service switched off: the package stays but is not bookable, and is not published (D-072).
        (await owner.PostAsync($"/api/v1/shop/services/{c}/deactivate", new { }, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await (await owner.GetAsync($"/api/v1/shop/packages/{packageId}", ct)).JsonAsync(ct)).GetProperty("isBookable").GetBoolean().ShouldBeFalse();
        (await (await anonymous.GetAsync($"/api/v1/public/shops/{slug}/packages", ct)).JsonAsync(ct)).GetArrayLength().ShouldBe(0);
        var published = await (await anonymous.GetAsync($"/api/v1/public/shops/{slug}/services", ct)).JsonAsync(ct);
        published.EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).ShouldBe([a, b], ignoreOrder: true);
    }

    [Fact]
    public async Task Admin_OverrideService_Audited_AndModerationHidesFromCustomers()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "svc_admin", ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        using var support = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.Support, ct);
        var shops = await ShopTestData.CreateTwoShopsAsync(factory, admin, ct);
        using var owner = await IdentityTestData.SignInStaffAsync(factory, shops.A.OwnerEmail, ct);
        var slug = await SlugAsync(admin, shops.A.ShopId, ct);
        var service = await CreateServiceAsync(owner, "حلاقة", 60m, 30, ct);
        var id = service.GetProperty("id").GetGuid();

        // Platform-wide list shows the shop's own price; Support may look but not override.
        var list = await (await support.GetAsync($"/api/v1/admin/services?shopId={shops.A.ShopId}", ct)).JsonAsync(ct);
        list.GetProperty("items")[0].GetProperty("price").GetDecimal().ShouldBe(60m);
        object Override(string reason, uint version) => new { nameAr = "حلاقة", nameEn = "Haircut", price = 55m, durationMinutes = 25, onlineBookable = true, reason, version };
        (await support.PutAsync($"/api/v1/admin/services/{id}/override", Override("Shop asked by phone", 0), ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using (var noReason = await admin.PutAsync($"/api/v1/admin/services/{id}/override", Override(" ", service.GetProperty("version").GetUInt32()), ct))
        {
            (await noReason.JsonAsync(ct)).GetProperty("errors").GetProperty("reason")[0].GetString().ShouldBe("validation.reason_required");
        }

        using (var overridden = await admin.PutAsync($"/api/v1/admin/services/{id}/override", Override("Shop asked by phone", service.GetProperty("version").GetUInt32()), ct))
        {
            overridden.StatusCode.ShouldBe(HttpStatusCode.OK, await overridden.Content.ReadAsStringAsync(ct));
            (await overridden.JsonAsync(ct)).GetProperty("price").GetDecimal().ShouldBe(55m);
        }

        // Moderation: hide needs a reason; hidden services are not published; the shop sees why.
        using (var hideNoReason = await admin.PostAsync($"/api/v1/admin/services/{id}/moderation", new { action = "Hide" }, ct))
        {
            hideNoReason.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        (await admin.PostAsync($"/api/v1/admin/services/{id}/moderation", new { action = "Hide", reason = "Misleading description" }, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var anonymous = ApiSession.Create(factory);
        (await (await anonymous.GetAsync($"/api/v1/public/shops/{slug}/services", ct)).JsonAsync(ct)).GetArrayLength().ShouldBe(0);
        var own = await (await owner.GetAsync($"/api/v1/shop/services/{id}", ct)).JsonAsync(ct);
        own.GetProperty("moderation").GetString().ShouldBe("Hidden");
        own.GetProperty("moderationReason").GetString().ShouldBe("Misleading description");
        (await admin.PostAsync($"/api/v1/admin/services/{id}/moderation", new { action = "Unhide" }, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await (await anonymous.GetAsync($"/api/v1/public/shops/{slug}/services", ct)).JsonAsync(ct)).GetArrayLength().ShouldBe(1);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
        var overrideEntry = await db.Set<AuditEntry>().SingleAsync(e => e.Action == "service.support_override", ct);
        overrideEntry.Summary.ShouldBe("Price 60.00 → 55.00 SAR; Duration 30 → 25 min; Name changed");
        overrideEntry.Reason.ShouldBe("Shop asked by phone");
        overrideEntry.ShopId.ShouldBe(shops.A.ShopId);
        (await db.Set<AuditEntry>().CountAsync(e => e.EntityId == id.ToString() && (e.Action == "service.hidden" || e.Action == "service.unhidden"), ct)).ShouldBe(2);
    }

    [Fact]
    public async Task Admin_AssignsProfessionalService_SameShopOnly_AndShopCannotAssign()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "svc_assign", ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shops = await ShopTestData.CreateTwoShopsAsync(factory, admin, ct);
        using var ownerA = await IdentityTestData.SignInStaffAsync(factory, shops.A.OwnerEmail, ct);
        using var ownerB = await IdentityTestData.SignInStaffAsync(factory, shops.B.OwnerEmail, ct);
        var cut = (await CreateServiceAsync(ownerA, "حلاقة", 60m, 30, ct)).GetProperty("id").GetGuid();
        var beard = (await CreateServiceAsync(ownerA, "لحية", 30m, 20, ct)).GetProperty("id").GetGuid();
        var foreign = (await CreateServiceAsync(ownerB, "حلاقة", 50m, 30, ct)).GetProperty("id").GetGuid();
        using var created = await admin.PostAsync("/api/v1/admin/professionals", new { shopId = shops.A.ShopId, nameAr = "فيصل", nameEn = "Faisal" }, ct);
        var professional = (await created.JsonAsync(ct)).GetProperty("id").GetGuid();

        using (var assigned = await admin.PutAsync($"/api/v1/admin/professionals/{professional}/services", new { serviceIds = new[] { cut, beard } }, ct))
        {
            assigned.StatusCode.ShouldBe(HttpStatusCode.OK, await assigned.Content.ReadAsStringAsync(ct));
            var options = (await assigned.JsonAsync(ct)).GetProperty("services").EnumerateArray().ToList();
            options.Select(o => o.GetProperty("serviceId").GetGuid()).ShouldBe([cut, beard], ignoreOrder: true, "only the professional's own shop's services are offered");
            options.ShouldAllBe(o => o.GetProperty("assigned").GetBoolean());
        }

        using (var crossShop = await admin.PutAsync($"/api/v1/admin/professionals/{professional}/services", new { serviceIds = new[] { cut, foreign } }, ct))
        {
            (await crossShop.JsonAsync(ct)).GetProperty("errors").GetProperty("serviceIds")[0].GetString().ShouldBe("validation.invalid");
        }

        // Shop users cannot assign (R-NEG-06), and no shop-side assignment route exists.
        (await ownerA.PutAsync($"/api/v1/admin/professionals/{professional}/services", new { serviceIds = new[] { cut } }, ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await (await ownerA.GetAsync("/api/v1/shop/services", ct)).JsonAsync(ct)).EnumerateArray()
            .Single(s => s.GetProperty("id").GetGuid() == cut).GetProperty("assignedProfessionalCount").GetInt32().ShouldBe(1);

        // The database itself rejects a cross-shop pairing.
        await using var scope = factory.Services.CreateAsyncScope();
        var connection = (NpgsqlConnection)scope.ServiceProvider.GetRequiredService<TrimmeDbContext>().Database.GetDbConnection();
        await connection.OpenAsync(ct);
        await using var insert = new NpgsqlCommand(
            "INSERT INTO services.professional_services (professional_id, service_id, shop_id, assigned_at) VALUES (@p, @s, @shop, now())", connection);
        insert.Parameters.AddWithValue("p", professional);
        insert.Parameters.AddWithValue("s", foreign);
        insert.Parameters.AddWithValue("shop", shops.A.ShopId);
        (await Should.ThrowAsync<PostgresException>(() => insert.ExecuteNonQueryAsync(ct))).SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task SuspendedShop_CatalogCommands_Are404_Not500()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "svc_suspended", ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shops = await ShopTestData.CreateTwoShopsAsync(factory, admin, ct);
        using var owner = await IdentityTestData.SignInStaffAsync(factory, shops.A.OwnerEmail, ct);
        var id = (await CreateServiceAsync(owner, "أ", 10m, 10, ct)).GetProperty("id").GetGuid();
        (await admin.PostAsync($"/api/v1/admin/shops/{shops.A.ShopId}/suspend", new { }, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await owner.PostAsync("/api/v1/shop/services", Service("ب", 10m, 10), ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.PostAsync("/api/v1/shop/packages", new { nameAr = "ب", price = 1m, durationMinutes = 5, serviceIds = new[] { id, id } }, ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.PutAsync($"/api/v1/shop/services/{id}", Service("ب", 10m, 10, version: 0), ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.PutAsync("/api/v1/shop/services/order", new { orderedIds = new[] { id } }, ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.GetAsync("/api/v1/shop/services", ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Categories_AdminManaged_BothLanguages_ActiveOnesPublishedAndSelectable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "svc_categories", ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shops = await ShopTestData.CreateTwoShopsAsync(factory, admin, ct);
        using var owner = await IdentityTestData.SignInStaffAsync(factory, shops.A.OwnerEmail, ct);

        using (var missingEnglish = await admin.PostAsync("/api/v1/admin/service-categories", new { nameAr = "شعر", nameEn = "", icon = "scissors", displayOrder = 1 }, ct))
        {
            (await missingEnglish.JsonAsync(ct)).GetProperty("errors").GetProperty("nameEn")[0].GetString().ShouldBe("validation.required");
        }

        using var created = await admin.PostAsync("/api/v1/admin/service-categories", new { nameAr = "شعر", nameEn = "Hair", icon = "scissors", displayOrder = 1 }, ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var categoryId = (await created.JsonAsync(ct)).GetProperty("id").GetGuid();

        using (var withCategory = await owner.PostAsync("/api/v1/shop/services", Service("حلاقة", 60m, 30, categoryId: categoryId), ct))
        {
            withCategory.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        var admins = await (await admin.GetAsync("/api/v1/admin/service-categories", ct)).JsonAsync(ct);
        admins.EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == categoryId).GetProperty("serviceCount").GetInt32().ShouldBe(1);

        (await admin.PostAsync($"/api/v1/admin/service-categories/{categoryId}/deactivate", new { }, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var anonymous = ApiSession.Create(factory);
        (await (await anonymous.GetAsync("/api/v1/public/service-categories", ct)).JsonAsync(ct)).GetArrayLength().ShouldBe(0);
        using (var inactive = await owner.PostAsync("/api/v1/shop/services", Service("لحية", 30m, 20, categoryId: categoryId), ct))
        {
            (await inactive.JsonAsync(ct)).GetProperty("errors").GetProperty("categoryId")[0].GetString().ShouldBe("validation.invalid");
        }

        (await owner.PostAsync("/api/v1/admin/service-categories", new { nameAr = "س", nameEn = "S", icon = "tag", displayOrder = 2 }, ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
