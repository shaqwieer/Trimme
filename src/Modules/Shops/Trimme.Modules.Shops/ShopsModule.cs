using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.BuildingBlocks.Web.Modules;
using Trimme.Modules.Shops.Api;
using Trimme.Modules.Shops.Application;
using Trimme.Modules.Shops.Infrastructure;
using Trimme.Modules.Shops.Infrastructure.Geocoding;
using Trimme.Modules.Shops.Infrastructure.Seeding;

namespace Trimme.Modules.Shops;

/// <summary>Shops module (schema <c>shops</c>): the shop tenant root, its profile, location and admin lifecycle.</summary>
public sealed class ShopsModule : ModuleBase
{
    public override string Name => "Shops";

    public override string Schema => "shops";

    public override void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        base.AddServices(services, configuration);
        services.AddScoped<IShopDirectory, ShopDirectory>();
        services.AddScoped<Application.Public.DiscoveryCatalog>();
        services.AddSingleton<IDevSeeder, DemoShopsSeeder>();

        var geocoding = configuration.GetSection(GeocodingOptions.SectionName);
        services.Configure<GeocodingOptions>(geocoding);
        if (string.Equals(geocoding[nameof(GeocodingOptions.Provider)], GeocodingOptions.NominatimProvider, StringComparison.OrdinalIgnoreCase))
        {
            services.AddMemoryCache();
            services.AddSingleton<NominatimThrottle>();
            services.AddHttpClient<IGeocoder, NominatimGeocoder>((provider, http) =>
            {
                var options = provider.GetRequiredService<IOptions<GeocodingOptions>>().Value.Nominatim;
                http.BaseAddress = options.BaseUrl;
                http.Timeout = TimeSpan.FromSeconds(8);
                http.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
            });
        }
        else
        {
            services.AddSingleton<IGeocoder, FakeGeocoder>();
        }
    }

    public override void MapEndpoints(IEndpointRouteBuilder api) => ShopEndpoints.Map(api);
}
