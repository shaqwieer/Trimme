using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trimme.BuildingBlocks.Application.Qr;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.BuildingBlocks.Web.Modules;
using Trimme.Modules.QrAnalytics.Api;
using Trimme.Modules.QrAnalytics.Application;
using Trimme.Modules.QrAnalytics.Application.Public;
using Trimme.Modules.QrAnalytics.Infrastructure;
using Trimme.Modules.QrAnalytics.Infrastructure.Seeding;

namespace Trimme.Modules.QrAnalytics;

/// <summary>
/// QR analytics module (schema <c>qr</c>, Phase 16): unique printed codes for shops and professionals, privacy-preserving
/// scan counting, first-party booking attribution, the admin analytics and the shop's own downloads (spec §17; D-114).
/// </summary>
public sealed class QrAnalyticsModule : ModuleBase
{
    public override string Name => "QrAnalytics";

    public override string Schema => "qr";

    public override void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        base.AddServices(services, configuration);
        services.Configure<QrOptions>(configuration.GetSection(QrOptions.SectionName));
        services.Configure<QrLinkOptions>(configuration.GetSection(QrLinkOptions.SectionName));
        services.AddScoped<QrCodeResolver>();
        services.AddScoped<QrReadModel>();
        services.AddScoped<IQrAttributionResolver, QrAttributionResolver>();
        services.AddSingleton<IDevSeeder, DemoQrSeeder>();
    }

    public override void MapEndpoints(IEndpointRouteBuilder api) => QrEndpoints.Map(api);
}
