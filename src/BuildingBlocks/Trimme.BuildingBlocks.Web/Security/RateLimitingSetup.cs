using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trimme.BuildingBlocks.Web.Errors;

namespace Trimme.BuildingBlocks.Web.Security;

/// <summary>Named rate-limit policies (spec §18). Endpoints opt in with <c>.RequireRateLimiting(RateLimitPolicies.X)</c>.</summary>
public static class RateLimitPolicies
{
    public const string Auth = "auth";
    public const string Otp = "otp";
    public const string Search = "search";
    public const string Availability = "availability";
    public const string Booking = "booking";
    public const string Review = "review";
    public const string Qr = "qr";

    public static readonly IReadOnlyDictionary<string, RateLimitPolicyOptions> Defaults =
        new Dictionary<string, RateLimitPolicyOptions>(StringComparer.Ordinal)
        {
            [Auth] = new() { PermitLimit = 10, WindowSeconds = 60 },
            [Otp] = new() { PermitLimit = 5, WindowSeconds = 600 },
            [Search] = new() { PermitLimit = 60, WindowSeconds = 60 },
            [Availability] = new() { PermitLimit = 60, WindowSeconds = 60 },
            [Booking] = new() { PermitLimit = 10, WindowSeconds = 60 },
            [Review] = new() { PermitLimit = 5, WindowSeconds = 60 },
            [Qr] = new() { PermitLimit = 30, WindowSeconds = 60 },
        };
}

public sealed class RateLimitPolicyOptions
{
    public int PermitLimit { get; set; }

    public int WindowSeconds { get; set; }
}

public static class RateLimitingSetup
{
    public const string SectionName = "RateLimiting";

    /// <summary>
    /// Registers every named policy as a fixed window partitioned by client IP address
    /// (authenticated-user partitioning is added with Identity in Phase 04). Limits are configurable
    /// per policy under <c>RateLimiting:{policy}</c>; rejected requests get a 429 problem response.
    /// </summary>
    public static IServiceCollection AddTrimmeRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                await context.HttpContext.WriteProblemAsync(
                    StatusCodes.Status429TooManyRequests,
                    ApiErrorCodes.RateLimited,
                    "Too many requests. Please try again later.");
            };

            foreach (var (name, defaults) in RateLimitPolicies.Defaults)
            {
                var configured = configuration.GetSection($"{SectionName}:{name}").Get<RateLimitPolicyOptions>();
                var permitLimit = configured?.PermitLimit > 0 ? configured.PermitLimit : defaults.PermitLimit;
                var windowSeconds = configured?.WindowSeconds > 0 ? configured.WindowSeconds : defaults.WindowSeconds;

                options.AddPolicy(name, httpContext => RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: $"{name}:{httpContext.Connection.RemoteIpAddress}",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = TimeSpan.FromSeconds(windowSeconds),
                        QueueLimit = 0,
                    }));
            }
        });

        return services;
    }
}
