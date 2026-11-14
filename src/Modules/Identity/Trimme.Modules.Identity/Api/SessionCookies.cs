using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Trimme.BuildingBlocks.Web.Security;
using Trimme.Modules.Identity.Application;

namespace Trimme.Modules.Identity.Api;

/// <summary>
/// Writes the session cookies (D-027): a short-lived HttpOnly access cookie for every path, and an HttpOnly refresh
/// cookie scoped to <c>/api/v1/auth</c> so it is only ever sent to the refresh and sign-out endpoints. Both are
/// <c>Secure</c>. Tokens never appear in a response body, so nothing can end up in web storage (spec §9).
/// A third cookie, <see cref="SignedInCookie"/>, holds no token: only the user type, so the web app can keep a
/// signed-in user away from the landing and sign-in pages until they sign out (D-144). It lives as long as the session
/// and is deleted wherever the session cookies are.
/// </summary>
internal static class SessionCookies
{
    public const string AccessCookie = "trimme-access";
    public const string RefreshCookie = "trimme-refresh";
    public const string RefreshCookiePath = "/api/v1/auth";

    /// <summary>
    /// "Someone is signed in here, as this user type" (D-144). Not a credential and not HttpOnly: the web app reads it
    /// in its proxy and in the browser (the Back button restores pages without asking the server). It grants nothing;
    /// every page and endpoint still checks the access cookie.
    /// </summary>
    public const string SignedInCookie = "trimme-signed-in";

    /// <summary>
    /// Issues the access and refresh cookies for <paramref name="session"/>. <paramref name="rotateCsrf"/> is true on
    /// sign-in (a new session gets a new CSRF token, defeating a token planted before sign-in) and false on a refresh,
    /// so a request the web client retries after refreshing still carries a valid header.
    /// </summary>
    public static async Task SignInAsync(HttpContext http, IssuedSession session, bool rotateCsrf = true)
    {
        var options = http.RequestServices.GetRequiredService<IOptions<SessionOptions>>().Value;
        var clock = http.RequestServices.GetRequiredService<TimeProvider>();

        List<Claim> claims =
        [
            new(TrimmeClaims.Subject, session.UserId.ToString()),
            new(TrimmeClaims.SessionId, session.SessionId.ToString()),
            new(TrimmeClaims.UserType, session.UserType),
        ];
        if (session.ShopId is { } shopId)
        {
            claims.Add(new Claim(TrimmeClaims.ShopId, shopId.ToString()));
        }

        if (session.ProfessionalId is { } professionalId)
        {
            claims.Add(new Claim(TrimmeClaims.ProfessionalId, professionalId.ToString()));
        }

        var identity = new ClaimsIdentity(
            claims,
            TrimmeClaims.AuthenticationScheme,
            TrimmeClaims.Subject,
            roleType: null);

        var accessExpires = clock.GetUtcNow() + options.AccessLifetime;
        await http.SignInAsync(
            TrimmeClaims.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = true,
                AllowRefresh = false,
                ExpiresUtc = accessExpires < session.ExpiresAt ? accessExpires : session.ExpiresAt,
            });

        http.Response.Cookies.Append(RefreshCookie, session.RefreshToken, RefreshCookieOptions(session.ExpiresAt));
        http.Response.Cookies.Append(SignedInCookie, session.UserType, SignedInCookieOptions(session.ExpiresAt));

        if (rotateCsrf)
        {
            Csrf.Issue(http.Response);
        }
    }

    public static async Task ClearAsync(HttpContext http)
    {
        await http.SignOutAsync(TrimmeClaims.AuthenticationScheme);
        http.Response.Cookies.Delete(RefreshCookie, RefreshCookieOptions(expires: null));
        ClearSignedInHint(http);
    }

    /// <summary>Deletes only the signed-in hint: a refresh without any refresh cookie can never restore a session.</summary>
    public static void ClearSignedInHint(HttpContext http) =>
        http.Response.Cookies.Delete(SignedInCookie, SignedInCookieOptions(expires: null));

    public static string? ReadRefreshToken(HttpRequest request) =>
        request.Cookies.TryGetValue(RefreshCookie, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    public static ClientContext Client(HttpContext http) =>
        new(http.Request.Headers.UserAgent.ToString(), http.Connection.RemoteIpAddress?.ToString());

    private static CookieOptions SignedInCookieOptions(DateTimeOffset? expires) => new()
    {
        HttpOnly = false,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        IsEssential = true,
        Expires = expires,
    };

    private static CookieOptions RefreshCookieOptions(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = RefreshCookiePath,
        IsEssential = true,
        Expires = expires,
    };
}
