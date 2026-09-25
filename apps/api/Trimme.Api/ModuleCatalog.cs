using Trimme.BuildingBlocks.Web.Modules;
using Trimme.Modules.Administration;
using Trimme.Modules.Availability;
using Trimme.Modules.Bookings;
using Trimme.Modules.Customers;
using Trimme.Modules.Identity;
using Trimme.Modules.Notifications;
using Trimme.Modules.Professionals;
using Trimme.Modules.QrAnalytics;
using Trimme.Modules.Reviews;
using Trimme.Modules.Services;
using Trimme.Modules.Shops;
using Trimme.Modules.Subscriptions;

namespace Trimme.Api;

/// <summary>Explicit list of feature modules composed into the API (D-038). Order is not significant.</summary>
public static class ModuleCatalog
{
    public static IReadOnlyList<IModule> All { get; } =
    [
        new IdentityModule(),
        new CustomersModule(),
        new ShopsModule(),
        new ProfessionalsModule(),
        new ServicesModule(),
        new AvailabilityModule(),
        new BookingsModule(),
        new ReviewsModule(),
        new SubscriptionsModule(),
        new NotificationsModule(),
        new QrAnalyticsModule(),
        new AdministrationModule(),
    ];
}
