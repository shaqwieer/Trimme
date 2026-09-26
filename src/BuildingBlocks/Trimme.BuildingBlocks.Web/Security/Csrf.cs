using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Trimme.BuildingBlocks.Web.Errors;

namespace Trimme.BuildingBlocks.Web.Security;

/// <summary>
/// Double-submit CSRF protection for the cookie-authenticated API (spec §9, D-027). The API issues a random token in a
/// readable cookie; the web app echoes it in the <see cref="HeaderName"/> header, which a cross-site page can neither
/// read nor set.
/// </summary>
public static class Csrf
{
    public const string CookieName = "trimme-csrf";
    public const string HeaderName = "X-CSRF-Token";
    public const string InvalidErrorCode = "auth.csrf_invalid";

    /// <summary>Creates a fresh token, sets the readable cookie and returns the token.</summary>
    public static string Issue(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        response.Cookies.Append(CookieName, token, CookieOptions());
        return token;
    }

    public static void Clear(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Cookies.Delete(CookieName, CookieOptions());
    }

    internal static bool IsValid(HttpRequest request)
    {
        var cookie = request.Cookies[CookieName];
        var header = request.Headers[HeaderName].ToString();
        if (string.IsNullOrEmpty(cookie) || string.IsNullOrEmpty(header))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(cookie), Encoding.UTF8.GetBytes(header));
    }

    /// <summary>Protects every unsafe endpoint of a route group (applied to <c>/api/v1</c>).</summary>
    public static TBuilder RequireCsrf<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
        => builder.WithMetadata(new CsrfProtectionMetadata(true));

    /// <summary>Opts one endpoint out (for example a signature-verified provider webhook). Use sparingly.</summary>
    public static TBuilder SkipCsrf<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
        => builder.WithMetadata(new CsrfProtectionMetadata(false));

    private static CookieOptions CookieOptions() => new()
    {
        HttpOnly = false,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        IsEssential = true,
    };

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>The most specific (last) instance decides whether an endpoint validates CSRF.</summary>
public sealed record CsrfProtectionMetadata(bool Enabled);

/// <summary>
/// Runs after routing and authentication, so only matched, protected endpoints are checked: unknown routes still
/// return 404 and unsupported methods 405. Safe methods are never checked.
/// </summary>
public sealed class CsrfProtectionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var method = context.Request.Method;
        var unsafeMethod = !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method)
                             || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method));

        if (unsafeMethod
            && context.GetEndpoint()?.Metadata.GetMetadata<CsrfProtectionMetadata>() is { Enabled: true }
            && !Csrf.IsValid(context.Request))
        {
            await context.WriteProblemAsync(
                StatusCodes.Status403Forbidden,
                Csrf.InvalidErrorCode,
                "The CSRF token is missing or invalid.");
            return;
        }

        await next(context);
    }
}
