using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Trimme.Api.Hosting;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.Modules.Identity.Domain;
using Trimme.Modules.Identity.Infrastructure.Persistence;
using Trimme.Modules.Identity.Infrastructure.Seeding;

namespace Trimme.IntegrationTests.Identity;

/// <summary>Staff email + password sign-in, lockout, password reset and invitations (R-AUTH-02, -03, -06, -07).</summary>
public sealed partial class StaffAuthTests(PostgresFixture postgres, MailpitFixture mailpit)
{
    [Fact]
    public async Task Staff_SignIn_ReturnsPermissions()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "staff_sign_in", ct);
        var email = IdentityTestData.NewEmail("ops");
        await IdentityTestData.CreateStaffAsync(factory, email, SystemRoles.OperationsManager, ct);
        using var session = ApiSession.Create(factory);

        using var response = await session.PostAsync(
            "/api/v1/auth/staff/sign-in", new { email = email.ToUpperInvariant(), password = IdentityTestData.StaffPassword }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var user = (await response.JsonAsync(ct)).GetProperty("user");
        user.GetProperty("userType").GetString().ShouldBe("PlatformAdmin");
        user.GetProperty("email").GetString().ShouldBe(email);
        var permissions = user.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ToArray();
        permissions.ShouldContain(Permissions.Admin.ShopsView);
        permissions.ShouldNotContain(Permissions.SuperAdmin.SubscriptionPlansManage);
        permissions.ShouldNotContain(p => p!.StartsWith("Shop.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Lockout_AfterFailedAttempts()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "lockout", ct);
        var email = IdentityTestData.NewEmail();
        await IdentityTestData.CreateStaffAsync(factory, email, SystemRoles.Support, ct);
        using var session = ApiSession.Create(factory);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            using var wrong = await session.PostAsync("/api/v1/auth/staff/sign-in", new { email, password = "wrong password!" }, ct);
            wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await wrong.ErrorCodeAsync(ct)).ShouldBe("auth.invalid_credentials");
        }

        using (var fifth = await session.PostAsync("/api/v1/auth/staff/sign-in", new { email, password = "wrong password!" }, ct))
        {
            fifth.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            var problem = await fifth.JsonAsync(ct);
            problem.GetProperty("errorCode").GetString().ShouldBe("auth.locked_out");
            problem.GetProperty("retryAfterSeconds").GetInt32().ShouldBeGreaterThan(14 * 60);
        }

        // The right password does not bypass the lockout.
        using var correct = await session.PostAsync("/api/v1/auth/staff/sign-in", new { email, password = IdentityTestData.StaffPassword }, ct);
        correct.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        session.Cookie("trimme-access").ShouldBeNull();
    }

    [Fact]
    public async Task Staff_SignIn_UnknownEmailAndWrongPassword_AreIndistinguishable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "staff_enum", ct);
        var email = IdentityTestData.NewEmail();
        await IdentityTestData.CreateStaffAsync(factory, email, SystemRoles.Support, ct);
        using var session = ApiSession.Create(factory);

        using var unknown = await session.PostAsync("/api/v1/auth/staff/sign-in", new { email = IdentityTestData.NewEmail(), password = "whatever pass" }, ct);
        using var wrong = await session.PostAsync("/api/v1/auth/staff/sign-in", new { email, password = "whatever pass" }, ct);

        unknown.StatusCode.ShouldBe(wrong.StatusCode);
        (await unknown.ErrorCodeAsync(ct)).ShouldBe(await wrong.ErrorCodeAsync(ct));
    }

    [Fact]
    public async Task Staff_PasswordReset_Flow()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "reset", ct, settings: mailpit.SmtpSettings);
        var email = IdentityTestData.NewEmail("reset");
        await IdentityTestData.CreateStaffAsync(factory, email, SystemRoles.SuperAdmin, ct);
        using var existingDevice = await IdentityTestData.SignInStaffAsync(factory, email, ct);
        using var browser = ApiSession.Create(factory);

        using (var forgot = await browser.PostAsync("/api/v1/auth/password/forgot", new { email, locale = "en" }, ct))
        {
            forgot.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        var message = await mailpit.WaitForMessageAsync(email, ct);
        message.Subject.ShouldBe("إعادة تعيين كلمة المرور في تريمي"); // the account's saved language (ar) wins
        var link = LinkPattern().Match(message.Text);
        link.Success.ShouldBeTrue(message.Text);
        link.Value.ShouldStartWith("https://app.trimme.test/ar/auth/reset-password?uid=");
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(link.Value).Query);

        const string newPassword = "a brand new passphrase";
        using (var reset = await browser.PostAsync(
                   "/api/v1/auth/password/reset", new { userId = Guid.Parse(query["uid"]!), token = query["token"], newPassword }, ct))
        {
            reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        // Every existing session ends; the old password stops working, the new one works; the link is single-use.
        using (var oldDevice = await existingDevice.GetAsync("/api/v1/me", ct))
        {
            oldDevice.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using (var oldPassword = await browser.PostAsync("/api/v1/auth/staff/sign-in", new { email, password = IdentityTestData.StaffPassword }, ct))
        {
            oldPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using var fresh = await IdentityTestData.SignInStaffAsync(factory, email, ct, newPassword);
        using var reused = await browser.PostAsync(
            "/api/v1/auth/password/reset", new { userId = Guid.Parse(query["uid"]!), token = query["token"], newPassword = "yet another passphrase" }, ct);
        (await reused.ErrorCodeAsync(ct)).ShouldBe("auth.reset_invalid");
    }

    [Fact]
    public async Task ForgotPassword_UnknownEmailOrCustomer_Returns202_AndSendsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "forgot_unknown", ct, settings: mailpit.SmtpSettings);
        var unknown = IdentityTestData.NewEmail("nobody");
        using var browser = ApiSession.Create(factory);

        using var response = await browser.PostAsync("/api/v1/auth/password/forgot", new { email = unknown, locale = "ar" }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await Task.Delay(500, ct);
        (await mailpit.CountMessagesAsync(unknown, ct)).ShouldBe(0);
    }

    [Fact]
    public async Task ForgotPassword_KnownEmail_Returns202_EvenWhenEmailDeliveryFails()
    {
        var ct = TestContext.Current.CancellationToken;
        var unreachableSmtp = new Dictionary<string, string?>
        {
            ["Email:Smtp:Host"] = "127.0.0.1",
            ["Email:Smtp:Port"] = "1",
            ["Email:Smtp:Security"] = "None",
        };
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "forgot_smtp_down", ct, settings: unreachableSmtp);
        var known = IdentityTestData.NewEmail("known");
        await IdentityTestData.CreateStaffAsync(factory, known, SystemRoles.Support, ct);
        using var browser = ApiSession.Create(factory);

        using var knownResponse = await browser.PostAsync("/api/v1/auth/password/forgot", new { email = known, locale = "ar" }, ct);
        using var unknownResponse = await browser.PostAsync(
            "/api/v1/auth/password/forgot", new { email = IdentityTestData.NewEmail("unknown"), locale = "ar" }, ct);

        knownResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        unknownResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task ResetPassword_RejectsWeakPassword_WithFieldError()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "reset_weak", ct);
        var email = IdentityTestData.NewEmail();
        var userId = await IdentityTestData.CreateStaffAsync(factory, email, SystemRoles.Support, ct);
        string token;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            token = await users.GeneratePasswordResetTokenAsync((await users.FindByIdAsync(userId.ToString()))!);
        }

        using var browser = ApiSession.Create(factory);
        using var response = await browser.PostAsync("/api/v1/auth/password/reset", new { userId, token, newPassword = "short" }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.JsonAsync(ct)).GetProperty("errors").GetProperty("password")[0].GetString().ShouldBe("validation.password_too_short");
    }

    [Fact]
    public async Task Admin_InvitesStaffUser_WhoAcceptsAndSignsIn()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "invite", ct, settings: mailpit.SmtpSettings);
        using var superAdmin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var invitee = IdentityTestData.NewEmail("invitee");

        using (var invite = await superAdmin.PostAsync(
                   "/api/v1/admin/staff/invitations", new { email = invitee, role = SystemRoles.OperationsManager, locale = "en" }, ct))
        {
            invite.StatusCode.ShouldBe(HttpStatusCode.Created);
            (await invite.JsonAsync(ct)).GetProperty("role").GetString().ShouldBe(SystemRoles.OperationsManager);
        }

        var message = await mailpit.WaitForMessageAsync(invitee, ct);
        message.Subject.ShouldBe("You're invited to TRIMME");
        message.Html.ShouldContain("dir=\"ltr\"");
        var link = LinkPattern().Match(message.Text);
        link.Value.ShouldStartWith("https://app.trimme.test/en/auth/accept-invite?token=");
        var token = System.Web.HttpUtility.ParseQueryString(new Uri(link.Value).Query)["token"];

        using var browser = ApiSession.Create(factory);
        using (var accept = await browser.PostAsync(
                   "/api/v1/auth/invitations/accept", new { token, displayName = "Huda", password = "operations passphrase" }, ct))
        {
            accept.StatusCode.ShouldBe(HttpStatusCode.OK);
            var user = (await accept.JsonAsync(ct)).GetProperty("user");
            user.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ShouldBe([SystemRoles.OperationsManager]);
            user.GetProperty("email").GetString().ShouldBe(invitee);
        }

        using (var me = await browser.GetAsync("/api/v1/me", ct))
        {
            me.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var reuse = await ApiSession.Create(factory).PostAsync(
                   "/api/v1/auth/invitations/accept", new { token, displayName = "Mallory", password = "another passphrase" }, ct))
        {
            (await reuse.ErrorCodeAsync(ct)).ShouldBe("invitation.invalid");
        }

        using var duplicate = await superAdmin.PostAsync(
            "/api/v1/admin/staff/invitations", new { email = invitee, role = SystemRoles.Support, locale = "en" }, ct);
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Invitation_CannotGrantARoleOfAnotherUserType()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "invite_role", ct);
        using var superAdmin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);

        foreach (var role in new[] { SystemRoles.ShopOwner, SystemRoles.Customer, "Root" })
        {
            using var invite = await superAdmin.PostAsync(
                "/api/v1/admin/staff/invitations", new { email = IdentityTestData.NewEmail(), role, locale = "ar" }, ct);
            invite.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await invite.JsonAsync(ct)).GetProperty("errors").GetProperty("role")[0].GetString().ShouldBe("invitation.role_invalid");
        }
    }

    [Fact]
    public async Task ShopAccount_CannotSelfRegister()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "no_self_register", ct);

        // The only self-service sign-up creates a customer, which holds no shop or admin permission.
        using var customer = await IdentityTestData.SignInCustomerAsync(factory, IdentityTestData.NewPhone(), ct);
        using var me = await customer.GetAsync("/api/v1/me", ct);
        var user = await me.JsonAsync(ct);
        user.GetProperty("userType").GetString().ShouldBe("Customer");
        user.GetProperty("permissions").GetArrayLength().ShouldBe(0);

        // Staff accounts exist only through an admin invitation: a guessed token creates nothing.
        using var guessed = await ApiSession.Create(factory).PostAsync(
            "/api/v1/auth/invitations/accept", new { token = "guessed-token", displayName = "Shop", password = "some passphrase" }, ct);
        (await guessed.ErrorCodeAsync(ct)).ShouldBe("invitation.invalid");

        // And the contract has no staff registration endpoint.
        using var client = factory.CreateClient();
        using var openApi = System.Text.Json.JsonDocument.Parse(
            await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), ct));
        var paths = openApi.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToArray();
        paths.ShouldNotContain(p => p.Contains("register", StringComparison.OrdinalIgnoreCase) || p.Contains("sign-up", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AdminBootstrap_RequiresDevelopmentAndEnv()
    {
        var ct = TestContext.Current.CancellationToken;

        // The seed command refuses to run outside Development or without the explicit flag and argument.
        DevSeedGuard.Evaluate("Production", "true", ["seed", "--dev"]).Allowed.ShouldBeFalse();
        DevSeedGuard.Evaluate("Development", null, ["seed", "--dev"]).Allowed.ShouldBeFalse();
        DevSeedGuard.Evaluate("Development", "true", ["seed", "--dev"]).Allowed.ShouldBeTrue();

        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "bootstrap", ct);
        var seeder = new BootstrapAdminSeeder();

        // Without the variables nothing is created.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await seeder.SeedAsync(scope.ServiceProvider, ct);
            (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().GetUsersInRoleAsync(SystemRoles.SuperAdmin)).ShouldBeEmpty();
        }

        var email = IdentityTestData.NewEmail("bootstrap");
        var configured = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                [BootstrapAdminSeeder.EmailVariable] = email,
                [BootstrapAdminSeeder.PasswordVariable] = IdentityTestData.StaffPassword,
            }).Build());

        for (var run = 0; run < 2; run++)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            await seeder.SeedAsync(new OverridingServiceProvider(scope.ServiceProvider, configured.BuildServiceProvider()), ct);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().GetUsersInRoleAsync(SystemRoles.SuperAdmin)).Count.ShouldBe(1);
        }

        using var admin = await IdentityTestData.SignInStaffAsync(factory, email, ct);
        using var me = await admin.GetAsync("/api/v1/me", ct);
        (await me.JsonAsync(ct)).GetProperty("permissions").EnumerateArray().Select(p => p.GetString())
            .ShouldContain(Permissions.SuperAdmin.SubscriptionPlansManage);
    }

    [GeneratedRegex(@"https://app\.trimme\.test/\S+")]
    private static partial Regex LinkPattern();

    /// <summary>Resolves <see cref="IConfiguration"/> from an override container and everything else from the API.</summary>
    private sealed class OverridingServiceProvider(IServiceProvider inner, IServiceProvider overrides) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IConfiguration) ? overrides.GetService(serviceType) : inner.GetService(serviceType);
    }
}
