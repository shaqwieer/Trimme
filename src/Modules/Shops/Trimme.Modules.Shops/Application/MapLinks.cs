using System.Globalization;
using System.Text.RegularExpressions;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Domain.Results;

namespace Trimme.Modules.Shops.Application;

/// <summary>
/// The point and address a pasted Google Maps link names (D-144): the coordinates come from the link itself; the
/// address fields from reverse geocoding them, else the place's name in the link. Any field the provider cannot fill is
/// null and stays editable in the picker.
/// </summary>
public sealed record MapLinkPlace(
    double Latitude,
    double Longitude,
    string? PlaceName,
    string? FormattedAddress,
    string? AddressLine,
    string? District,
    string? City);

/// <summary>
/// Follows a Google Maps short link (<c>maps.app.goo.gl</c>, <c>goo.gl/maps</c>) to the full link it stands for,
/// contacting only Google hosts; <c>null</c> when it cannot (unknown link, network failure, a hop off Google).
/// </summary>
public interface IMapLinkResolver
{
    Task<Uri?> ResolveAsync(Uri shortLink, CancellationToken cancellationToken);
}

/// <summary>
/// Reads Google Maps links. Pure, so the link shapes are unit-tested: <c>/maps/place/Name/@lat,lng,17z/data=…!3dLAT!4dLNG</c>,
/// <c>?q=lat,lng</c>, <c>?api=1&amp;query=lat,lng</c>, <c>?ll=</c>, <c>/maps/dir/?destination=</c>, <c>/maps/search/lat,lng</c>,
/// and the consent page's <c>continue</c> link.
/// </summary>
public static partial class GoogleMapsLink
{
    public const int MaxLength = 2048;

    private static readonly string[] CoordinateParameters = ["q", "query", "ll", "destination", "daddr", "center", "sll"];
    private static readonly string[] NameParameters = ["q", "query", "destination", "daddr"];

    /// <summary>A Google host that serves Maps: <c>google.com</c>, <c>maps.google.com</c>, <c>www.google.com.sa</c>, …</summary>
    public static bool IsMapsHost(string host) => MapsHostPattern().IsMatch(host);

    /// <summary>A short-link host whose redirect the API may follow.</summary>
    public static bool IsShortLink(Uri link) =>
        link.Host.Equals("maps.app.goo.gl", StringComparison.OrdinalIgnoreCase)
        || (link.Host.Equals("goo.gl", StringComparison.OrdinalIgnoreCase) && link.AbsolutePath.StartsWith("/maps", StringComparison.OrdinalIgnoreCase));

    /// <summary>Google's cookie-consent interstitial, which carries the real link in <c>continue</c>.</summary>
    public static bool IsConsentHost(string host) => host.Equals("consent.google.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>The link as an absolute https/http URI, trimmed of anything pasted around it; <c>null</c> when it is none.</summary>
    public static Uri? Parse(string? text)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        // People paste a share text ("Salon … https://maps.app.goo.gl/…"): keep the first URL in it.
        var start = trimmed.IndexOf("http", StringComparison.OrdinalIgnoreCase);
        if (start < 0 || trimmed.Length > MaxLength)
        {
            return null;
        }

        var candidate = trimmed[start..].Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries)[0];
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri
            : null;
    }

    /// <summary>The consent page's <c>continue</c> link, when it is a Google Maps link.</summary>
    public static Uri? ConsentTarget(Uri consent)
    {
        var target = Parse(Query(consent, "continue"));
        return target is not null && IsMapsHost(target.Host) ? target : null;
    }

    /// <summary>
    /// The pin's coordinates. The place's own <c>!3dLAT!4dLNG</c> comes first: <c>@lat,lng</c> is the camera's centre,
    /// often off the pin. Then the query parameters, then a coordinate typed as the search or place name.
    /// </summary>
    public static (double Latitude, double Longitude)? Coordinates(Uri link)
    {
        var text = Uri.UnescapeDataString(link.OriginalString);
        var pins = PinPattern().Matches(text);
        if (pins.Count > 0 && Pair(pins[^1].Groups["lat"].Value, pins[^1].Groups["lng"].Value) is { } pin)
        {
            return pin;
        }

        foreach (var name in CoordinateParameters)
        {
            if (Pair(Query(link, name)) is { } fromQuery)
            {
                return fromQuery;
            }
        }

        foreach (var segment in PlaceSegments(link))
        {
            if (Pair(segment) is { } fromPath)
            {
                return fromPath;
            }
        }

        var camera = CameraPattern().Match(text);
        return camera.Success ? Pair(camera.Groups["lat"].Value, camera.Groups["lng"].Value) : null;
    }

    /// <summary>The place's name in the link (<c>/maps/place/Name/</c>, or a non-coordinate <c>q</c>), for the address fallback.</summary>
    public static string? PlaceName(Uri link)
    {
        foreach (var segment in PlaceSegments(link))
        {
            if (Pair(segment) is null && Clean(segment) is { } name)
            {
                return name;
            }
        }

        foreach (var parameter in NameParameters)
        {
            var value = Query(link, parameter);
            if (value is not null && Pair(value) is null && Clean(value) is { } name)
            {
                return name;
            }
        }

        return null;
    }

    private static IEnumerable<string> PlaceSegments(Uri link)
    {
        var segments = link.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (segments[i] is "place" or "search")
            {
                yield return Uri.UnescapeDataString(segments[i + 1].Replace('+', ' '));
            }
        }
    }

    private static string? Query(Uri link, string name)
    {
        foreach (var part in link.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = part.IndexOf('=', StringComparison.Ordinal);
            var key = equals < 0 ? part : part[..equals];
            if (key.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return equals < 0 ? string.Empty : Uri.UnescapeDataString(part[(equals + 1)..].Replace('+', ' '));
            }
        }

        return null;
    }

    private static (double, double)? Pair(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = PairPattern().Match(value.Trim());
        return match.Success ? Pair(match.Groups["lat"].Value, match.Groups["lng"].Value) : null;
    }

    private static (double, double)? Pair(string lat, string lng) =>
        double.TryParse(lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
        && double.TryParse(lng, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude)
        && latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180
        && !(latitude == 0 && longitude == 0)
            ? (latitude, longitude)
            : null;

    private static string? Clean(string value)
    {
        var name = value.Trim();
        return name.Length is >= 2 and <= 120 ? name : null;
    }

    [GeneratedRegex(@"!3d(?<lat>-?\d{1,2}(?:\.\d+)?)!4d(?<lng>-?\d{1,3}(?:\.\d+)?)", RegexOptions.CultureInvariant)]
    private static partial Regex PinPattern();

    [GeneratedRegex(@"@(?<lat>-?\d{1,2}(?:\.\d+)?),(?<lng>-?\d{1,3}(?:\.\d+)?)", RegexOptions.CultureInvariant)]
    private static partial Regex CameraPattern();

    [GeneratedRegex(@"^(?:loc:)?\s*(?<lat>-?\d{1,2}(?:\.\d+)?)\s*,\s*(?<lng>-?\d{1,3}(?:\.\d+)?)(?:\s*\(.*\))?$", RegexOptions.CultureInvariant)]
    private static partial Regex PairPattern();

    [GeneratedRegex(@"^(?:www\.|maps\.)?google\.(?:com|[a-z]{2}|com?\.[a-z]{2})$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex MapsHostPattern();
}

internal sealed record ResolveMapLinkQuery(string Url, string Language) : IQuery<Result<MapLinkPlace>>;

/// <summary>
/// "Paste the Google Maps link" in the location picker (D-144): reads the point from the link, following a short link
/// through Google only; reverse-geocodes it for the address. A link without coordinates is searched by its place name.
/// </summary>
internal sealed class ResolveMapLinkHandler(IMapLinkResolver resolver, IGeocoder geocoder) : IQueryHandler<ResolveMapLinkQuery, Result<MapLinkPlace>>
{
    public async Task<Result<MapLinkPlace>> Handle(ResolveMapLinkQuery query, CancellationToken cancellationToken)
    {
        if (GoogleMapsLink.Parse(query.Url) is not { } link)
        {
            return Invalid("geo.map_link_invalid", "This is not a Google Maps link.");
        }

        if (GoogleMapsLink.IsShortLink(link))
        {
            link = await resolver.ResolveAsync(link, cancellationToken) ?? link;
            if (GoogleMapsLink.IsShortLink(link))
            {
                return Invalid("geo.map_link_unresolved", "The short link could not be opened.");
            }
        }
        else if (GoogleMapsLink.IsConsentHost(link.Host))
        {
            link = GoogleMapsLink.ConsentTarget(link) ?? link;
        }

        if (!GoogleMapsLink.IsMapsHost(link.Host))
        {
            return Invalid("geo.map_link_invalid", "This is not a Google Maps link.");
        }

        var language = GeocodingLanguage.Normalize(query.Language);
        var name = GoogleMapsLink.PlaceName(link);
        var point = GoogleMapsLink.Coordinates(link);
        if (point is null && name is not null)
        {
            var found = await geocoder.SearchAsync(name, language, cancellationToken);
            point = found.Count == 0 ? null : (found[0].Latitude, found[0].Longitude);
        }

        if (point is not { } at)
        {
            return Invalid("geo.map_link_no_location", "The link names no location.");
        }

        var address = await geocoder.ReverseAsync(at.Latitude, at.Longitude, language, cancellationToken);
        return new MapLinkPlace(
            at.Latitude, at.Longitude, name,
            address?.FormattedAddress ?? name, address?.AddressLine, address?.District, address?.City);
    }

    private static Error Invalid(string code, string message) =>
        Error.Validation(code, message, new Dictionary<string, string[]>(StringComparer.Ordinal) { ["url"] = [code] });
}
