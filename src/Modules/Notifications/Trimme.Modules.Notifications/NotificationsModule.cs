using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Notifications;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.BuildingBlocks.Web.Jobs;
using Trimme.BuildingBlocks.Web.Modules;
using Trimme.Modules.Notifications.Api;
using Trimme.Modules.Notifications.Application;
using Trimme.Modules.Notifications.Application.Admin;
using Trimme.Modules.Notifications.Infrastructure;
using Trimme.Modules.Notifications.Infrastructure.Seeding;
using Trimme.Modules.Notifications.Jobs;

namespace Trimme.Modules.Notifications;

/// <summary>
/// Notifications module (schema <c>notifications</c>, Phase 15): versioned WhatsApp templates per event, audience and
/// locale; dispatches rendered from them for booking lifecycle events and reminders through a provider abstraction (fake
/// locally, Meta Cloud API in production); in-app notification centres for shops, customers and admins (spec §16, §17;
/// D-108…D-112).
/// </summary>
public sealed class NotificationsModule : ModuleBase
{
    public override string Name => "Notifications";

    public override string Schema => "notifications";

    public override void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        base.AddServices(services, configuration);
        services.Configure<MessageLinkOptions>(configuration.GetSection(MessageLinkOptions.SectionName));
        services.Configure<NotificationsOptions>(configuration.GetSection(NotificationsOptions.SectionName));
        services.AddOptions<WhatsAppOptions>()
            .Bind(configuration.GetSection(WhatsAppOptions.SectionName))
            .Validate<IHostEnvironment>(
                (options, environment) => options.Provider != WhatsAppProviderKind.Fake || WhatsAppOptions.IsLocal(environment),
                "WhatsApp:Provider=Fake is only allowed in Development and Testing.")
            .ValidateOnStart();

        services.AddSingleton<FakeWhatsAppInbox>();
        services.AddHttpClient<MetaCloudApiProvider>(client => client.Timeout = TimeSpan.FromSeconds(15));
        services.AddScoped<IWhatsAppProvider>(sp =>
            sp.GetRequiredService<IOptions<WhatsAppOptions>>().Value.Resolve(sp.GetRequiredService<IHostEnvironment>()) switch
            {
                WhatsAppProviderKind.Fake => ActivatorUtilities.CreateInstance<FakeWhatsAppProvider>(sp),
                WhatsAppProviderKind.Meta => sp.GetRequiredService<MetaCloudApiProvider>(),
                _ => new UnconfiguredWhatsAppProvider(),
            });
        services.AddScoped<IWhatsAppAuthenticationSender, WhatsAppAuthenticationSender>();

        services.AddScoped<DispatchSender>();
        services.AddScoped<DispatchFactory>();
        services.AddScoped<TemplateMapper>();
        services.AddScoped<INotificationCenter, NotificationCenter>();
        services.AddScoped<IOutboxConsumer, BookingNotificationsConsumer>();
        services.AddScoped<SendDispatchJob>();
        services.AddScoped<BookingReminderJob>();
        services.AddScoped<ProfessionalDeliveryJob>();
        services.AddRecurringJob<NotificationSweepJob>("notifications-sweep", "*/5 * * * *");
        services.AddRecurringJob<DispatchRetentionJob>("notifications-retention", "15 4 * * *");

        services.AddSingleton<IReferenceDataSynchronizer, DefaultTemplatesSynchronizer>();
        services.AddSingleton<IDevSeeder, DemoNotificationsSeeder>();
    }

    public override void MapEndpoints(IEndpointRouteBuilder api) => NotificationEndpoints.Map(api);
}
