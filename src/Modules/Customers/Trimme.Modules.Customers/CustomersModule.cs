using Microsoft.AspNetCore.Routing;
using Trimme.BuildingBlocks.Web.Modules;
using Trimme.Modules.Customers.Api;

namespace Trimme.Modules.Customers;

/// <summary>
/// Customers module (schema <c>customers</c>): the customer's own saved shops and professionals (R-CUS-10, D-098). The
/// customer account itself lives in Identity; bookings and reviews in their modules.
/// </summary>
public sealed class CustomersModule : ModuleBase
{
    public override string Name => "Customers";

    public override string Schema => "customers";

    public override void MapEndpoints(IEndpointRouteBuilder api) => FavoriteEndpoints.Map(api);
}
