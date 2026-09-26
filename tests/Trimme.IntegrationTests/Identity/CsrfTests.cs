using System.Net;
using Shouldly;
using Trimme.BuildingBlocks.Web.Security;
using Trimme.IntegrationTests.Infrastructure;

namespace Trimme.IntegrationTests.Identity;

/// <summary>Double-submit CSRF protection for the cookie-authenticated API (R-AUTH-08).</summary>
public sealed class CsrfTests(PostgresFixture postgres)
{
    [Fact]
    public async Task UnsafeRequest_WithoutCsrf_Rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "csrf", ct);
        using var session = await IdentityTestData.SignInCustomerAsync(factory, IdentityTestData.NewPhone(), ct);

        // Signed in, but no header: rejected before the handler runs, and the session survives.
        using (var missing = await session.SendAsync(HttpMethod.Post, "/api/v1/auth/sessions/revoke-all", null, ct, withCsrf: false))
        {
            missing.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await missing.ErrorCodeAsync(ct)).ShouldBe(Csrf.InvalidErrorCode);
        }

        // A header that does not match the cookie is rejected too.
        using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/sign-out"))
        {
            request.Headers.Add(Csrf.HeaderName, "forged-value");
            using var forged = await session.Client.SendAsync(request, ct);
            forged.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using (var me = await session.GetAsync("/api/v1/me", ct))
        {
            me.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // Anonymous unsafe endpoints (sign-in) are protected as well.
        using var anonymous = ApiSession.Create(factory);
        using var otp = await anonymous.SendAsync(
            HttpMethod.Post, "/api/v1/auth/otp/request", new { phone = IdentityTestData.NewPhone(), termsAccepted = true }, ct, withCsrf: false);
        otp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CsrfCheck_DoesNotMaskRoutingErrors_OrSafeMethods()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "csrf_routing", ct);
        using var session = ApiSession.Create(factory);

        using var unknown = await session.SendAsync(HttpMethod.Post, "/api/v1/does-not-exist", new { }, ct, withCsrf: false);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var wrongMethod = await session.SendAsync(HttpMethod.Post, "/api/v1/meta", new { }, ct, withCsrf: false);
        wrongMethod.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);

        using var safe = await session.GetAsync("/api/v1/meta", ct);
        safe.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CsrfToken_RotatesOnSignIn()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "csrf_rotate", ct);
        using var session = ApiSession.Create(factory);
        var before = await session.CsrfTokenAsync(ct);
        var phone = IdentityTestData.NewPhone();
        var challengeId = await IdentityTestData.RequestOtpAsync(session, phone, ct);

        using var verify = await session.PostAsync("/api/v1/auth/otp/verify", new { challengeId, code = IdentityTestData.LatestOtp(factory, phone) }, ct);

        verify.StatusCode.ShouldBe(HttpStatusCode.OK);
        session.Cookie(Csrf.CookieName).ShouldNotBe(before);
    }

    [Fact]
    public async Task Cors_Preflight_AllowsCsrfHeader_WithCredentials_ForKnownOriginOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "csrf_cors", ct);
        using var client = factory.CreateClient();

        using var allowed = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/otp/request");
        allowed.Headers.Add("Origin", TrimmeApiFactory.AllowedTestOrigin);
        allowed.Headers.Add("Access-Control-Request-Method", "POST");
        allowed.Headers.Add("Access-Control-Request-Headers", "content-type,x-csrf-token");
        using var allowedResponse = await client.SendAsync(allowed, ct);
        allowedResponse.Headers.GetValues("Access-Control-Allow-Origin").Single().ShouldBe(TrimmeApiFactory.AllowedTestOrigin);
        allowedResponse.Headers.GetValues("Access-Control-Allow-Credentials").Single().ShouldBe("true");
        string.Join(',', allowedResponse.Headers.GetValues("Access-Control-Allow-Headers")).ToLowerInvariant().ShouldContain("x-csrf-token");

        using var foreign = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/otp/request");
        foreign.Headers.Add("Origin", "https://evil.example");
        foreign.Headers.Add("Access-Control-Request-Method", "POST");
        foreign.Headers.Add("Access-Control-Request-Headers", "content-type,x-csrf-token");
        using var foreignResponse = await client.SendAsync(foreign, ct);
        foreignResponse.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }
}
