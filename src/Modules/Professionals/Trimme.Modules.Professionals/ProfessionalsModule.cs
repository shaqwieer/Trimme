using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.BuildingBlocks.Web.Modules;
using Trimme.Modules.Professionals.Api;
using Trimme.Modules.Professionals.Application;
using Trimme.Modules.Professionals.Infrastructure;
using Trimme.Modules.Professionals.Infrastructure.Seeding;

namespace Trimme.Modules.Professionals;

/// <summary>
/// Professionals module (schema <c>professionals</c>): barbers and stylists, each in exactly one shop, managed by the
/// platform admins and by their shop's owner (D-146), with a protected WhatsApp contact (Phase 06). A professional's shop
/// never changes (D-011).
/// </summary>
public sealed class ProfessionalsModule : ModuleBase
{
    public override string Name => "Professionals";

    public override string Schema => "professionals";

    public override void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        base.AddServices(services, configuration);
        services.AddScoped<ProfessionalSupport>();
        services.AddScoped<IProfessionalDirectory, ProfessionalDirectory>();
        services.AddScoped<IProfessionalLoginNumbers, ProfessionalLoginNumbers>();
        services.AddScoped<IBarberAccountProfiles, BarberAccountProfiles>();
        services.AddScoped<BuildingBlocks.Application.Notifications.IProfessionalContactReader, ProfessionalContactReader>();
        services.AddScoped<BuildingBlocks.Application.Reporting.IProfessionalStatistics, ProfessionalStatistics>();
        services.AddSingleton<IDevSeeder, DemoProfessionalsSeeder>();
        services.AddSingleton<IReferenceDataSynchronizer, ProfessionalQrBackfill>();
    }

    public override void MapEndpoints(IEndpointRouteBuilder api) => ProfessionalEndpoints.Map(api);
}
