using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Shouldly;
using Trimme.BuildingBlocks.Web.Errors;
using Trimme.BuildingBlocks.Web.Security;

namespace Trimme.IntegrationTests.Api;

/// <summary>Default 1 MB body ceiling with explicit per-endpoint opt-in for uploads (used from Phase 06).</summary>
public sealed class RequestSizeLimitTests
{
    private const int TwoMegabytes = 2 * 1024 * 1024;

    [Fact]
    public async Task Default_endpoint_rejects_a_2MB_body()
    {
        await using var app = await StartHostAsync();
        using var client = app.GetTestClient();

        using var response = await client.PostAsync(new Uri("/default", UriKind.Relative), Body(TwoMegabytes), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task Endpoint_with_explicit_larger_limit_accepts_a_2MB_body()
    {
        await using var app = await StartHostAsync();
        using var client = app.GetTestClient();

        using var response = await client.PostAsync(new Uri("/upload", UriKind.Relative), Body(TwoMegabytes), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe(TwoMegabytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Endpoint_limit_is_still_enforced()
    {
        await using var app = await StartHostAsync();
        using var client = app.GetTestClient();

        using var response = await client.PostAsync(new Uri("/upload", UriKind.Relative), Body(5 * 1024 * 1024), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
    }

    private static ByteArrayContent Body(int size) => new(new byte[size]);

    private static async Task<WebApplication> StartHostAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddTrimmeProblemDetails();

        var app = builder.Build();
        app.UseRouting();
        app.UseMiddleware<RequestSizeLimitMiddleware>();
        app.MapPost("/default", () => Results.Ok());
        app.MapPost("/upload", async (HttpRequest request, CancellationToken ct) =>
            {
                using var buffer = new MemoryStream();
                await request.Body.CopyToAsync(buffer, ct);
                return Results.Text(buffer.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            })
            .WithMetadata(new RequestSizeLimitAttribute(3 * 1024 * 1024));
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }
}
