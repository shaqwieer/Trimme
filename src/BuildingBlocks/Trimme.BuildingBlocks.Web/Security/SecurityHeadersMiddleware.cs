using Microsoft.AspNetCore.Http;

namespace Trimme.BuildingBlocks.Web.Security;

/// <summary>
/// Baseline security headers for a JSON API. The API never serves HTML to browsers except the
/// development-only API reference UI, which is exempted from the strict CSP.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    internal const string StrictContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";

    private static readonly PathString[] RelaxedCspPaths = ["/scalar"];

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "geolocation=(), camera=(), microphone=(), payment=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Cross-Origin-Resource-Policy"] = "same-site";

            // API responses are per-user by default: never cached by browsers or proxies unless an endpoint says so.
            if (!headers.ContainsKey("Cache-Control"))
            {
                headers.CacheControl = "no-store";
            }

            if (!RelaxedCspPaths.Any(p => context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
            {
                headers.ContentSecurityPolicy = StrictContentSecurityPolicy;
            }

            headers.Remove("Server");
            headers.Remove("X-Powered-By");
            return Task.CompletedTask;
        });

        return next(context);
    }
}
