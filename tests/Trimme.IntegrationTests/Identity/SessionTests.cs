using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.Modules.Identity.Domain;

namespace Trimme.IntegrationTests.Identity;

/// <summary>Cookie sessions with rotating refresh tokens (D-027): R-AUTH-04 and R-AUTH-05.</summary>
public sealed class SessionTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Cookies_AreSecureHttpOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "cookies", ct);
        var phone = IdentityTestData.NewPhone();
        using var session = ApiSession.Create(factory);
        var challengeId = await IdentityTestData.RequestOtpAsync(session, phone, ct);

        using var response = await session.PostAsync(
            "/api/v1/auth/otp/verify", new { challengeId, code = IdentityTestData.LatestOtp(factory, phone) }, ct);

        var setCookies = response.Headers.GetValues("Set-Cookie").ToArray();
        var access = setCookies.Single(c => c.StartsWith("trimme-access=", StringComparison.Ordinal)).ToLowerInvariant();
        access.ShouldContain("httponly");
        access.ShouldContain("secure");
        access.ShouldContain("samesite=lax");
        access.ShouldContain("path=/;");

        var refresh = setCookies.Single(c => c.StartsWith("trimme-refresh=", StringComparison.Ordinal)).ToLowerInvariant();
        refresh.ShouldContain("httponly");
        refresh.ShouldContain("secure");
        refresh.ShouldContain("samesite=strict");
        refresh.ShouldContain("path=/api/v1/auth");

        var csrf = setCookies.Single(c => c.StartsWith("trimme-csrf=", StringComparison.Ordinal)).ToLowerInvariant();
        csrf.ShouldContain("secure");
        csrf.ShouldNotContain("httponly");

        // The signed-in hint (D-144) carries only the user type, for the web app's redirects; it is no token.
        var hint = setCookies.Single(c => c.StartsWith("trimme-signed-in=", StringComparison.Ordinal)).ToLowerInvariant();
        hint.ShouldStartWith("trimme-signed-in=customer;");
        hint.ShouldContain("secure");
        hint.ShouldContain("samesite=lax");
        hint.ShouldContain("path=/;");
        hint.ShouldNotContain("httponly");

        // Tokens travel only in cookies, never in a body the page could store.
        var body = await response.Content.ReadAsStringAsync(ct);
        body.ShouldNotContain(session.Cookie("trimme-refresh", "/api/v1/auth")!);
        body.ShouldNotContain(session.Cookie("trimme-access")!);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task CustomerAndShopSessions_LastAYear_PlatformAdminSessionsAWeek()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "lifetime", ct);
        var phone = IdentityTestData.NewPhone();
        using var session = ApiSession.Create(factory);
        var challengeId = await IdentityTestData.RequestOtpAsync(session, phone, ct);

        // A customer signs in with a paid code, so the session and its refresh cookie last a year (D-142).
        using var response = await session.PostAsync(
            "/api/v1/auth/otp/verify", new { challengeId, code = IdentityTestData.LatestOtp(factory, phone) }, ct);
        var refresh = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("trimme-refresh=", StringComparison.Ordinal));
        var expires = refresh.Split(';').Select(part => part.Trim()).Single(part => part.StartsWith("expires=", StringComparison.OrdinalIgnoreCase));
        var cookieExpires = DateTimeOffset.Parse(expires["expires=".Length..], CultureInfo.InvariantCulture);
        (cookieExpires - DateTimeOffset.UtcNow).TotalDays.ShouldBe(365, tolerance: 0.01);

        // Shop owners and desk staff a year too; the platform's admins, who reach every shop, a week.
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shops = await Tenancy.ShopTestData.CreateTwoShopsAsync(factory, admin, ct);
        using var owner = await IdentityTestData.SignInStaffAsync(factory, shops.A.OwnerEmail, ct);
        using var desk = await IdentityTestData.SignInStaffAsync(factory, shops.A.StaffEmail, ct);
        await using var scope = factory.Services.CreateAsyncScope();
        var sessions = await scope.ServiceProvider.GetRequiredService<TrimmeDbContext>().Set<UserSession>().AsNoTracking().ToListAsync(ct);
        var customer = sessions.Single(s => s.UserType == UserType.Customer);
        (customer.ExpiresAt - customer.CreatedAt).ShouldBe(TimeSpan.FromDays(365));
        sessions.Count(s => s.UserType == UserType.ShopUser).ShouldBe(2);
        sessions.Where(s => s.UserType == UserType.ShopUser).ShouldAllBe(s => s.ExpiresAt - s.CreatedAt == TimeSpan.FromDays(365));
        sessions.Count(s => s.UserType == UserType.PlatformAdmin).ShouldBeGreaterThan(0);
        sessions.Where(s => s.UserType == UserType.PlatformAdmin).ShouldAllBe(s => s.ExpiresAt - s.CreatedAt == TimeSpan.FromDays(7));
    }

    [Fact]
    public async Task Refresh_Rotates()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "refresh_rotates", ct, clock);
        using var session = await IdentityTestData.SignInCustomerAsync(factory, IdentityTestData.NewPhone(), ct);
        var firstRefresh = session.Cookie("trimme-refresh", "/api/v1/auth");
        var firstAccess = session.Cookie("trimme-access");

        // The access cookie lives 15 minutes; after that the API answers 401 until the client refreshes.
        clock.Advance(TimeSpan.FromMinutes(16));
        using (var expired = await session.GetAsync("/api/v1/me", ct))
        {
            expired.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await expired.ErrorCodeAsync(ct)).ShouldBe("auth.unauthenticated");
        }

        using (var refreshed = await session.PostAsync("/api/v1/auth/refresh", body: null, ct))
        {
            refreshed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        session.Cookie("trimme-refresh", "/api/v1/auth").ShouldNotBe(firstRefresh);
        session.Cookie("trimme-access").ShouldNotBe(firstAccess);
        using var me = await session.GetAsync("/api/v1/me", ct);
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RefreshReuse_RevokesFamily()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "refresh_reuse", ct, clock);
        using var victim = await IdentityTestData.SignInCustomerAsync(factory, IdentityTestData.NewPhone(), ct);
        var stolenRefresh = victim.Cookie("trimme-refresh", "/api/v1/auth")!;

        using (var rotated = await victim.PostAsync("/api/v1/auth/refresh", body: null, ct))
        {
            rotated.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        // A replay right after the rotation looks like a second tab racing: 409, nothing revoked.
        using var attacker = ApiSession.Create(factory);
        attacker.Cookies.Add(new Uri(ApiSession.Origin, "/api/v1/auth"), new Cookie("trimme-refresh", stolenRefresh, "/api/v1/auth"));
        using (var race = await attacker.PostAsync("/api/v1/auth/refresh", body: null, ct))
        {
            race.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await race.ErrorCodeAsync(ct)).ShouldBe("auth.refresh_race");
        }

        using (var stillIn = await victim.GetAsync("/api/v1/me", ct))
        {
            stillIn.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // After the grace window the replay is treated as theft: the whole family is revoked.
        clock.Advance(TimeSpan.FromSeconds(11));
        using (var reuse = await attacker.PostAsync("/api/v1/auth/refresh", body: null, ct))
        {
            reuse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await reuse.ErrorCodeAsync(ct)).ShouldBe("auth.refresh_invalid");
        }

        using (var victimMe = await victim.GetAsync("/api/v1/me", ct))
        {
            victimMe.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using var victimRefresh = await victim.PostAsync("/api/v1/auth/refresh", body: null, ct);
        victimRefresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RevokeAll_InvalidatesOtherSessions()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "revoke_all", ct, clock);
        var phone = IdentityTestData.NewPhone();
        using var phoneSession = await IdentityTestData.SignInCustomerAsync(factory, phone, ct);
        clock.Advance(TimeSpan.FromSeconds(31));
        using var laptopSession = await IdentityTestData.SignInCustomerAsync(factory, phone, ct);

        using (var list = await laptopSession.GetAsync("/api/v1/auth/sessions", ct))
        {
            var sessions = await list.JsonAsync(ct);
            sessions.GetArrayLength().ShouldBe(2);
            sessions.EnumerateArray().Count(s => s.GetProperty("isCurrent").GetBoolean()).ShouldBe(1);
        }

        using (var revoke = await laptopSession.PostAsync("/api/v1/auth/sessions/revoke-all", body: null, ct))
        {
            revoke.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await revoke.JsonAsync(ct)).GetProperty("revoked").GetInt32().ShouldBe(1);
        }

        // The other device's still-unexpired access cookie stops working immediately, and it cannot refresh.
        using (var otherMe = await phoneSession.GetAsync("/api/v1/me", ct))
        {
            otherMe.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using (var otherRefresh = await phoneSession.PostAsync("/api/v1/auth/refresh", body: null, ct))
        {
            otherRefresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using var currentMe = await laptopSession.GetAsync("/api/v1/me", ct);
        currentMe.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SignOut_EndsTheSession_AndClearsCookies()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "sign_out", ct);
        using var session = await IdentityTestData.SignInCustomerAsync(factory, IdentityTestData.NewPhone(), ct);
        var refresh = session.Cookie("trimme-refresh", "/api/v1/auth")!;

        using (var signOut = await session.PostAsync("/api/v1/auth/sign-out", body: null, ct))
        {
            signOut.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        session.Cookie("trimme-access").ShouldBeNull();
        session.Cookie("trimme-refresh", "/api/v1/auth").ShouldBeNull();
        session.Cookie("trimme-signed-in").ShouldBeNull();
        using (var me = await session.GetAsync("/api/v1/me", ct))
        {
            me.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        // The refresh token of the ended session is dead too, and its failed refresh deletes a stale hint (D-144).
        using var replay = ApiSession.Create(factory);
        replay.Cookies.Add(new Uri(ApiSession.Origin, "/api/v1/auth"), new Cookie("trimme-refresh", refresh, "/api/v1/auth"));
        replay.Cookies.Add(ApiSession.Origin, new Cookie("trimme-signed-in", "Customer", "/") { Secure = true });
        using (var refreshed = await replay.PostAsync("/api/v1/auth/refresh", body: null, ct))
        {
            refreshed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        replay.Cookie("trimme-signed-in").ShouldBeNull();

        // A browser with the hint but no refresh cookie at all loses the hint on its first refresh too.
        using var hintOnly = ApiSession.Create(factory);
        hintOnly.Cookies.Add(ApiSession.Origin, new Cookie("trimme-signed-in", "ShopUser", "/") { Secure = true });
        using (var refreshed = await hintOnly.PostAsync("/api/v1/auth/refresh", body: null, ct))
        {
            refreshed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        hintOnly.Cookie("trimme-signed-in").ShouldBeNull();
    }

    [Fact]
    public async Task RevokeSession_OfAnotherUser_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "revoke_foreign", ct);
        using var alice = await IdentityTestData.SignInCustomerAsync(factory, IdentityTestData.NewPhone(), ct);
        using var bob = await IdentityTestData.SignInCustomerAsync(factory, IdentityTestData.NewPhone(), ct);

        using var bobSessions = await bob.GetAsync("/api/v1/auth/sessions", ct);
        var bobSessionId = (await bobSessions.JsonAsync(ct))[0].GetProperty("id").GetGuid();

        using var attempt = await alice.DeleteAsync($"/api/v1/auth/sessions/{bobSessionId}", ct);
        attempt.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var bobMe = await bob.GetAsync("/api/v1/me", ct);
        bobMe.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
