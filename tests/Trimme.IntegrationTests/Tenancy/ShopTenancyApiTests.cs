using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.IntegrationTests.Identity;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.Modules.Administration.Domain;
using Trimme.Modules.Identity.Domain;
using Trimme.Modules.Identity.Infrastructure.Persistence;

namespace Trimme.IntegrationTests.Tenancy;

/// <summary>Shop lifecycle, shop-user accounts and claims-only tenancy over HTTP (R-SHP-01, R-AUTH-02, R-TEN-01/06/08).</summary>
public sealed partial class ShopTenancyApiTests(PostgresFixture postgres, MailpitFixture mailpit)
{
    [Fact]
    public async Task Admin_CreatesShop_WithOwnerInvite()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "shop_invite", ct, mailpit.SmtpSettings);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);

        // Draft until activated.
        using var created = await admin.PostAsync("/api/v1/admin/shops", new { slug = "Al-Asala-Riyadh", nameAr = "صالون الأصالة", nameEn = "Al Asala Salon" }, ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var shop = await created.JsonAsync(ct);
        shop.GetProperty("status").GetString().ShouldBe("Draft");
        shop.GetProperty("slug").GetString().ShouldBe("al-asala-riyadh");
        shop.GetProperty("timeZone").GetString().ShouldBe("Asia/Riyadh");
        var shopId = shop.GetProperty("id").GetGuid();

        using (var duplicate = await admin.PostAsync("/api/v1/admin/shops", new { slug = "al-asala-riyadh", nameAr = "س", nameEn = "S" }, ct))
        {
            (await duplicate.JsonAsync(ct)).GetProperty("errors").GetProperty("slug")[0].GetString().ShouldBe("validation.slug_taken");
        }

        var owner = IdentityTestData.NewEmail("owner");
        using (var invite = await admin.PostAsync(
                   $"/api/v1/admin/shops/{shopId}/users/invitations", new { email = owner, role = SystemRoles.ShopOwner, locale = "ar" }, ct))
        {
            invite.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        var message = await mailpit.WaitForMessageAsync(owner, ct);
        message.Html.ShouldContain("مالك المحل");
        var token = System.Web.HttpUtility.ParseQueryString(new Uri(LinkPattern().Match(message.Text).Value).Query)["token"];

        using var browser = ApiSession.Create(factory);
        using (var accept = await browser.PostAsync(
                   "/api/v1/auth/invitations/accept", new { token, displayName = "Owner", password = "shop owner passphrase" }, ct))
        {
            accept.StatusCode.ShouldBe(HttpStatusCode.OK);
            var user = (await accept.JsonAsync(ct)).GetProperty("user");
            user.GetProperty("userType").GetString().ShouldBe("ShopUser");
            user.GetProperty("shopId").GetGuid().ShouldBe(shopId);
            user.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ShouldContain(Permissions.Shop.ServicesManage);
        }

        // A Draft shop's owner can sign in and set the shop up; the tenant is their shop.
        using var myShop = await browser.GetAsync("/api/v1/shop/me", ct);
        var mine = await myShop.JsonAsync(ct);
        mine.GetProperty("id").GetGuid().ShouldBe(shopId);
        mine.GetProperty("status").GetString().ShouldBe("Draft");
        (await ProbeAsync(browser, ct)).Tenant.ShouldBe(shopId);
    }

    [Fact]
    public async Task Admin_InvitesShopUser_OnlyForAnExistingShop_AndShopRoles()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "shop_invite_rules", ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shopId = await ShopTestData.CreateShopAsync(admin, $"s-{Guid.NewGuid():N}"[..20], ct);

        using (var unknownShop = await admin.PostAsync(
                   $"/api/v1/admin/shops/{Guid.NewGuid()}/users/invitations", new { email = IdentityTestData.NewEmail(), role = SystemRoles.ShopOwner }, ct))
        {
            unknownShop.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await unknownShop.ErrorCodeAsync(ct)).ShouldBe("shop.not_found");
        }

        foreach (var role in new[] { SystemRoles.SuperAdmin, SystemRoles.Customer })
        {
            using var wrongRole = await admin.PostAsync(
                $"/api/v1/admin/shops/{shopId}/users/invitations", new { email = IdentityTestData.NewEmail(), role }, ct);
            wrongRole.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        // Operations managers have Admin.Shops.ManageAccount; Support does not.
        using var support = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.Support, ct);
        using var forbidden = await support.PostAsync(
            $"/api/v1/admin/shops/{shopId}/users/invitations", new { email = IdentityTestData.NewEmail(), role = SystemRoles.ShopStaff }, ct);
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ShopEndpoints_IgnoreClientSuppliedShopId()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "shop_claims", ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shops = await ShopTestData.CreateTwoShopsAsync(factory, admin, ct);
        using var ownerA = await IdentityTestData.SignInStaffAsync(factory, shops.A.OwnerEmail, ct);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/shop/me?shopId={shops.B.ShopId}");
        request.Headers.Add("X-Shop-Id", shops.B.ShopId.ToString());
        using var response = await ownerA.Client.SendAsync(request, ct);

        (await response.JsonAsync(ct)).GetProperty("id").GetGuid().ShouldBe(shops.A.ShopId);
        var probe = await ProbeAsync(ownerA, ct);
        probe.Tenant.ShouldBe(shops.A.ShopId);
        probe.VisibleShops.ShouldBe(1, "A shop user sees only its own shop row.");

        // Other shops' admin views are simply forbidden to shop users (no existence leak either way).
        using var foreign = await ownerA.GetAsync($"/api/v1/admin/shops/{shops.B.ShopId}", ct);
        foreign.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Staff of shop B resolve to shop B.
        using var staffB = await IdentityTestData.SignInStaffAsync(factory, shops.B.StaffEmail, ct);
        (await ProbeAsync(staffB, ct)).Tenant.ShouldBe(shops.B.ShopId);

        // Non-shop callers have no tenant (shop-owned data is deny-all for them).
        (await ProbeAsync(admin, ct)).Tenant.ShouldBeNull();
        using var customer = await IdentityTestData.SignInCustomerAsync(factory, IdentityTestData.NewPhone(), ct);
        (await ProbeAsync(customer, ct)).Tenant.ShouldBeNull();
    }

    [Fact]
    public async Task Suspending_A_Shop_RevokesTenantAccess_Immediately()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "shop_suspend", ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shops = await ShopTestData.CreateTwoShopsAsync(factory, admin, ct);
        using var owner = await IdentityTestData.SignInStaffAsync(factory, shops.A.OwnerEmail, ct);
        (await ProbeAsync(owner, ct)).Tenant.ShouldBe(shops.A.ShopId);

        using (var suspend = await admin.PostAsync($"/api/v1/admin/shops/{shops.A.ShopId}/suspend", new { reason = "Unpaid subscription" }, ct))
        {
            (await suspend.JsonAsync(ct)).GetProperty("status").GetString().ShouldBe("Suspended");
        }

        // Same session, next request: no tenant, but the dashboard can still explain why.
        (await ProbeAsync(owner, ct)).Tenant.ShouldBeNull();
        using (var me = await owner.GetAsync("/api/v1/shop/me", ct))
        {
            (await me.JsonAsync(ct)).GetProperty("status").GetString().ShouldBe("Suspended");
        }

        using (var again = await admin.PostAsync($"/api/v1/admin/shops/{shops.A.ShopId}/suspend", new { }, ct))
        {
            again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }

        using (var activate = await admin.PostAsync($"/api/v1/admin/shops/{shops.A.ShopId}/activate", new { }, ct))
        {
            activate.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await ProbeAsync(owner, ct)).Tenant.ShouldBe(shops.A.ShopId);
    }

    [Fact]
    public async Task AdminShopActions_AreAudited_WithoutPersonalData()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "shop_audit", ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shopId = await ShopTestData.CreateShopAsync(admin, $"audit-{Guid.NewGuid():N}"[..20], ct);
        var invitee = IdentityTestData.NewEmail("audited");
        using (await admin.PostAsync($"/api/v1/admin/shops/{shopId}/suspend", new { reason = "Complaint review" }, ct))
        using (await admin.PostAsync($"/api/v1/admin/shops/{shopId}/users/invitations", new { email = invitee, role = SystemRoles.ShopStaff }, ct))
        {
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
        var entries = await db.Set<AuditEntry>().Where(e => e.ShopId == shopId).OrderBy(e => e.OccurredAt).ToListAsync(ct);

        entries.Select(e => e.Action).ShouldBe(["shop.created", "shop.activated", "shop.suspended", "shop_user.invited"]);
        entries.ShouldAllBe(e => e.ActorType == "PlatformAdmin" && e.ActorUserId != null && e.CorrelationId != null);
        entries.Single(e => e.Action == "shop.suspended").Summary.ShouldBe("Active → Suspended");
        entries.Single(e => e.Action == "shop.suspended").Reason.ShouldBe("Complaint review");
        entries.ShouldAllBe(e => !(e.Summary ?? string.Empty).Contains('@') && !(e.Reason ?? string.Empty).Contains('@'));
    }

    [Fact]
    public async Task ShopMembership_IsEnforcedByTheDatabase_AndCannotChange()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "shop_membership", ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shops = await ShopTestData.CreateTwoShopsAsync(factory, admin, ct);

        // A user pointing at a shop that does not exist is rejected by the foreign key.
        await Should.ThrowAsync<DbUpdateException>(() =>
            ShopTestData.CreateShopUserAsync(factory, Guid.NewGuid(), IdentityTestData.NewEmail("ghost"), SystemRoles.ShopOwner, ct));

        // Moving a shop user to another shop is refused.
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var owner = await users.FindByEmailAsync(shops.A.OwnerEmail);
        owner!.ShopId = new ShopId(shops.B.ShopId);
        await Should.ThrowAsync<TenantViolationException>(() => users.UpdateAsync(owner));
    }

    private static async Task<(Guid? Tenant, int VisibleShops)> ProbeAsync(ApiSession session, CancellationToken ct)
    {
        using var response = await session.GetAsync(ShopTestData.TenantProbePath, ct);
        var json = await response.JsonAsync(ct);
        var tenant = json.GetProperty("tenant");
        return (tenant.ValueKind == System.Text.Json.JsonValueKind.Null ? null : tenant.GetGuid(), json.GetProperty("visibleShops").GetInt32());
    }

    [GeneratedRegex(@"https://app\.trimme\.test/\S+")]
    private static partial Regex LinkPattern();
}
