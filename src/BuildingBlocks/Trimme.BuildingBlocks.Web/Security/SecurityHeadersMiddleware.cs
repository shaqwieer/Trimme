using Microsoft.AspNetCore.Http;
using Trimme.BuildingBlocks.Web.Caching;

namespace Trimme.BuildingBlocks.Web.Security;

/// <summary>
/// Baseline security headers for a JSON API. The API serves HTML to browsers only for the development-only API
/// reference UI (no CSP) and the admin-only jobs dashboard (a same-origin CSP that allows its own inline scripts and
/// styles); everything else gets the strict CSP.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public const string StrictContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";

    public const string DashboardContentSecurityPolicy =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; "
        + "frame-ancestors 'none'; base-uri 'none'; form-action 'self'";

    private static readonly PathString[] RelaxedCspPaths = ["/scalar"];

    private static readonly PathString DashboardPath = "/api/ops/jobs";

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Decide on the path as it arrives: branches such as the jobs dashboard (`Map`) move their prefix into PathBase
        // before the response starts, so the path seen in OnStarting would no longer match (D-117).
        var path = context.Request.Path;
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "geolocation=(), camera=(), microphone=(), payment=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Cross-Origin-Resource-Policy"] = "same-site";

            // API responses are per-user by default: never cached by browsers or proxies unless an endpoint says so, or
            // it is an anonymous public read (Phase 17, D-121).
            if (!headers.ContainsKey("Cache-Control"))
            {
                headers.CacheControl = PublicCache.MayBeKeptByHttpCaches(context) ? PublicCache.HttpCacheControl : "no-store";
            }

            if (path.StartsWithSegments(DashboardPath, StringComparison.OrdinalIgnoreCase))
            {
                headers.ContentSecurityPolicy = DashboardContentSecurityPolicy;
            }
            else if (!RelaxedCspPaths.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
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
