using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Trimme.BuildingBlocks.Application.Validation;
using Trimme.BuildingBlocks.Web.Errors;
using Trimme.BuildingBlocks.Web.Observability;
using Trimme.IntegrationTests.Infrastructure;

namespace Trimme.IntegrationTests.Api;

/// <summary>Problem details, correlation IDs, security headers, CORS and body limits (R-FND-03/06/14/15).</summary>
public sealed class HttpConventionTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string ThrowValidationPath = "/__test/throw-validation";
    private const string ThrowUnexpectedPath = "/__test/throw-unexpected";

    private TrimmeApiFactory _factory = null!;

    public async ValueTask InitializeAsync()
    {
        var connectionString = await postgres.CreateDatabaseAsync("http", TestContext.Current.CancellationToken);
        _factory = new TrimmeApiFactory(connectionString)
        {
            // Test-only middleware appended after the real pipeline: it only sees requests no endpoint handled.
            ConfigureTestServices = services => services.AddSingleton<IStartupFilter, ThrowingStartupFilter>(),
        };
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task UnknownRoute_Returns_ProblemDetails()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/does-not-exist", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        using var problem = await ReadJsonAsync(response);
        problem.RootElement.GetProperty("status").GetInt32().ShouldBe(404);
        problem.RootElement.GetProperty(ApiErrorCodes.ErrorCodeExtension).GetString().ShouldBe(ApiErrorCodes.NotFound);
        problem.RootElement.GetProperty(ApiErrorCodes.CorrelationIdExtension).GetString()
            .ShouldBe(response.Headers.GetValues(CorrelationId.HeaderName).Single());
    }

    [Fact]
    public async Task ValidationError_HasStableCode_AndFieldErrors()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri(ThrowValidationPath, UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var problem = await ReadJsonAsync(response);
        problem.RootElement.GetProperty(ApiErrorCodes.ErrorCodeExtension).GetString().ShouldBe(ApiErrorCodes.ValidationFailed);
        problem.RootElement.GetProperty("errors").GetProperty("name")[0].GetString().ShouldBe("name.required");
    }

    [Fact]
    public async Task UnexpectedError_DoesNotLeakExceptionDetails()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri(ThrowUnexpectedPath, UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain(ApiErrorCodes.Unexpected);
        body.ShouldNotContain("SecretInternalDetail");
        body.ShouldNotContain("StackTrace", Case.Insensitive);
    }

    [Fact]
    public async Task Response_HasCorrelationId_GeneratedWhenMissing()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        response.Headers.GetValues(CorrelationId.HeaderName).Single().Length.ShouldBeGreaterThanOrEqualTo(8);
    }

    [Theory]
    [InlineData("web-2f6c1a9e8b7d", true)]
    [InlineData("bad id with spaces", false)]
    [InlineData("short", false)]
    public async Task Response_EchoesOnlyWellFormedCorrelationIds(string incoming, bool echoed)
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation(CorrelationId.HeaderName, incoming);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        var returned = response.Headers.GetValues(CorrelationId.HeaderName).Single();
        (returned == incoming).ShouldBe(echoed);
    }

    [Fact]
    public async Task SecurityHeaders_Present()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/meta", UriKind.Relative), TestContext.Current.CancellationToken);

        response.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
        response.Headers.GetValues("X-Frame-Options").Single().ShouldBe("DENY");
        response.Headers.GetValues("Referrer-Policy").Single().ShouldBe("strict-origin-when-cross-origin");
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("default-src 'none'");
        response.Headers.Contains("Server").ShouldBeFalse();
    }

    [Fact]
    public async Task Cors_AllowsConfiguredOrigin_WithCredentials()
    {
        using var response = await SendPreflightAsync(TrimmeApiFactory.AllowedTestOrigin);

        response.Headers.GetValues("Access-Control-Allow-Origin").Single().ShouldBe(TrimmeApiFactory.AllowedTestOrigin);
        response.Headers.GetValues("Access-Control-Allow-Credentials").Single().ShouldBe("true");
    }

    [Fact]
    public async Task Cors_RejectsUnknownOrigin()
    {
        using var response = await SendPreflightAsync("https://evil.example");

        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    [Fact]
    public async Task RequestBody_TooLarge_Returns413Problem()
    {
        using var client = _factory.CreateClient();
        using var content = new ByteArrayContent(new byte[1_048_577]);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        using var response = await client.PostAsync(new Uri("/api/v1/meta", UriKind.Relative), content, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        using var problem = await ReadJsonAsync(response);
        problem.RootElement.GetProperty(ApiErrorCodes.ErrorCodeExtension).GetString().ShouldBe(ApiErrorCodes.PayloadTooLarge);
    }

    [Fact]
    public async Task MethodNotAllowed_Returns_ProblemDetails()
    {
        using var client = _factory.CreateClient();
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(new Uri("/api/v1/meta", UriKind.Relative), content, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        using var problem = await ReadJsonAsync(response);
        problem.RootElement.GetProperty(ApiErrorCodes.ErrorCodeExtension).GetString().ShouldBe(ApiErrorCodes.MethodNotAllowed);
    }

    private async Task<HttpResponseMessage> SendPreflightAsync(string origin)
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/meta");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

    private sealed class ThrowingStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Use((context, nextMiddleware) =>
            {
                if (context.Request.Path == ThrowValidationPath)
                {
                    throw new RequestValidationException(new Dictionary<string, string[]> { ["name"] = ["name.required"] });
                }

                if (context.Request.Path == ThrowUnexpectedPath)
                {
                    throw new InvalidOperationException("SecretInternalDetail");
                }

                return nextMiddleware(context);
            });
        };
    }
}
