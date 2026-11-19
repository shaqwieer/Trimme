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
/// Loyalty module (schema <c>loyalty</c>): points per mobile number per shop (D-149), while the shop's paid add-on is in
/// force. The owner sets the rules; completed visits earn points; a full card is a reward the customer (or the barber, for a
/// walk-in's number) uses when booking. D-132 coupons already issued stay usable until they expire.
/// </summary>
public sealed class LoyaltyModule : ModuleBase
{
    public override string Name => "Loyalty";

    public override string Schema => "loyalty";

    public override void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        base.AddServices(services, configuration);
        services.AddScoped<ILoyaltyRewardHolder, LoyaltyRewardHolder>();
        services.AddScoped<ILoyaltyReportSource, Application.Shop.LoyaltyReportSource>();
        services.AddScoped<CustomerLoyaltyReader>();
        services.AddScoped<Application.LoyaltyLedger>();
        services.AddScoped<Application.LoyaltyRulesStore>();
        services.AddScoped<IOutboxConsumer, LoyaltyBookingConsumer>();
    }

    public override void MapEndpoints(IEndpointRouteBuilder api) => LoyaltyEndpoints.Map(api);
}
