using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Trimme.BuildingBlocks.Application.Bookings;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Scheduling;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.BuildingBlocks.Web.Modules;
using Trimme.Modules.Bookings.Api;
using Trimme.Modules.Bookings.Application;
using Trimme.Modules.Bookings.Application.Admin;
using Trimme.Modules.Bookings.Application.Customer;
using Trimme.Modules.Bookings.Infrastructure;
using Trimme.Modules.Bookings.Infrastructure.Seeding;

namespace Trimme.Modules.Bookings;

/// <summary>
/// Bookings module (schema <c>bookings</c>, Phase 10): online bookings, walk-ins, the state machine with history,
/// reschedule and cancel, idempotent commands, outbox events and the exclusion constraint that makes double booking
/// impossible (spec §11, D-085…D-089).
/// </summary>
public sealed class BookingsModule : ModuleBase
{
    public override string Name => "Bookings";

    public override string Schema => "bookings";

    public override void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        base.AddServices(services, configuration);
        services.AddScoped<IdempotencyGate>();
        services.AddScoped<CustomerBookingSupport>();
        services.AddScoped<ProfessionalPicker>();
        services.AddScoped<ShopBookingReader>();
        services.AddScoped<AdminBookingMapper>();

        // Replaces the Availability module's stand-in (registered with TryAdd before this module).
        services.Replace(ServiceDescriptor.Scoped<IBookedTimeReader, BookedTimeReader>());
        services.AddScoped<IShopServiceUsage, BookingServiceUsage>();
        services.AddScoped<IBookingReviewSource, BookingReviewSource>();
        services.AddSingleton<IDevSeeder, DemoBookingsSeeder>();
    }

    public override void MapEndpoints(IEndpointRouteBuilder api) => BookingEndpoints.Map(api);
}
