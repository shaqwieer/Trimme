using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Trimme.Modules.Shops.Application;
using Trimme.Modules.Shops.Infrastructure.Geocoding;

namespace Trimme.UnitTests.Shops;

/// <summary>Pasting a Google Maps link into the location picker (D-144): the link shapes Google hands out.</summary>
public sealed class GoogleMapsLinkTests
{
    [Theory]
    // A place: the pin (!3d!4d) wins over the camera centre (@…), which is often off the pin.
    [InlineData("https://www.google.com/maps/place/%D8%B5%D8%A7%D9%84%D9%88%D9%86/@24.8120000,46.5990000,17z/data=!3m1!4b1!4m6!3m5!1s0x3e2ee3:0x1!8m2!3d24.8123456!4d46.6011234!16s", 24.8123456, 46.6011234)]
    // The camera centre alone.
    [InlineData("https://www.google.com/maps/@24.7136,46.6753,15z", 24.7136, 46.6753)]
    // Shared pins and the Maps URL API.
    [InlineData("https://maps.google.com/?q=24.7136,46.6753", 24.7136, 46.6753)]
    [InlineData("https://www.google.com/maps/search/?api=1&query=24.7136%2C46.6753", 24.7136, 46.6753)]
    [InlineData("https://www.google.com/maps/dir/?api=1&destination=24.7136,46.6753", 24.7136, 46.6753)]
    [InlineData("https://maps.google.com/maps?ll=24.7136,46.6753&z=16", 24.7136, 46.6753)]
    [InlineData("https://www.google.com/maps/search/24.7136,+46.6753", 24.7136, 46.6753)]
    [InlineData("https://www.google.com/maps/place/24%C2%B042'49.0%22N/@24.7136,46.6753,17z/data=!4m4!3m3!8m2!3d24.71361!4d46.67531", 24.71361, 46.67531)]
    [InlineData("https://www.google.com.sa/maps?q=loc:24.7136,46.6753", 24.7136, 46.6753)]
    // Southern and western hemispheres.
    [InlineData("https://www.google.com/maps?q=-33.8688,151.2093", -33.8688, 151.2093)]
    [InlineData("https://www.google.com/maps/@40.7128,-74.006,12z", 40.7128, -74.006)]
    public void Coordinates_AreReadFromEveryLinkShape(string url, double latitude, double longitude)
    {
        var link = GoogleMapsLink.Parse(url)!;
        GoogleMapsLink.IsMapsHost(link.Host).ShouldBeTrue();
        GoogleMapsLink.Coordinates(link).ShouldBe((latitude, longitude));
    }

    [Theory]
    [InlineData("https://www.google.com/maps/place/Barber+House/data=!4m2!3m1!1s0x0:0x1", "Barber House")]
    [InlineData("https://www.google.com/maps?q=%D8%AD%D9%8A+%D8%A7%D9%84%D9%85%D9%84%D9%82%D8%A7", "حي الملقا")]
    public void ALinkWithoutCoordinates_GivesThePlaceName(string url, string name)
    {
        var link = GoogleMapsLink.Parse(url)!;
        GoogleMapsLink.Coordinates(link).ShouldBeNull();
        GoogleMapsLink.PlaceName(link).ShouldBe(name);
    }

    [Theory]
    [InlineData("صالون الأصالة https://maps.app.goo.gl/AbCdEf123 ", "https://maps.app.goo.gl/AbCdEf123")]
    [InlineData("  https://goo.gl/maps/xyz", "https://goo.gl/maps/xyz")]
    public void AShareText_KeepsItsLink_AndShortLinksAreRecognised(string text, string expected)
    {
        var link = GoogleMapsLink.Parse(text)!;
        link.ToString().ShouldBe(expected);
        GoogleMapsLink.IsShortLink(link).ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("حي الملقا")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://www.google.com/maps")]
    public void NotALink_IsRefused(string text) => GoogleMapsLink.Parse(text).ShouldBeNull();

    [Theory]
    [InlineData("google.com", true)]
    [InlineData("www.google.com.sa", true)]
    [InlineData("maps.google.co.uk", true)]
    [InlineData("google.com.evil.example", false)]
    [InlineData("evilgoogle.com", false)]
    [InlineData("maps.app.goo.gl", false)]
    [InlineData("localhost", false)]
    public void OnlyGoogleHostsServeMaps(string host, bool maps) => GoogleMapsLink.IsMapsHost(host).ShouldBe(maps);

    [Fact]
    public void TheConsentPage_LeadsToItsMapsLink_Only()
    {
        var consent = new Uri("https://consent.google.com/m?continue=https://www.google.com/maps/place/x/@24.7,46.6,17z&gl=SA");
        GoogleMapsLink.ConsentTarget(consent)!.Host.ShouldBe("www.google.com");
        GoogleMapsLink.ConsentTarget(new Uri("https://consent.google.com/m?continue=https://evil.example/maps")).ShouldBeNull();
    }

    [Fact]
    public async Task AShortLink_IsFollowedThroughGoogleOnly()
    {
        var handler = new Redirects(new Dictionary<string, string>
        {
            ["https://maps.app.goo.gl/AbC"] = "https://consent.google.com/m?continue=https%3A%2F%2Fwww.google.com%2Fmaps%2Fplace%2FSalon%2F%4024.8%2C46.6%2C17z%2Fdata%3D!3d24.8123!4d46.6011",
        });
        var resolver = new MapLinkResolver(new HttpClient(handler), NullLogger<MapLinkResolver>.Instance);

        var resolved = await resolver.ResolveAsync(new Uri("https://maps.app.goo.gl/AbC"), TestContext.Current.CancellationToken);

        GoogleMapsLink.Coordinates(resolved!).ShouldBe((24.8123, 46.6011));
        handler.Requested.ShouldBe(["https://maps.app.goo.gl/AbC"]);
    }

    [Fact]
    public async Task AShortLink_RedirectingOffGoogle_IsNotFollowed()
    {
        var handler = new Redirects(new Dictionary<string, string>
        {
            ["https://maps.app.goo.gl/Evil"] = "http://169.254.169.254/latest/meta-data",
            ["https://maps.app.goo.gl/Loop"] = "https://maps.app.goo.gl/Loop",
        });
        var resolver = new MapLinkResolver(new HttpClient(handler), NullLogger<MapLinkResolver>.Instance);
        var ct = TestContext.Current.CancellationToken;

        (await resolver.ResolveAsync(new Uri("https://maps.app.goo.gl/Evil"), ct)).ShouldBeNull();
        (await resolver.ResolveAsync(new Uri("https://maps.app.goo.gl/Loop"), ct)).ShouldBeNull();
        handler.Requested.ShouldNotContain(url => url.Contains("169.254", StringComparison.Ordinal));
        handler.Requested.Count(url => url.EndsWith("/Loop", StringComparison.Ordinal)).ShouldBe(MapLinkResolver.MaxHops);
    }

    [Fact]
    public async Task TheHandler_FillsTheAddress_FromThePoint_OrSearchesThePlaceName()
    {
        var geocoder = new Gazetteer();
        var handler = new ResolveMapLinkHandler(new NoResolver(), geocoder);
        var ct = TestContext.Current.CancellationToken;

        var pinned = (await handler.Handle(new ResolveMapLinkQuery("https://www.google.com/maps/@24.81,46.60,17z", "ar"), ct)).Value;
        pinned.ShouldBe(new MapLinkPlace(24.81, 46.60, null, "شارع، الملقا، الرياض", "شارع", "الملقا", "الرياض"));

        var named = (await handler.Handle(new ResolveMapLinkQuery("https://www.google.com/maps/place/Barber+House/data=!4m2", "ar"), ct)).Value;
        (named.Latitude, named.Longitude, named.PlaceName).ShouldBe((24.76, 46.60, "Barber House"));

        var refused = await handler.Handle(new ResolveMapLinkQuery("https://example.com/maps/@24.81,46.60,17z", "ar"), ct);
        refused.Error!.Code.ShouldBe("geo.map_link_invalid");
        (await handler.Handle(new ResolveMapLinkQuery("https://maps.app.goo.gl/AbC", "ar"), ct)).Error!.Code.ShouldBe("geo.map_link_unresolved");
        (await handler.Handle(new ResolveMapLinkQuery("https://www.google.com/maps", "ar"), ct)).Error!.Code.ShouldBe("geo.map_link_no_location");
    }

    private sealed class Redirects(Dictionary<string, string> map) : HttpMessageHandler
    {
        public List<string> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Requested.Add(url);
            var response = map.TryGetValue(url, out var location)
                ? new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri(location) } }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
            return Task.FromResult(response);
        }
    }

    private sealed class NoResolver : IMapLinkResolver
    {
        public Task<Uri?> ResolveAsync(Uri shortLink, CancellationToken cancellationToken) => Task.FromResult<Uri?>(null);
    }

    private sealed class Gazetteer : IGeocoder
    {
        public Task<IReadOnlyList<GeocodedPlace>> SearchAsync(string query, string language, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GeocodedPlace>>(
                query == "Barber House" ? [new GeocodedPlace(24.76, 46.60, "حطين", null, "حطين", "الرياض")] : []);

        public Task<GeocodedPlace?> ReverseAsync(double latitude, double longitude, string language, CancellationToken cancellationToken) =>
            Task.FromResult<GeocodedPlace?>(new GeocodedPlace(latitude, longitude, "شارع، الملقا، الرياض", "شارع", "الملقا", "الرياض"));
    }
}
