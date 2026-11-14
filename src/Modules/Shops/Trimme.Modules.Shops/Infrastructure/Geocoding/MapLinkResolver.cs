using Microsoft.Extensions.Logging;
using Trimme.Modules.Shops.Application;

namespace Trimme.Modules.Shops.Infrastructure.Geocoding;

/// <summary>
/// Follows a Google Maps short link (D-144). This is the only place the API fetches a URL a user typed, so it is kept
/// to Google: redirects are never followed automatically (the handler is built with <c>AllowAutoRedirect = false</c>);
/// each hop's <c>Location</c> must be https on a short-link, consent or Google Maps host; at most <see cref="MaxHops"/>
/// hops; the body is never read. Failures degrade to "unresolved" and are logged without the link.
/// </summary>
internal sealed class MapLinkResolver(HttpClient http, ILogger<MapLinkResolver> logger) : IMapLinkResolver
{
    public const int MaxHops = 4;

    public async Task<Uri?> ResolveAsync(Uri shortLink, CancellationToken cancellationToken)
    {
        var current = shortLink;
        for (var hop = 0; hop < MaxHops; hop++)
        {
            if (current.Scheme != Uri.UriSchemeHttps)
            {
                return null;
            }

            if (GoogleMapsLink.IsConsentHost(current.Host))
            {
                current = GoogleMapsLink.ConsentTarget(current) ?? current;
                if (GoogleMapsLink.IsConsentHost(current.Host))
                {
                    return null;
                }
            }

            if (GoogleMapsLink.IsMapsHost(current.Host) && GoogleMapsLink.Coordinates(current) is not null)
            {
                return current;
            }

            if (!GoogleMapsLink.IsShortLink(current) && !GoogleMapsLink.IsMapsHost(current.Host))
            {
                return null;
            }

            Uri? next;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                var status = (int)response.StatusCode;
                next = status is >= 300 and < 400 && response.Headers.Location is { } location
                    ? (location.IsAbsoluteUri ? location : new Uri(current, location))
                    : null;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("A Google Maps short link could not be followed: {Error}.", exception.GetType().Name);
                return null;
            }

            if (next is null)
            {
                // No further redirect: a Maps page is the answer (its name may still be searched), anything else is not.
                return GoogleMapsLink.IsMapsHost(current.Host) ? current : null;
            }

            current = next;
        }

        return GoogleMapsLink.IsMapsHost(current.Host) ? current : null;
    }
}
