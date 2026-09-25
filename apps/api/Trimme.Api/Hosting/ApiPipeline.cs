using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using Serilog;
using Trimme.BuildingBlocks.Web.Errors;
using Trimme.BuildingBlocks.Web.Modules;
using Trimme.BuildingBlocks.Web.Observability;
using Trimme.BuildingBlocks.Web.Security;

namespace Trimme.Api.Hosting;

internal static class ApiPipeline
{
    public const string ApiPrefix = "/api/v1";
    public const string TestingEnvironment = "Testing";

    public static WebApplication UseTrimmeApi(this WebApplication app, IReadOnlyList<IModule> modules)
    {
        app.UseForwardedHeaders();
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseTrimmeProblemDetails();
        app.UseSerilogRequestLogging();
        app.UseMiddleware<RequestSizeLimitMiddleware>();

        if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment(TestingEnvironment))
        {
            app.UseHsts();
        }

        app.UseCors(CorsSetup.PolicyName);
        app.UseRateLimiter();

        MapPlatformEndpoints(app);

        var api = app.MapGroup(ApiPrefix);
        MetaEndpoints.Map(api);
        foreach (var module in modules)
        {
            module.MapEndpoints(api);
        }

        return app;
    }

    private static void MapPlatformEndpoints(WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteHealthResponse,
        });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ApiServices.ReadyHealthTag),
            ResponseWriter = WriteHealthResponse,
        });

        if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment(TestingEnvironment))
        {
            app.MapOpenApi("/openapi/{documentName}.json");
        }

        if (app.Environment.IsDevelopment())
        {
            app.MapScalarApiReference("/scalar", options => options
                .WithTitle("TRIMME API")
                .WithOpenApiRoutePattern("/openapi/{documentName}.json"));
        }
    }

    /// <summary>Minimal health payload: status per check, no exception messages or connection details.</summary>
    private static Task WriteHealthResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString() }),
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(payload), context.RequestAborted);
    }
}
