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
/// </summary>
internal static class SessionCookies
{
    public const string AccessCookie = "trimme-access";
    public const string RefreshCookie = "trimme-refresh";
    public const string RefreshCookiePath = "/api/v1/auth";

    /// <summary>
    /// Issues the access and refresh cookies for <paramref name="session"/>. <paramref name="rotateCsrf"/> is true on
    /// sign-in (a new session gets a new CSRF token, defeating a token planted before sign-in) and false on a refresh,
    /// so a request the web client retries after refreshing still carries a valid header.
    /// </summary>
    public static async Task SignInAsync(HttpContext http, IssuedSession session, bool rotateCsrf = true)
    {
        var options = http.RequestServices.GetRequiredService<IOptions<SessionOptions>>().Value;
        var clock = http.RequestServices.GetRequiredService<TimeProvider>();

        var identity = new ClaimsIdentity(
            [
                new Claim(TrimmeClaims.Subject, session.UserId.ToString()),
                new Claim(TrimmeClaims.SessionId, session.SessionId.ToString()),
                new Claim(TrimmeClaims.UserType, session.UserType),
            ],
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

        if (rotateCsrf)
        {
            Csrf.Issue(http.Response);
        }
    }

    public static async Task ClearAsync(HttpContext http)
    {
        await http.SignOutAsync(TrimmeClaims.AuthenticationScheme);
        http.Response.Cookies.Delete(RefreshCookie, RefreshCookieOptions(expires: null));
    }

    public static string? ReadRefreshToken(HttpRequest request) =>
        request.Cookies.TryGetValue(RefreshCookie, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    public static ClientContext Client(HttpContext http) =>
        new(http.Request.Headers.UserAgent.ToString(), http.Connection.RemoteIpAddress?.ToString());

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
