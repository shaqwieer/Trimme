using NetTopologySuite.Geometries;
using Trimme.BuildingBlocks.Domain.Results;

namespace Trimme.Modules.Shops.Domain;

/// <summary>
/// The exact shop entrance (spec §8, R-SHP-02): a WGS 84 point stored as PostGIS <c>geography(Point, 4326)</c> with a
/// GiST index, plus the address shown to customers. <see cref="Point"/> uses X = longitude, Y = latitude.
/// </summary>
public sealed class ShopLocation
{
    public const int Srid = 4326;
    public const int MaxAddressLineLength = 200;
    public const int MaxAreaLength = 80;
    public const int MaxFormattedAddressLength = 300;

    private static readonly GeometryFactory Geography = new(new PrecisionModel(), Srid);

    private ShopLocation(Point point, string? addressLine, string? district, string? city, string? formattedAddress, LocationSource source, DateTimeOffset confirmedAt, Guid? confirmedBy)
    {
        Point = point;
        AddressLine = addressLine;
        District = district;
        City = city;
        FormattedAddress = formattedAddress;
        Source = source;
        ConfirmedAt = confirmedAt;
        ConfirmedBy = confirmedBy;
    }

    private ShopLocation()
    {
        Point = Point.Empty;
    }

    public Point Point { get; private set; }

    public double Latitude => Point.Y;

    public double Longitude => Point.X;

    /// <summary>Street and building, e.g. "طريق أنس بن مالك، مبنى ١٢".</summary>
    public string? AddressLine { get; private set; }

    public string? District { get; private set; }

    public string? City { get; private set; }

    /// <summary>The resolved address the user confirmed (from the geocoder, or typed).</summary>
    public string? FormattedAddress { get; private set; }

    /// <summary>How the point was chosen.</summary>
    public LocationSource Source { get; private set; }

    public DateTimeOffset ConfirmedAt { get; private set; }

    /// <summary>The user who confirmed the point (admin or shop owner).</summary>
    public Guid? ConfirmedBy { get; private set; }

    /// <summary>Coordinates are rounded to 6 decimal places (about 11 cm).</summary>
    public static Result<ShopLocation> Create(
        double latitude,
        double longitude,
        string? addressLine,
        string? district,
        string? city,
        string? formattedAddress,
        LocationSource source,
        DateTimeOffset confirmedAt,
        Guid? confirmedBy)
    {
        if (!double.IsFinite(latitude) || latitude is < -90 or > 90)
        {
            return Invalid("latitude");
        }

        if (!double.IsFinite(longitude) || longitude is < -180 or > 180)
        {
            return Invalid("longitude");
        }

        var point = Geography.CreatePoint(new Coordinate(Math.Round(longitude, 6), Math.Round(latitude, 6)));
        return new ShopLocation(point, Clean(addressLine), Clean(district), Clean(city), Clean(formattedAddress), source, confirmedAt, confirmedBy);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Error Invalid(string field) =>
        Error.Validation("validation.failed", "The coordinates are invalid.",
            new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = ["validation.coordinate_invalid"] });
}

/// <summary>How the location point was chosen in the picker.</summary>
public enum LocationSource
{
    /// <summary>Placed or dragged on the map, or typed as coordinates.</summary>
    Manual,

    /// <summary>A geocoder search result.</summary>
    Geocoded,

    /// <summary>The device's current position.</summary>
    Device,
}
