using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.IntegrationTests.Identity;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.Modules.Administration.Domain;
using Trimme.Modules.Identity.Domain;

namespace Trimme.IntegrationTests.Tenancy;

/// <summary>
/// D-158 (the client's request): the admin's shop page shows the shop's owner and gives them a new temporary password,
/// shown once; the owner's sessions end and the next sign-in asks for a new password (D-139). Only an owner of that shop
/// can be reset.
/// </summary>
public sealed class ShopOwnerPasswordResetTests(PostgresFixture postgres)
{
    private const string FirstPassword = "temporary pass 2026";
    private const string OwnPassword = "my own passphrase";
    private const string NextPassword = "a brand new passphrase";

    [Fact]
    public async Task Admin_SeesTheOwner_AndGivesANewTemporaryPassword_ThatEndsTheirSessions()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "shop_owner_reset", ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shopId = await ShopTestData.CreateShopAsync(admin, "owner-reset", ct);
        var otherShopId = await ShopTestData.CreateShopAsync(admin, "owner-reset-other", ct);
        var email = IdentityTestData.NewEmail("owner");
        var userId = await CreateOwnerAsync(admin, shopId, email, ct);

        // The owner replaces the first temporary password and is signed in.
        using (var browser = ApiSession.Create(factory))
        using (var changed = await browser.PostAsync(
                   "/api/v1/auth/password/change-temporary", new { email, password = FirstPassword, newPassword = OwnPassword }, ct))
        {
            changed.StatusCode.ShouldBe(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync(ct));
        }

        using var owner = await IdentityTestData.SignInStaffAsync(factory, email, ct, OwnPassword);

        using (var list = await admin.GetAsync($"/api/v1/admin/shops/{shopId}/users", ct))
        {
            list.StatusCode.ShouldBe(HttpStatusCode.OK);
            var row = (await list.JsonAsync(ct)).EnumerateArray().ShouldHaveSingleItem();
            row.GetProperty("userId").GetGuid().ShouldBe(userId);
            row.GetProperty("displayName").GetString().ShouldBe("مالك المحل");
            row.GetProperty("email").GetString().ShouldBe(email);
            row.GetProperty("isDisabled").GetBoolean().ShouldBeFalse();
            row.GetProperty("mustChangePassword").GetBoolean().ShouldBeFalse();
        }

        // Another shop's page lists no one and cannot reach this owner.
        using (var otherList = await admin.GetAsync($"/api/v1/admin/shops/{otherShopId}/users", ct))
        {
            (await otherList.JsonAsync(ct)).GetArrayLength().ShouldBe(0);
        }

        using (var wrongShop = await admin.PostAsync($"/api/v1/admin/shops/{otherShopId}/users/{userId}/temporary-password", new { }, ct))
        {
            wrongShop.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await wrongShop.ErrorCodeAsync(ct)).ShouldBe("auth.user_not_found");
        }

        string temporary;
        using (var reset = await admin.PostAsync($"/api/v1/admin/shops/{shopId}/users/{userId}/temporary-password", new { }, ct))
        {
            reset.StatusCode.ShouldBe(HttpStatusCode.OK, await reset.Content.ReadAsStringAsync(ct));
            var body = await reset.JsonAsync(ct);
            temporary = body.GetProperty("temporaryPassword").GetString().ShouldNotBeNull();
            temporary.Length.ShouldBe(12);
            body.GetProperty("account").GetProperty("mustChangePassword").GetBoolean().ShouldBeTrue();
        }

        // The owner's session ended at once.
        using (var me = await owner.GetAsync("/api/v1/me", ct))
        {
            me.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using (var list = await admin.GetAsync($"/api/v1/admin/shops/{shopId}/users", ct))
        {
            (await list.JsonAsync(ct))[0].GetProperty("mustChangePassword").GetBoolean().ShouldBeTrue();
        }

        // Audited against the shop, without the password or the email address.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var entry = await scope.ServiceProvider.GetRequiredService<TrimmeDbContext>().Set<AuditEntry>().AsNoTracking()
                .SingleAsync(e => e.EntityId == userId.ToString() && e.Action == "shop_user.password_reset", ct);
            entry.ShopId.ShouldBe(shopId);
            entry.Summary.ShouldNotBeNull().ShouldNotContain(temporary);
            entry.Summary.ShouldNotContain(email);
        }

        using var later = ApiSession.Create(factory);
        using (var old = await later.PostAsync("/api/v1/auth/staff/sign-in", new { email, password = OwnPassword }, ct))
        {
            old.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        // The temporary password opens no session until the owner replaces it (D-139).
        using (var signIn = await later.PostAsync("/api/v1/auth/staff/sign-in", new { email, password = temporary }, ct))
        {
            signIn.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await signIn.ErrorCodeAsync(ct)).ShouldBe("auth.password_change_required");
        }

        using (var changed = await later.PostAsync(
                   "/api/v1/auth/password/change-temporary", new { email, password = temporary, newPassword = NextPassword }, ct))
        {
            changed.StatusCode.ShouldBe(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync(ct));
        }

        using var again = await IdentityTestData.SignInStaffAsync(factory, email, ct, NextPassword);
    }

    [Fact]
    public async Task Reset_ReachesOnlyAnOwnerOfThatShop_AndNeedsThePermission()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "shop_owner_reset_rules", ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shopId = await ShopTestData.CreateShopAsync(admin, "owner-reset-rules", ct);
        var ownerId = await CreateOwnerAsync(admin, shopId, IdentityTestData.NewEmail("owner"), ct);

        Guid adminId;
        using (var me = await admin.GetAsync("/api/v1/me", ct))
        {
            adminId = (await me.JsonAsync(ct)).GetProperty("id").GetGuid();
        }

        // An admin (or any account that is not this shop's owner) is never reachable through a shop.
        foreach (var target in new[] { adminId, Guid.NewGuid() })
        {
            using var refused = await admin.PostAsync($"/api/v1/admin/shops/{shopId}/users/{target}/temporary-password", new { }, ct);
            refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await refused.ErrorCodeAsync(ct)).ShouldBe("auth.user_not_found");
        }

        using (var unknownShop = await admin.GetAsync($"/api/v1/admin/shops/{Guid.NewGuid()}/users", ct))
        {
            unknownShop.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await unknownShop.ErrorCodeAsync(ct)).ShouldBe("shop.not_found");
        }

        // Support has no Admin.Shops.ManageAccount: neither the list nor the reset.
        using var support = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.Support, ct);
        using (var list = await support.GetAsync($"/api/v1/admin/shops/{shopId}/users", ct))
        {
            list.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using (var reset = await support.PostAsync($"/api/v1/admin/shops/{shopId}/users/{ownerId}/temporary-password", new { }, ct))
        {
            reset.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
    }

    private static async Task<Guid> CreateOwnerAsync(ApiSession admin, Guid shopId, string email, CancellationToken ct)
    {
        using var created = await admin.PostAsync(
            $"/api/v1/admin/shops/{shopId}/users",
            new { email, displayName = "مالك المحل", role = SystemRoles.ShopOwner, password = FirstPassword, locale = "ar" },
            ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(ct));
        return (await created.JsonAsync(ct)).GetProperty("id").GetGuid();
    }
}
