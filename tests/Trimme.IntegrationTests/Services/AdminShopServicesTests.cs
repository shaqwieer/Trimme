using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.IntegrationTests.Identity;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.Modules.Administration.Domain;
using Trimme.Modules.Identity.Domain;
using static Trimme.IntegrationTests.Bookings.BookingTestData;

namespace Trimme.IntegrationTests.Services;

/// <summary>
/// D-127: a platform admin builds a shop's catalogue for it. The service keeps the shop's own price and duration, only
/// the shop's own barbers can do it, the customer sees it at once (shop page and availability), and every change is
/// audited. Support may look but not add or edit.
/// </summary>
public sealed class AdminShopServicesTests(PostgresFixture postgres)
{
    private static object Body(decimal price, int duration, IEnumerable<Guid>? professionalIds, uint version = 0) => new
    {
        nameAr = "صبغة",
        nameEn = "Colour",
        descriptionAr = (string?)null,
        descriptionEn = (string?)null,
        categoryId = (Guid?)null,
        price,
        durationMinutes = duration,
        onlineBookable = true,
        professionalIds,
        version,
    };

    [Fact]
    public async Task Admin_AddsAndEditsAShopsService_WithItsOwnBarbers_Published_AndAudited()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "svc_admin_build", ct);
        using var support = await IdentityTestData.SignInNewStaffAsync(w.Factory, SystemRoles.Support, ct);
        using var anonymous = ApiSession.Create(w.Factory);
        var shopA = w.Shops.A.ShopId;
        var target = TodayAt(DateTimeOffset.UtcNow).AddDays(2);

        (await support.PostAsync($"/api/v1/admin/shops/{shopA}/services", Body(90m, 45, [w.Faisal]), ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var otherShopsBarber = await FailsAsync(w.Admin.PostAsync($"/api/v1/admin/shops/{shopA}/services", Body(90m, 45, [w.ProB]), ct), HttpStatusCode.BadRequest, "professionalIds", ct);
        otherShopsBarber.ShouldContain("validation.invalid");
        await FailsAsync(w.Admin.PostAsync($"/api/v1/admin/shops/{Guid.NewGuid()}/services", Body(90m, 45, []), ct), HttpStatusCode.NotFound, "shop.not_found", ct);

        var created = await OkAsync(w.Admin.PostAsync($"/api/v1/admin/shops/{shopA}/services", Body(90m, 45, [w.Faisal]), ct), ct, HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();
        created.GetProperty("shopId").GetGuid().ShouldBe(shopA);
        created.GetProperty("price").GetDecimal().ShouldBe(90m);
        created.GetProperty("currency").GetString().ShouldBe("SAR");
        created.GetProperty("professionalIds").EnumerateArray().Select(p => p.GetGuid()).ShouldBe([w.Faisal]);

        // The customer sees it at once: on the shop's services and in its availability, with Faisal only.
        var published = await OkAsync(anonymous.GetAsync($"/api/v1/public/shops/{w.SlugA}/services", ct), ct);
        var listed = published.EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == id);
        listed.GetProperty("price").GetDecimal().ShouldBe(90m);
        listed.GetProperty("professionalIds").EnumerateArray().Select(p => p.GetGuid()).ShouldBe([w.Faisal]);
        var slots = await OkAsync(anonymous.GetAsync($"/api/v1/public/shops/{w.SlugA}/availability/slots?serviceId={id}&date={Iso(target)}", ct), ct);
        slots.GetProperty("slots").GetArrayLength().ShouldBeGreaterThan(0);
        slots.GetProperty("slots").EnumerateArray()
            .SelectMany(s => s.GetProperty("professionalIds").EnumerateArray().Select(p => p.GetGuid())).Distinct().ShouldBe([w.Faisal]);

        // An edit needs the version read; Support cannot edit.
        var version = created.GetProperty("version").GetUInt32();
        (await support.PutAsync($"/api/v1/admin/services/{id}", Body(95m, 50, null, version), ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await FailsAsync(w.Admin.PutAsync($"/api/v1/admin/services/{id}", Body(95m, 50, null, version + 1), ct), HttpStatusCode.Conflict, null, ct);
        var edited = await OkAsync(w.Admin.PutAsync($"/api/v1/admin/services/{id}", Body(95m, 50, [w.Faisal, w.Omar], version), ct), ct);
        edited.GetProperty("price").GetDecimal().ShouldBe(95m);
        edited.GetProperty("durationMinutes").GetInt32().ShouldBe(50);
        edited.GetProperty("professionalIds").EnumerateArray().Select(p => p.GetGuid()).ShouldBe([w.Faisal, w.Omar], ignoreOrder: true);

        // Leaving the barbers out of an edit keeps them.
        var kept = await OkAsync(w.Admin.PutAsync($"/api/v1/admin/services/{id}", Body(95m, 50, null, edited.GetProperty("version").GetUInt32()), ct), ct);
        kept.GetProperty("assignedProfessionalCount").GetInt32().ShouldBe(2);

        // The shop sees the service as its own.
        using var ownerA = await IdentityTestData.SignInStaffAsync(w.Factory, w.Shops.A.OwnerEmail, ct);
        (await OkAsync(ownerA.GetAsync($"/api/v1/shop/services/{id}", ct), ct)).GetProperty("price").GetDecimal().ShouldBe(95m);
        (await w.OwnerB.GetAsync($"/api/v1/shop/services/{id}", ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await using var scope = w.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
        var audit = await db.Set<AuditEntry>().AsNoTracking().Where(e => e.EntityId == id.ToString()).OrderBy(e => e.Sequence).ToListAsync(ct);
        audit.Select(a => a.Action).ShouldBe(["service.admin_created", "service.admin_updated", "service.admin_updated"]);
    }
}
