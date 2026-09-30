using Microsoft.AspNetCore.Http;

namespace Trimme.BuildingBlocks.Web.Security;

/// <summary>
/// Baseline security headers for a JSON API. The API serves HTML to browsers only for the development-only API
/// reference UI (no CSP) and the admin-only jobs dashboard (a same-origin CSP that allows its own inline scripts and
/// styles); everything else gets the strict CSP.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    internal const string StrictContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";

    internal const string DashboardContentSecurityPolicy =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; "
        + "frame-ancestors 'none'; base-uri 'none'; form-action 'self'";

    private static readonly PathString[] RelaxedCspPaths = ["/scalar"];

    private static readonly PathString DashboardPath = "/api/ops/jobs";

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

            if (context.Request.Path.StartsWithSegments(DashboardPath, StringComparison.OrdinalIgnoreCase))
            {
                headers.ContentSecurityPolicy = DashboardContentSecurityPolicy;
            }
            else if (!RelaxedCspPaths.Any(p => context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
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
