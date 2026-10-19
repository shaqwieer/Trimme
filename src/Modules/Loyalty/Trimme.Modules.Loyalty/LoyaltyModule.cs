using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trimme.BuildingBlocks.Application.Loyalty;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Web.Modules;
using Trimme.Modules.Loyalty.Api;
using Trimme.Modules.Loyalty.Application.Customer;
using Trimme.Modules.Loyalty.Infrastructure;
using Trimme.Modules.Loyalty.Jobs;

namespace Trimme.Modules.Loyalty;

/// <summary>
/// Loyalty module (schema <c>loyalty</c>, D-132): a shop's stamp card. The platform admin sets each shop's terms; completed
/// online bookings earn stamps; a full card becomes a reward the customer chooses when booking, and the shop applies it at
/// the visit.
/// </summary>
public sealed class LoyaltyModule : ModuleBase
{
    public override string Name => "Loyalty";

    public override string Schema => "loyalty";

    public override void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        base.AddServices(services, configuration);
        services.AddScoped<ILoyaltyRewardHolder, LoyaltyRewardHolder>();
        services.AddScoped<CustomerLoyaltyReader>();
        services.AddScoped<IOutboxConsumer, LoyaltyBookingConsumer>();
    }

    public override void MapEndpoints(IEndpointRouteBuilder api) => LoyaltyEndpoints.Map(api);
}
