using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.BuildingBlocks.Web.Modules;
using Trimme.Modules.Shops.Api;
using Trimme.Modules.Shops.Infrastructure;
using Trimme.Modules.Shops.Infrastructure.Seeding;

namespace Trimme.Modules.Shops;

/// <summary>Shops module (schema <c>shops</c>): the shop tenant root and its admin lifecycle (Phase 05).</summary>
public sealed class ShopsModule : ModuleBase
{
    public override string Name => "Shops";

    public override string Schema => "shops";

    public override void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        base.AddServices(services, configuration);
        services.AddScoped<IShopDirectory, ShopDirectory>();
        services.AddSingleton<IDevSeeder, DemoShopsSeeder>();
    }

    public override void MapEndpoints(IEndpointRouteBuilder api) => ShopEndpoints.Map(api);
}
