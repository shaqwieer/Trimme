using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trimme.BuildingBlocks.Web.Observability;

namespace Trimme.BuildingBlocks.Web.Security;

public static class CorsSetup
{
    public const string PolicyName = "trimme-web";
    public const string AllowedOriginsKey = "Cors:AllowedOrigins";

    /// <summary>
    /// Strict CORS (spec §9): only explicitly configured origins, with credentials (cookie auth).
    /// No wildcard is ever allowed. In production the web app and API share one origin via Nginx,
    /// so the list is normally empty there.
    /// </summary>
    public static IServiceCollection AddTrimmeCors(this IServiceCollection services, IConfiguration configuration)
    {
        var origins = (configuration.GetSection(AllowedOriginsKey).Get<string[]>() ?? [])
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Select(o => o.TrimEnd('/'))
            .ToArray();

        if (origins.Any(o => o.Contains('*', StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Wildcard CORS origins are not allowed.");
        }

        services.AddCors(options => options.AddPolicy(PolicyName, policy =>
        {
            policy.WithOrigins(origins)
                .AllowCredentials()
                .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
                .WithHeaders("Content-Type", "Accept", "Accept-Language", "Idempotency-Key", "X-CSRF-Token", CorrelationId.HeaderName)
                .WithExposedHeaders(CorrelationId.HeaderName, "ETag", "Retry-After")
                .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
        }));

        return services;
    }
}
