using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trimme.BuildingBlocks.Application.Privacy;
using Trimme.BuildingBlocks.Web.Modules;
using Trimme.Modules.Customers.Api;
using Trimme.Modules.Customers.Application.Customer;

namespace Trimme.Modules.Customers;

/// <summary>
/// Customers module (schema <c>customers</c>): the customer's own saved shops and professionals (R-CUS-10, D-098). The
/// customer account itself lives in Identity; bookings and reviews in their modules.
/// </summary>
public sealed class CustomersModule : ModuleBase
{
    public override string Name => "Customers";

    public override string Schema => "customers";

    public override void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        base.AddServices(services, configuration);
        services.AddScoped<ICustomerDataEraser, FavoritesEraser>();
    }

    public override void MapEndpoints(IEndpointRouteBuilder api) => FavoriteEndpoints.Map(api);
}
