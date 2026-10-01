using System.Net;
using System.Text.Json;
using Shouldly;
using Trimme.BuildingBlocks.Web.Errors;
using Trimme.BuildingBlocks.Web.Security;
using Trimme.IntegrationTests.Identity;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.Modules.Identity.Domain;

namespace Trimme.IntegrationTests.Api;

/// <summary>
/// Spec §18 (Phase 17): every named policy throttles its real endpoints, on the real API, for the right caller. Each case
/// lowers only its own policy to two requests per window: the third gets 429 with <c>Retry-After</c> and the
/// <c>rate_limited</c> problem, whatever the request's own outcome would have been (the limiter runs before the endpoint).
/// Policies for signed-in users are partitioned per user, so a second user is not affected by the first.
/// </summary>
public sealed class RateLimitPolicyTests(PostgresFixture postgres)
{
    private const int Limit = 2;

    public enum Caller
    {
        Anonymous,
        Customer,
        Admin,
    }

    public static TheoryData<string, Caller, string, string> Cases => new()
    {
        { RateLimitPolicies.Auth, Caller.Anonymous, "POST", "/api/v1/auth/staff/sign-in" },
        { RateLimitPolicies.Otp, Caller.Anonymous, "POST", "/api/v1/auth/otp/request" },
        { RateLimitPolicies.Search, Caller.Anonymous, "GET", "/api/v1/public/shops/search?lat=24.7&lng=46.6" },
        { RateLimitPolicies.Availability, Caller.Anonymous, "GET", "/api/v1/public/shops/no-such-shop/status" },
        { RateLimitPolicies.Qr, Caller.Anonymous, "GET", "/api/v1/public/qr/ABCDEFGH" },
        { RateLimitPolicies.Booking, Caller.Customer, "POST", "/api/v1/bookings" },
        { RateLimitPolicies.Review, Caller.Customer, "POST", "/api/v1/me/bookings/01990000-0000-7000-8000-000000000001/review" },
        { RateLimitPolicies.Favorites, Caller.Customer, "PUT", "/api/v1/me/favorites/shops/01990000-0000-7000-8000-000000000002" },
        { RateLimitPolicies.Geocode, Caller.Admin, "GET", "/api/v1/admin/geo/search?q=olaya" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task EachPolicy_ThrottlesItsEndpoints_WithA429Problem(string policy, Caller caller, string method, string path)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, $"rl_{policy}", ct, settings: new Dictionary<string, string?>
        {
            [$"{RateLimitingSetup.SectionName}:{policy}:{nameof(RateLimitPolicyOptions.PermitLimit)}"] = Limit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [$"{RateLimitingSetup.SectionName}:{policy}:{nameof(RateLimitPolicyOptions.WindowSeconds)}"] = "300",
        });

        using var first = await SessionAsync(factory, caller, ct);
        for (var i = 0; i < Limit; i++)
        {
            using var allowed = await SendAsync(first, method, path, ct);
            allowed.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests, $"request {i + 1} of {Limit} is within the {policy} limit");
        }

        using var rejected = await SendAsync(first, method, path, ct);
        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests, $"{method} {path} is throttled by {policy}");
        int.Parse(rejected.Headers.GetValues("Retry-After").Single(), System.Globalization.CultureInfo.InvariantCulture).ShouldBeInRange(1, 300);
        using (var problem = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync(ct)))
        {
            problem.RootElement.GetProperty(ApiErrorCodes.ErrorCodeExtension).GetString().ShouldBe(ApiErrorCodes.RateLimited);
        }

        if (caller != Caller.Anonymous)
        {
            using var second = await SessionAsync(factory, caller, ct);
            using var other = await SendAsync(second, method, path, ct);
            other.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests, "signed-in callers are limited per user");
        }
    }

    private static async Task<ApiSession> SessionAsync(TrimmeApiFactory factory, Caller caller, CancellationToken ct) => caller switch
    {
        Caller.Customer => await IdentityTestData.SignInCustomerAsync(factory, IdentityTestData.NewPhone(), ct),
        Caller.Admin => await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct),
        _ => ApiSession.Create(factory),
    };

    private static Task<HttpResponseMessage> SendAsync(ApiSession session, string method, string path, CancellationToken ct)
    {
        object? body = method switch
        {
            "POST" when path.EndsWith("/otp/request", StringComparison.Ordinal) => new { phone = IdentityTestData.NewPhone(), termsAccepted = true, locale = "ar" },
            "POST" when path.EndsWith("/staff/sign-in", StringComparison.Ordinal) => new { email = "nobody@trimme.test", password = "wrong password" },
            "POST" or "PUT" => new { },
            _ => null,
        };
        return session.SendAsync(new HttpMethod(method), path, body, ct);
    }
}
