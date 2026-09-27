using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.BuildingBlocks.Web.Modules;
using Trimme.Modules.Professionals.Api;
using Trimme.Modules.Professionals.Application.Admin;
using Trimme.Modules.Professionals.Infrastructure.Seeding;

namespace Trimme.Modules.Professionals;

/// <summary>
/// Professionals module (schema <c>professionals</c>): barbers and stylists, each in exactly one shop, managed by
/// platform admins, with a protected WhatsApp contact (Phase 06). A professional's shop never changes (D-011).
/// </summary>
public sealed class ProfessionalsModule : ModuleBase
{
    public override string Name => "Professionals";

    public override string Schema => "professionals";

    public override void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        base.AddServices(services, configuration);
        services.AddScoped<ProfessionalAdminSupport>();
        services.AddSingleton<IDevSeeder, DemoProfessionalsSeeder>();
    }

    public override void MapEndpoints(IEndpointRouteBuilder api) => ProfessionalEndpoints.Map(api);
}
