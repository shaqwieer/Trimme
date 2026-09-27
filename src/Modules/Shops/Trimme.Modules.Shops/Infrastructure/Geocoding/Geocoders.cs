using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trimme.Modules.Shops.Application;

namespace Trimme.Modules.Shops.Infrastructure.Geocoding;

internal sealed class GeocodingOptions
{
    public const string SectionName = "Geocoding";
    public const string FakeProvider = "Fake";
    public const string NominatimProvider = "Nominatim";

    /// <summary><c>Fake</c> (default: no third-party calls) or <c>Nominatim</c>.</summary>
    public string Provider { get; set; } = FakeProvider;

    public NominatimOptions Nominatim { get; set; } = new();
}

internal sealed class NominatimOptions
{
    /// <summary>A Nominatim-compatible host. The public OSM instance allows at most one request per second (usage policy).</summary>
    public Uri BaseUrl { get; set; } = new("https://nominatim.openstreetmap.org/");

    /// <summary>Identifies the application to the provider, as its usage policy requires.</summary>
    public string UserAgent { get; set; } = "TRIMME-dev/0.1 (+https://github.com/shaqwieer/Trimme)";

    /// <summary>ISO country codes that bound search results.</summary>
    public string CountryCodes { get; set; } = "sa";

    public int MinIntervalMilliseconds { get; set; } = 1100;
}

/// <summary>
/// Nominatim adapter (development, D-007): results are cached for a day and requests are serialized and spaced to
/// respect the one-request-per-second policy. Failures degrade to "no result" and are logged without the query.
/// </summary>
internal sealed partial class NominatimGeocoder(
    HttpClient http,
    IMemoryCache cache,
    NominatimThrottle throttle,
    IOptions<GeocodingOptions> options,
    ILogger<NominatimGeocoder> logger) : IGeocoder
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

    public async Task<IReadOnlyList<GeocodedPlace>> SearchAsync(string query, string language, CancellationToken cancellationToken)
    {
        var settings = options.Value.Nominatim;
        var url = $"search?format=jsonv2&addressdetails=1&limit=5&countrycodes={Uri.EscapeDataString(settings.CountryCodes)}"
                  + $"&accept-language={language}&q={Uri.EscapeDataString(query)}";
        return await cache.GetOrCreateAsync($"geo:s:{language}:{query.ToLowerInvariant()}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            var places = await GetAsync<List<NominatimPlace>>(url, cancellationToken) ?? [];
            return (IReadOnlyList<GeocodedPlace>)[.. places.Select(ToPlace).OfType<GeocodedPlace>()];
        }) ?? [];
    }

    public async Task<GeocodedPlace?> ReverseAsync(double latitude, double longitude, string language, CancellationToken cancellationToken)
    {
        var lat = Math.Round(latitude, 5).ToString(CultureInfo.InvariantCulture);
        var lon = Math.Round(longitude, 5).ToString(CultureInfo.InvariantCulture);
        return await cache.GetOrCreateAsync($"geo:r:{language}:{lat},{lon}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            var place = await GetAsync<NominatimPlace>($"reverse?format=jsonv2&addressdetails=1&zoom=18&accept-language={language}&lat={lat}&lon={lon}", cancellationToken);
            return place is null ? null : ToPlace(place);
        });
    }

    private async Task<T?> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken)
        where T : class
    {
        await throttle.WaitAsync(cancellationToken);
        try
        {
            using var response = await http.GetAsync(new Uri(relativeUrl, UriKind.Relative), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogFailure(logger, (int)response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            LogFailure(logger, 0);
            return null;
        }
        finally
        {
            throttle.Release();
        }
    }

    private static GeocodedPlace? ToPlace(NominatimPlace place)
    {
        if (place.DisplayName is null
            || !double.TryParse(place.Lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
            || !double.TryParse(place.Lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
        {
            return null;
        }

        var address = place.Address;
        var line = string.Join(' ', new[] { address?.Road, address?.HouseNumber }.Where(p => !string.IsNullOrWhiteSpace(p)));
        return new GeocodedPlace(
            lat,
            lon,
            place.DisplayName,
            line.Length == 0 ? null : line,
            address?.Suburb ?? address?.Neighbourhood ?? address?.Quarter ?? address?.CityDistrict,
            address?.City ?? address?.Town ?? address?.State);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Geocoding request failed (status {Status}); returning no result")]
    private static partial void LogFailure(ILogger logger, int status);

    private sealed record NominatimPlace(
        [property: JsonPropertyName("lat")] string? Lat,
        [property: JsonPropertyName("lon")] string? Lon,
        [property: JsonPropertyName("display_name")] string? DisplayName,
        [property: JsonPropertyName("address")] NominatimAddress? Address);

    private sealed record NominatimAddress(
        [property: JsonPropertyName("road")] string? Road,
        [property: JsonPropertyName("house_number")] string? HouseNumber,
        [property: JsonPropertyName("suburb")] string? Suburb,
        [property: JsonPropertyName("neighbourhood")] string? Neighbourhood,
        [property: JsonPropertyName("quarter")] string? Quarter,
        [property: JsonPropertyName("city_district")] string? CityDistrict,
        [property: JsonPropertyName("city")] string? City,
        [property: JsonPropertyName("town")] string? Town,
        [property: JsonPropertyName("state")] string? State);
}

/// <summary>Serializes provider calls and keeps at least the configured interval between them (process-wide).</summary>
internal sealed class NominatimThrottle(IOptions<GeocodingOptions> options, TimeProvider clock) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _last = DateTimeOffset.MinValue;

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        var wait = _last + TimeSpan.FromMilliseconds(options.Value.Nominatim.MinIntervalMilliseconds) - clock.GetUtcNow();
        if (wait > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(wait, clock, cancellationToken);
            }
            catch
            {
                _gate.Release();
                throw;
            }
        }
    }

    public void Release()
    {
        _last = clock.GetUtcNow();
        _gate.Release();
    }

    public void Dispose() => _gate.Dispose();
}

/// <summary>
/// Deterministic geocoder over a small gazetteer of Riyadh districts with real coordinates. Used by tests, CI and the
/// default local stack, so no automated run calls a third-party service.
/// </summary>
internal sealed class FakeGeocoder : IGeocoder
{
    internal static readonly IReadOnlyList<(string Ar, string En, double Lat, double Lng, string StreetAr, string StreetEn)> Districts =
    [
        ("الملقا", "Al Malqa", 24.8123, 46.6011, "طريق أنس بن مالك", "Anas Ibn Malik Road"),
        ("حطين", "Hittin", 24.7630, 46.6010, "طريق الأمير تركي", "Prince Turki Road"),
        ("النرجس", "Al Narjis", 24.8420, 46.6690, "طريق عثمان بن عفان", "Othman Ibn Affan Road"),
        ("العليا", "Al Olaya", 24.6930, 46.6850, "طريق العليا", "Olaya Street"),
        ("الياسمين", "Al Yasmin", 24.8240, 46.6440, "طريق الملك عبدالعزيز", "King Abdulaziz Road"),
        ("السويدي", "As Suwaidi", 24.5950, 46.6680, "طريق السويدي العام", "As Suwaidi Road"),
        ("قرطبة", "Qurtubah", 24.8070, 46.7470, "طريق الإمام عبدالله بن سعود", "Imam Abdullah Ibn Saud Road"),
    ];

    public Task<IReadOnlyList<GeocodedPlace>> SearchAsync(string query, string language, CancellationToken cancellationToken)
    {
        var term = query.Trim();
        IReadOnlyList<GeocodedPlace> matches =
        [
            .. Districts
                .Where(d => d.Ar.Contains(term, StringComparison.Ordinal) || term.Contains(d.Ar, StringComparison.Ordinal)
                            || d.En.Contains(term, StringComparison.OrdinalIgnoreCase) || term.Contains(d.En, StringComparison.OrdinalIgnoreCase))
                .Select(d => ToPlace(d, language)),
        ];
        return Task.FromResult(matches);
    }

    public Task<GeocodedPlace?> ReverseAsync(double latitude, double longitude, string language, CancellationToken cancellationToken)
    {
        var nearest = Districts.MinBy(d => Math.Pow(d.Lat - latitude, 2) + Math.Pow(d.Lng - longitude, 2));
        var within = Math.Abs(nearest.Lat - latitude) < 0.2 && Math.Abs(nearest.Lng - longitude) < 0.2;
        return Task.FromResult(within ? ToPlace(nearest, language) with { Latitude = latitude, Longitude = longitude } : null);
    }

    private static GeocodedPlace ToPlace((string Ar, string En, double Lat, double Lng, string StreetAr, string StreetEn) d, string language) =>
        language == "en"
            ? new GeocodedPlace(d.Lat, d.Lng, $"{d.StreetEn}, {d.En}, Riyadh", d.StreetEn, d.En, "Riyadh")
            : new GeocodedPlace(d.Lat, d.Lng, $"{d.StreetAr}، حي {d.Ar}، الرياض", d.StreetAr, d.Ar, "الرياض");
}
