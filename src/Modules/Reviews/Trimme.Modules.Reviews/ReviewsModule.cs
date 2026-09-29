using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trimme.BuildingBlocks.Application.Bookings;
using Trimme.BuildingBlocks.Application.Discovery;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.BuildingBlocks.Web.Modules;
using Trimme.Modules.Reviews.Api;
using Trimme.Modules.Reviews.Infrastructure;
using Trimme.Modules.Reviews.Infrastructure.Seeding;

namespace Trimme.Modules.Reviews;

/// <summary>
/// Reviews module (schema <c>reviews</c>): customers' reviews of completed bookings and the rating aggregates of shops and
/// professionals (D-017, D-092). Phase 11 builds the read side, the aggregates and the demo reviews; customers write
/// reviews from Phase 12 and admins moderate them in Phase 14.
/// </summary>
public sealed class ReviewsModule : ModuleBase
{
    public override string Name => "Reviews";

    public override string Schema => "reviews";

    public override void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        base.AddServices(services, configuration);
        services.AddScoped<IRatingReader, RatingReader>();
        services.AddScoped<Application.Admin.AdminReviewMapper>();
        services.AddScoped<IReviewLookup, Application.Customer.ReviewLookup>();
        services.AddSingleton<IDevSeeder, DemoReviewsSeeder>();
    }

    public override void MapEndpoints(IEndpointRouteBuilder api) => ReviewEndpoints.Map(api);
}
