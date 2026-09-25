using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using Serilog;
using Serilog.Formatting.Compact;
using Trimme.BuildingBlocks.Application;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Errors;
using Trimme.BuildingBlocks.Web.Modules;
using Trimme.BuildingBlocks.Web.Observability;
using Trimme.BuildingBlocks.Web.Security;

namespace Trimme.Api.Hosting;

internal static class ApiServices
{
    public const string ReadyHealthTag = "ready";

    /// <summary>Registers everything the API needs. Must not open connections or perform I/O (EF tooling builds the host).</summary>
    public static WebApplicationBuilder AddTrimmeApi(this WebApplicationBuilder builder, IReadOnlyList<IModule> modules)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        services.AddSerilog((serviceProvider, logger) => ConfigureLogging(logger, configuration, builder.Environment));

        var requestLimits = configuration.GetSection(RequestLimitsOptions.SectionName).Get<RequestLimitsOptions>() ?? new RequestLimitsOptions();
        services.Configure<RequestLimitsOptions>(configuration.GetSection(RequestLimitsOptions.SectionName));
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Limits.MaxRequestBodySize = requestLimits.MaxBodyBytes;
        });

        services.ConfigureHttpJsonOptions(json =>
            json.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        services.AddTrimmeProblemDetails();
        services.AddTrimmeCors(configuration);
        services.AddTrimmeRateLimiting(configuration);
        services.Configure<ForwardedHeadersOptions>(options => ConfigureForwardedHeaders(options, configuration));

        services.AddTrimmeApplication();
        services.AddTrimmePersistence();
        foreach (var module in modules)
        {
            services.AddSingleton<IModelContributor>(module);
            module.AddServices(services, configuration);
        }

        services.AddHealthChecks()
            .AddDbContextCheck<TrimmeDbContext>("database", tags: [ReadyHealthTag]);

        services.AddOpenApi("v1", options => options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info.Title = "TRIMME API";
            document.Info.Version = "v1";
            document.Info.Description = "Public, customer, shop and admin API for the TRIMME salon and barber booking marketplace.";
            document.Servers?.Clear();
            return Task.CompletedTask;
        }));

        return builder;
    }

    private static void ConfigureLogging(LoggerConfiguration logger, IConfiguration configuration, IWebHostEnvironment environment)
    {
        logger
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .Enrich.With<RedactionEnricher>()
            .Enrich.WithProperty("Application", "Trimme.Api");

        if (environment.IsDevelopment())
        {
            logger.WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {Message:lj}{NewLine}{Exception}",
                formatProvider: System.Globalization.CultureInfo.InvariantCulture);
        }
        else
        {
            logger.WriteTo.Console(new RenderedCompactJsonFormatter());
        }
    }

    /// <summary>Trusts X-Forwarded-* only from explicitly configured proxies (Nginx), so rate limits see real client IPs.</summary>
    private static void ConfigureForwardedHeaders(ForwardedHeadersOptions options, ConfigurationManager configuration)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        foreach (var proxy in configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
        {
            if (System.Net.IPAddress.TryParse(proxy, out var address))
            {
                options.KnownProxies.Add(address);
            }
        }
    }
}
