using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Discovery;
using Trimme.BuildingBlocks.Application.Scheduling;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.BuildingBlocks.Web.Modules;
using Trimme.Modules.Services.Api;
using Trimme.Modules.Services.Application;
using Trimme.Modules.Services.Infrastructure;
using Trimme.Modules.Services.Infrastructure.Seeding;

namespace Trimme.Modules.Services;

/// <summary>
/// Services module (schema <c>services</c>): platform categories, each shop's own services and packages, and the
/// admin's professional–service assignments (Phase 07). Prices and durations belong to the shops (spec §10).
/// </summary>
public sealed class ServicesModule : ModuleBase
{
    public override string Name => "Services";

    public override string Schema => "services";

    public override void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        base.AddServices(services, configuration);
        services.AddScoped<ShopCatalogReader>();
        services.AddScoped<IShopServiceUsage, PackageServiceUsage>();
        services.AddScoped<IBookableOfferCatalog, BookableOfferCatalog>();
        services.AddScoped<IShopOfferReader, ShopOfferReader>();
        services.AddScoped<BuildingBlocks.Application.Reporting.IServiceCategoryLookup, ServiceCategoryLookup>();
        services.AddSingleton<IDevSeeder, DemoCatalogSeeder>();
    }

    public override void MapEndpoints(IEndpointRouteBuilder api) => CatalogEndpoints.Map(api);
}
