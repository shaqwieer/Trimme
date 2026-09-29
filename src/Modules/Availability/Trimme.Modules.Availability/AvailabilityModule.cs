using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Trimme.BuildingBlocks.Application.Discovery;
using Trimme.BuildingBlocks.Application.Scheduling;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.BuildingBlocks.Web.Modules;
using Trimme.Modules.Availability.Api;
using Trimme.Modules.Availability.Application;
using Trimme.Modules.Availability.Application.Public;
using Trimme.Modules.Availability.Infrastructure;
using Trimme.Modules.Availability.Infrastructure.Seeding;

namespace Trimme.Modules.Availability;

/// <summary>
/// Availability module (schema <c>availability</c>, Phase 09): shop opening hours and closures, professionals' hours,
/// breaks and time off, and the availability engine behind the public slot API (spec §11, D-082).
/// </summary>
public sealed class AvailabilityModule : ModuleBase
{
    public override string Name => "Availability";

    public override string Schema => "availability";

    public override void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        base.AddServices(services, configuration);
        services.AddScoped<ScheduleLoader>();
        services.AddScoped<ShopScheduleContext>();
        services.AddScoped<ConflictFinder>();
        services.AddScoped<PublicAvailabilityService>();
        services.AddScoped<IAvailabilityChecker, AvailabilityChecker>();
        services.AddScoped<IShopOpeningReader, ShopOpeningReader>();
        services.AddScoped<ISlotProbe, SlotProbe>();
        services.TryAddScoped<IBookedTimeReader, NoBookedTime>();
        services.AddSingleton<IDevSeeder, DemoSchedulesSeeder>();
    }

    public override void MapEndpoints(IEndpointRouteBuilder api) => AvailabilityEndpoints.Map(api);
}
