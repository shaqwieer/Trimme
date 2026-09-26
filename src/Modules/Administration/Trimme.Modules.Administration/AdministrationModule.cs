using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trimme.BuildingBlocks.Application.Auditing;
using Trimme.BuildingBlocks.Web.Modules;
using Trimme.Modules.Administration.Infrastructure;

namespace Trimme.Modules.Administration;

/// <summary>Administration module (schema <c>administration</c>): the audit trail (Phase 05); platform settings in Phase 08.</summary>
public sealed class AdministrationModule : ModuleBase
{
    public override string Name => "Administration";

    public override string Schema => "administration";

    public override void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        base.AddServices(services, configuration);
        services.AddScoped<IAuditLog, AuditLog>();
    }
}
