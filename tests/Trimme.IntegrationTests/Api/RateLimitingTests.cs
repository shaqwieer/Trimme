using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Trimme.BuildingBlocks.Web.Errors;
using Trimme.BuildingBlocks.Web.Security;

namespace Trimme.IntegrationTests.Api;

/// <summary>
/// Exercises the named rate-limit policies on a minimal host (no feature endpoint uses them yet in Phase 01).
/// </summary>
public sealed class RateLimitingTests
{
    [Fact]
    public async Task Policy_rejects_requests_over_the_limit_with_429_problem()
    {
        await using var app = await StartHostAsync(new Dictionary<string, string?>
        {
            ["RateLimiting:auth:PermitLimit"] = "2",
            ["RateLimiting:auth:WindowSeconds"] = "60",
        });
        using var client = app.GetTestClient();
        var ct = TestContext.Current.CancellationToken;

        (await client.GetAsync(new Uri("/limited", UriKind.Relative), ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync(new Uri("/limited", UriKind.Relative), ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var rejected = await client.GetAsync(new Uri("/limited", UriKind.Relative), ct);

        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        using var problem = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync(ct));
        problem.RootElement.GetProperty(ApiErrorCodes.ErrorCodeExtension).GetString().ShouldBe(ApiErrorCodes.RateLimited);
    }

    [Fact]
    public void Every_named_policy_has_positive_defaults()
    {
        RateLimitPolicies.Defaults.Keys.ShouldBe(
            [RateLimitPolicies.Auth, RateLimitPolicies.Otp, RateLimitPolicies.Search, RateLimitPolicies.Availability,
             RateLimitPolicies.Booking, RateLimitPolicies.Review, RateLimitPolicies.Qr],
            ignoreOrder: true);
        RateLimitPolicies.Defaults.Values.ShouldAllBe(p => p.PermitLimit > 0 && p.WindowSeconds > 0);
    }

    private static async Task<WebApplication> StartHostAsync(Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddTrimmeProblemDetails();
        builder.Services.AddTrimmeRateLimiting(builder.Configuration);

        var app = builder.Build();
        app.UseTrimmeProblemDetails();
        app.UseRateLimiter();
        app.MapGet("/limited", () => Results.Ok()).RequireRateLimiting(RateLimitPolicies.Auth);
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }
}
