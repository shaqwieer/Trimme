using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Trimme.BuildingBlocks.Application.Notifications;
using Trimme.BuildingBlocks.Application.Privacy;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Privacy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.Modules.Notifications.Application;
using Trimme.Modules.Notifications.Domain;

namespace Trimme.Modules.Notifications.Infrastructure.Seeding;

/// <summary>
/// Demo notification history (spec §20): the confirmation dispatches of the completed demo visits (customer and
/// professional, delivered or read, one failed) and a few in-app notifications. Rows are written as history: nothing is
/// sent, no job is scheduled and no outbox message is written. Idempotent by fixed ids; development only.
/// </summary>
internal sealed class DemoNotificationsSeeder : IDevSeeder
{
    /// <summary>After bookings (400) and reviews.</summary>
    public int Order => 600;

    public string Name => "notifications-demo";

    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<TrimmeDbContext>();
        var bookings = services.GetRequiredService<IBookingNotificationSource>();
        var shops = services.GetRequiredService<IShopDirectory>();
        var protector = services.GetRequiredService<IPersonalDataProtector>();
        var links = services.GetRequiredService<IOptions<MessageLinkOptions>>().Value.PublicBaseUrl;
        using var scope = services.GetRequiredService<ISystemDataScope>().Begin();

        var templates = await db.Set<WhatsAppTemplate>().Include(t => t.Versions)
            .Where(t => t.Event == MessageEvent.BookingConfirmed && t.Locale == "ar").ToListAsync(cancellationToken);
        var index = 0;
        foreach (var visit in DemoVisits.All)
        {
            if (await bookings.FindAsync(visit.BookingId, cancellationToken) is not { } booking || await shops.FindAsync(booking.ShopId, cancellationToken) is not { } shop)
            {
                continue;
            }

            var professionalNumber = DemoData.Professionals.FirstOrDefault(p => p.Id == visit.ProfessionalId)?.WhatsApp;
            foreach (var (audience, number, recipientId) in new[]
                     {
                         (MessageAudience.Customer, (string?)visit.Customer.Mobile, visit.Customer.Id),
                         (MessageAudience.Professional, professionalNumber, visit.ProfessionalId),
                     })
            {
                var id = DispatchId.From(DefaultTemplates.StableId($"demo-dispatch:{visit.BookingId}:{audience}"));
                if (number is null || !PhoneNumber.TryParse(number, out var phone)
                    || templates.FirstOrDefault(t => t.Audience == audience) is not { ActiveVersion: { } version } template
                    || await db.Set<WhatsAppDispatch>().AnyAsync(d => d.Id == id, cancellationToken))
                {
                    continue;
                }

                var message = RenderedMessage.From(
                    version.Content,
                    MessageComposer.Values(booking, shop, audience, "ar", 30, links),
                    MessageComposer.Urls(booking, shop, audience, "ar", links));
                var failed = index++ == 3;
                db.Add(WhatsAppDispatch.Seeded(
                    id, $"demo:{visit.BookingId:N}:{audience}", booking.Id, booking.ShopId, template, version,
                    protector.Protect(phone.E164, NotificationPurposes.DispatchRecipient), phone.Masked, recipientId, message,
                    failed ? DispatchStatus.Failed : audience == MessageAudience.Customer ? DispatchStatus.Read : DispatchStatus.Delivered,
                    failed ? "fake.transient: simulated temporary failure" : null,
                    booking.StartsAt.AddDays(-2)));
            }
        }

        if (await bookings.FindAsync(DemoReviewableVisits.UpcomingBookingId, cancellationToken) is { } upcoming)
        {
            var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["reference"] = upcoming.Reference,
                ["itemNameAr"] = upcoming.ItemNameAr,
                ["itemNameEn"] = upcoming.ItemNameEn ?? upcoming.ItemNameAr,
                ["professionalNameAr"] = upcoming.ProfessionalNameAr,
                ["professionalNameEn"] = upcoming.ProfessionalNameEn,
                ["startsAt"] = upcoming.StartsAt.ToString("O", CultureInfo.InvariantCulture),
                ["status"] = upcoming.Status,
                ["actor"] = "Customer",
                ["customerName"] = upcoming.CustomerName,
            };
            const string dedupe = "demo:upcoming";
            if (!await db.Set<ShopNotification>().AnyAsync(n => n.ShopId == upcoming.ShopId && n.DedupeKey == dedupe, cancellationToken))
            {
                db.Add(ShopNotification.Create(upcoming.ShopId, NoticeKinds.BookingCreated, dedupe, parameters, upcoming.Id, upcoming.StartsAt.AddDays(-1)));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
