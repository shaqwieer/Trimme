using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.IntegrationTests.Identity;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.Modules.Administration.Domain;
using Trimme.Modules.Identity.Domain;
using Trimme.Modules.Identity.Infrastructure.Persistence;

namespace Trimme.IntegrationTests.Tenancy;

/// <summary>
/// D-139: an admin creates a shop owner or staff account with a temporary password, and the user replaces it before
/// the first session starts.
/// </summary>
public sealed class ShopUserTemporaryPasswordTests(PostgresFixture postgres)
{
    private const string TemporaryPassword = "temporary pass 2026";
    private const string NewPassword = "my own passphrase";

    [Fact]
    public async Task Admin_CreatesShopUser_WhoReplacesTheTemporaryPassword_BeforeTheFirstSession()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "shop_user_temp", ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shopId = await ShopTestData.CreateShopAsync(admin, "temp-pass", ct);
        var email = IdentityTestData.NewEmail("owner");

        Guid userId;
        using (var created = await admin.PostAsync(
                   $"/api/v1/admin/shops/{shopId}/users",
                   new { email, displayName = "مالك المحل", role = SystemRoles.ShopOwner, password = TemporaryPassword, locale = "ar" },
                   ct))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(ct));
            var body = await created.JsonAsync(ct);
            userId = body.GetProperty("id").GetGuid();
            body.GetProperty("email").GetString().ShouldBe(email);
            body.GetProperty("displayName").GetString().ShouldBe("مالك المحل");
            body.GetProperty("role").GetString().ShouldBe(SystemRoles.ShopOwner);
            body.TryGetProperty("password", out _).ShouldBeFalse();
        }

        // Audited against the shop, without the email address or the password.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var entry = await scope.ServiceProvider.GetRequiredService<TrimmeDbContext>().Set<AuditEntry>().AsNoTracking()
                .SingleAsync(e => e.EntityId == userId.ToString(), ct);
            entry.Action.ShouldBe("shop_user.created");
            entry.ShopId.ShouldBe(shopId);
            entry.Summary.ShouldNotBeNull().ShouldNotContain(email);
            entry.Summary.ShouldNotContain(TemporaryPassword);
        }

        using var browser = ApiSession.Create(factory);

        // The right temporary password starts no session; a wrong one answers as usual.
        using (var signIn = await browser.PostAsync("/api/v1/auth/staff/sign-in", new { email, password = TemporaryPassword }, ct))
        {
            signIn.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await signIn.ErrorCodeAsync(ct)).ShouldBe("auth.password_change_required");
        }

        browser.Cookie("trimme-access").ShouldBeNull();
        browser.Cookie("trimme-refresh", "/api/v1/auth").ShouldBeNull();
        using (var wrong = await browser.PostAsync("/api/v1/auth/staff/sign-in", new { email, password = "not the password" }, ct))
        {
            (await wrong.ErrorCodeAsync(ct)).ShouldBe("auth.invalid_credentials");
        }

        // The change proves the temporary password and refuses to keep it.
        using (var wrong = await browser.PostAsync(
                   "/api/v1/auth/password/change-temporary", new { email, password = "not the password", newPassword = NewPassword }, ct))
        {
            wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await wrong.ErrorCodeAsync(ct)).ShouldBe("auth.invalid_credentials");
        }

        using (var same = await browser.PostAsync(
                   "/api/v1/auth/password/change-temporary", new { email, password = TemporaryPassword, newPassword = TemporaryPassword }, ct))
        {
            same.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            FieldErrors(await same.JsonAsync(ct), "password").ShouldBe(["validation.password_unchanged"]);
        }

        using (var weak = await browser.PostAsync(
                   "/api/v1/auth/password/change-temporary", new { email, password = TemporaryPassword, newPassword = "short" }, ct))
        {
            weak.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            FieldErrors(await weak.JsonAsync(ct), "password").ShouldContain("validation.password_too_short");
        }

        browser.Cookie("trimme-access").ShouldBeNull();

        using (var changed = await browser.PostAsync(
                   "/api/v1/auth/password/change-temporary", new { email, password = TemporaryPassword, newPassword = NewPassword }, ct))
        {
            changed.StatusCode.ShouldBe(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync(ct));
            var user = (await changed.JsonAsync(ct)).GetProperty("user");
            user.GetProperty("id").GetGuid().ShouldBe(userId);
            user.GetProperty("userType").GetString().ShouldBe("ShopUser");
            user.GetProperty("shopId").GetGuid().ShouldBe(shopId);
            user.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ShouldContain(Permissions.Shop.ServicesManage);
        }

        browser.Cookie("trimme-access").ShouldNotBeNull();
        using (var myShop = await browser.GetAsync("/api/v1/shop/me", ct))
        {
            myShop.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await myShop.JsonAsync(ct)).GetProperty("id").GetGuid().ShouldBe(shopId);
        }

        // Done once: the endpoint does not change an ordinary password, and the temporary one is gone.
        using (var again = await browser.PostAsync(
                   "/api/v1/auth/password/change-temporary", new { email, password = NewPassword, newPassword = "yet another passphrase" }, ct))
        {
            again.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await again.ErrorCodeAsync(ct)).ShouldBe("auth.password_change_not_required");
        }

        using var later = ApiSession.Create(factory);
        using (var old = await later.PostAsync("/api/v1/auth/staff/sign-in", new { email, password = TemporaryPassword }, ct))
        {
            old.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using var signedIn = await IdentityTestData.SignInStaffAsync(factory, email, ct, NewPassword);
    }

    [Fact]
    public async Task CreateShopUser_NeedsAnExistingShop_AShopRole_AFreeEmail_AndThePermission()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "shop_user_create_rules", ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shopId = await ShopTestData.CreateShopAsync(admin, "create-rules", ct, activate: false);

        object Body(string email, string role, string password = TemporaryPassword) =>
            new { email, displayName = "Desk", role, password, locale = "en" };

        using (var unknownShop = await admin.PostAsync(
                   $"/api/v1/admin/shops/{Guid.NewGuid()}/users", Body(IdentityTestData.NewEmail(), SystemRoles.ShopOwner), ct))
        {
            unknownShop.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await unknownShop.ErrorCodeAsync(ct)).ShouldBe("shop.not_found");
        }

        foreach (var role in new[] { SystemRoles.SuperAdmin, SystemRoles.Customer })
        {
            using var wrongRole = await admin.PostAsync($"/api/v1/admin/shops/{shopId}/users", Body(IdentityTestData.NewEmail(), role), ct);
            wrongRole.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            FieldErrors(await wrongRole.JsonAsync(ct), "role").ShouldBe(["invitation.role_invalid"]);
        }

        using (var weak = await admin.PostAsync(
                   $"/api/v1/admin/shops/{shopId}/users", Body(IdentityTestData.NewEmail(), SystemRoles.ShopStaff, "short"), ct))
        {
            weak.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            FieldErrors(await weak.JsonAsync(ct), "password").ShouldContain("validation.password_too_short");
        }

        // An invitation already emailed to the address is revoked by the manual account.
        var email = IdentityTestData.NewEmail("desk");
        using (var invite = await admin.PostAsync(
                   $"/api/v1/admin/shops/{shopId}/users/invitations", new { email, role = SystemRoles.ShopStaff, locale = "en" }, ct))
        {
            invite.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        using (var created = await admin.PostAsync($"/api/v1/admin/shops/{shopId}/users", Body(email, SystemRoles.ShopStaff), ct))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
            (await db.Set<Invitation>().AsNoTracking().Where(i => i.Email == email).Select(i => i.RevokedAt).SingleAsync(ct)).ShouldNotBeNull();
        }

        using (var duplicate = await admin.PostAsync($"/api/v1/admin/shops/{shopId}/users", Body(email.ToUpperInvariant(), SystemRoles.ShopOwner), ct))
        {
            duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await duplicate.ErrorCodeAsync(ct)).ShouldBe("invitation.email_registered");
        }

        // Operations managers have Admin.Shops.ManageAccount; Support does not.
        using var support = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.Support, ct);
        using var forbidden = await support.PostAsync($"/api/v1/admin/shops/{shopId}/users", Body(IdentityTestData.NewEmail(), SystemRoles.ShopStaff), ct);
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task EmailedReset_ReplacesTheTemporaryPassword_Too()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "shop_user_temp_reset", ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shopId = await ShopTestData.CreateShopAsync(admin, "temp-reset", ct);
        var email = IdentityTestData.NewEmail("staff");
        using (var created = await admin.PostAsync(
                   $"/api/v1/admin/shops/{shopId}/users",
                   new { email, displayName = "Desk", role = SystemRoles.ShopStaff, password = TemporaryPassword },
                   ct))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        // The link the forgot-password email carries.
        string token;
        Guid userId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync(email)).ShouldNotBeNull();
            user.MustChangePassword.ShouldBeTrue();
            userId = user.Id;
            token = await users.GeneratePasswordResetTokenAsync(user);
        }

        using var browser = ApiSession.Create(factory);
        using (var reset = await browser.PostAsync("/api/v1/auth/password/reset", new { userId, token, newPassword = NewPassword }, ct))
        {
            reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using var signedIn = await IdentityTestData.SignInStaffAsync(factory, email, ct, NewPassword);
    }

    private static string?[] FieldErrors(JsonElement problem, string field) =>
        [.. problem.GetProperty("errors").GetProperty(field).EnumerateArray().Select(e => e.GetString())];
}
