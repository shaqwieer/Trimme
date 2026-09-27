using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trimme.BuildingBlocks.Application.Platform;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.BuildingBlocks.Web.Modules;
using Trimme.Modules.Subscriptions.Api;
using Trimme.Modules.Subscriptions.Application.Admin;
using Trimme.Modules.Subscriptions.Infrastructure;
using Trimme.Modules.Subscriptions.Infrastructure.Seeding;

namespace Trimme.Modules.Subscriptions;

/// <summary>
/// Subscriptions module (schema <c>subscriptions</c>): SuperAdmin plans with versioned prices, each shop's
/// subscription periods and overrides, and the D-014 bookability gate (Phase 08). No payment in v1 (spec §15).
/// </summary>
public sealed class SubscriptionsModule : ModuleBase
{
    public override string Name => "Subscriptions";

    public override string Schema => "subscriptions";

    public override void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        base.AddServices(services, configuration);
        services.AddScoped<SubscriptionSuspension>();
        services.AddScoped<IShopBookability, ShopBookabilityService>();
        services.AddSingleton<IDevSeeder, DemoSubscriptionsSeeder>();
    }

    public override void MapEndpoints(IEndpointRouteBuilder api) => SubscriptionEndpoints.Map(api);
}
