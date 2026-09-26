using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Trimme.BuildingBlocks.Web.Security;

namespace Trimme.IntegrationTests.Infrastructure;

/// <summary>
/// A browser-like client: keeps cookies for <c>https://localhost</c> (the session cookies are <c>Secure</c>) and, like
/// the web app, echoes the CSRF cookie in the <c>X-CSRF-Token</c> header on unsafe requests.
/// </summary>
public sealed class ApiSession : IDisposable
{
    public static readonly Uri Origin = new("https://localhost");

    private ApiSession(HttpClient client, CookieContainer cookies)
    {
        Client = client;
        Cookies = cookies;
    }

    public HttpClient Client { get; }

    public CookieContainer Cookies { get; }

    public static ApiSession Create(TrimmeApiFactory factory)
    {
        var cookies = new CookieContainer();
        var client = factory.CreateDefaultClient(Origin, new CookieContainerHandler(cookies));
        return new ApiSession(client, cookies);
    }

    public string? Cookie(string name, string path = "/") =>
        Cookies.GetCookies(new Uri(Origin, path))[name]?.Value;

    public Task<HttpResponseMessage> GetAsync(string path, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, path, body: null, cancellationToken);

    public Task<HttpResponseMessage> PostAsync(string path, object? body, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, path, body, cancellationToken);

    public Task<HttpResponseMessage> DeleteAsync(string path, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Delete, path, body: null, cancellationToken);

    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken,
        bool withCsrf = true)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (withCsrf && method != HttpMethod.Get && method != HttpMethod.Head)
        {
            request.Headers.Add(Csrf.HeaderName, await CsrfTokenAsync(cancellationToken));
        }

        return await Client.SendAsync(request, cancellationToken);
    }

    /// <summary>Returns the current CSRF cookie, fetching one first if the "browser" has none (as the web client does).</summary>
    public async Task<string> CsrfTokenAsync(CancellationToken cancellationToken)
    {
        if (Cookie(Csrf.CookieName) is { Length: > 0 } token)
        {
            return token;
        }

        using var response = await Client.GetAsync(new Uri("/api/v1/auth/csrf", UriKind.Relative), cancellationToken);
        response.EnsureSuccessStatusCode();
        return Cookie(Csrf.CookieName) ?? throw new InvalidOperationException("The CSRF endpoint did not set a cookie.");
    }

    public void Dispose() => Client.Dispose();
}

public static class HttpResponseAssertions
{
    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    public static async Task<string?> ErrorCodeAsync(this HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var json = await response.JsonAsync(cancellationToken);
        return json.TryGetProperty("errorCode", out var code) ? code.GetString() : null;
    }
}
