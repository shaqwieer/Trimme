using FluentValidation;
using Trimme.BuildingBlocks.Application.Messaging;

namespace Trimme.Modules.Shops.Application;

/// <summary>An address search or reverse-geocoding result.</summary>
public sealed record GeocodedPlace(
    double Latitude,
    double Longitude,
    string FormattedAddress,
    string? AddressLine,
    string? District,
    string? City);

/// <summary>
/// Map provider port for the location picker (D-007): forward search and reverse geocoding, always through the API so
/// provider keys and usage limits stay server-side. Adapters: Nominatim-compatible (development) and a fixed Riyadh
/// gazetteer (tests, CI and the default local stack). The production provider is configuration.
/// </summary>
public interface IGeocoder
{
    Task<IReadOnlyList<GeocodedPlace>> SearchAsync(string query, string language, CancellationToken cancellationToken);

    Task<GeocodedPlace?> ReverseAsync(double latitude, double longitude, string language, CancellationToken cancellationToken);
}

internal sealed record SearchPlacesQuery(string Query, string Language) : IQuery<IReadOnlyList<GeocodedPlace>>;

internal sealed record ReversePlaceQuery(double Latitude, double Longitude, string Language) : IQuery<GeocodedPlace?>;

internal sealed class SearchPlacesValidator : AbstractValidator<SearchPlacesQuery>
{
    public SearchPlacesValidator()
    {
        RuleFor(q => q.Query).Must(q => q.Trim().Length >= 2).WithErrorCode("validation.too_short")
            .MaximumLength(120).WithErrorCode("validation.too_long");
    }
}

internal sealed class ReversePlaceValidator : AbstractValidator<ReversePlaceQuery>
{
    public ReversePlaceValidator()
    {
        RuleFor(q => q.Latitude).InclusiveBetween(-90, 90).WithErrorCode("validation.coordinate_invalid");
        RuleFor(q => q.Longitude).InclusiveBetween(-180, 180).WithErrorCode("validation.coordinate_invalid");
    }
}

internal sealed class SearchPlacesHandler(IGeocoder geocoder) : IQueryHandler<SearchPlacesQuery, IReadOnlyList<GeocodedPlace>>
{
    public Task<IReadOnlyList<GeocodedPlace>> Handle(SearchPlacesQuery query, CancellationToken cancellationToken) =>
        geocoder.SearchAsync(query.Query.Trim(), GeocodingLanguage.Normalize(query.Language), cancellationToken);
}

internal sealed class ReversePlaceHandler(IGeocoder geocoder) : IQueryHandler<ReversePlaceQuery, GeocodedPlace?>
{
    public Task<GeocodedPlace?> Handle(ReversePlaceQuery query, CancellationToken cancellationToken) =>
        geocoder.ReverseAsync(query.Latitude, query.Longitude, GeocodingLanguage.Normalize(query.Language), cancellationToken);
}

internal static class GeocodingLanguage
{
    public static string Normalize(string? language) => language == "en" ? "en" : "ar";
}
