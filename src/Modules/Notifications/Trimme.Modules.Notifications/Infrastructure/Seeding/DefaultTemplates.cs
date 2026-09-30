using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.Modules.Notifications.Domain;

namespace Trimme.Modules.Notifications.Infrastructure.Seeding;

/// <summary>
/// The starting wording of every template slot (spec §16, D-109): customers get five events and professionals four, in
/// Arabic and English. <c>migrate</c> creates a missing slot with this text as its active version 1 and never touches an
/// existing slot, so admin edits always win. This is the only place with message text; jobs and handlers render templates
/// from the database (R-NEG-09). Production must register matching approved templates with Meta.
/// </summary>
internal sealed class DefaultTemplatesSynchronizer : IReferenceDataSynchronizer
{
    public int Order => 400;

    public string Name => "WhatsApp templates";

    public async Task SynchronizeAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<TrimmeDbContext>();
        var existing = await db.Set<WhatsAppTemplate>().AsNoTracking()
            .Select(t => new { t.Event, t.Audience, t.Locale })
            .ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var (key, content) in DefaultTemplates.All)
        {
            if (existing.Any(e => e.Event == key.Event && e.Audience == key.Audience && e.Locale == key.Locale))
            {
                continue;
            }

            db.Add(WhatsAppTemplate.CreateWithActiveVersion(
                WhatsAppTemplateId.From(DefaultTemplates.StableId($"template:{key.Event}:{key.Audience}:{key.Locale}")),
                TemplateVersionId.From(DefaultTemplates.StableId($"template:{key.Event}:{key.Audience}:{key.Locale}:v1")),
                key.Event, key.Audience, key.Locale, content, now));
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

internal readonly record struct TemplateKey(MessageEvent Event, MessageAudience Audience, string Locale);

internal static class DefaultTemplates
{
    private static readonly TemplateButton ManageAr = new("إدارة الموعد", TemplateButtonTarget.ManageBooking);
    private static readonly TemplateButton ManageEn = new("Manage booking", TemplateButtonTarget.ManageBooking);
    private static readonly TemplateButton DirectionsAr = new("الاتجاهات", TemplateButtonTarget.ShopPage);
    private static readonly TemplateButton DirectionsEn = new("Directions", TemplateButtonTarget.ShopPage);

    public static IReadOnlyList<(TemplateKey Key, TemplateContent Content)> All { get; } =
    [
        Customer(MessageEvent.BookingConfirmed, "ar",
            "مرحباً {{customer_name}}، تم تأكيد حجزك في {{shop_name}}.\n"
            + "الخدمة: {{service_name}} مع {{professional_name}}\n"
            + "الموعد: {{booking_date}} الساعة {{booking_time}} ({{duration}})\n"
            + "المبلغ: {{amount}}، يُدفع في المحل\n"
            + "العنوان: {{address}}\n"
            + "رقم الحجز: {{booking_reference}}",
            ManageAr, DirectionsAr),
        Customer(MessageEvent.BookingConfirmed, "en",
            "Hi {{customer_name}}, your booking at {{shop_name}} is confirmed.\n"
            + "Service: {{service_name}} with {{professional_name}}\n"
            + "When: {{booking_date}} at {{booking_time}} ({{duration}})\n"
            + "Amount: {{amount}}, paid at the shop\n"
            + "Address: {{address}}\n"
            + "Booking reference: {{booking_reference}}",
            ManageEn, DirectionsEn),
        Customer(MessageEvent.BookingPending, "ar",
            "مرحباً {{customer_name}}، استلمنا طلب حجزك في {{shop_name}} وهو بانتظار تأكيد المحل.\n"
            + "الخدمة: {{service_name}} مع {{professional_name}}\n"
            + "الموعد: {{booking_date}} الساعة {{booking_time}}\n"
            + "سنرسل لك رسالة عند التأكيد. رقم الحجز: {{booking_reference}}",
            ManageAr),
        Customer(MessageEvent.BookingPending, "en",
            "Hi {{customer_name}}, we received your booking request at {{shop_name}}; the shop will confirm it shortly.\n"
            + "Service: {{service_name}} with {{professional_name}}\n"
            + "When: {{booking_date}} at {{booking_time}}\n"
            + "We will message you once it is confirmed. Booking reference: {{booking_reference}}",
            ManageEn),
        Customer(MessageEvent.BookingRescheduled, "ar",
            "مرحباً {{customer_name}}، تم تعديل موعدك في {{shop_name}}.\n"
            + "الموعد الجديد: {{booking_date}} الساعة {{booking_time}}\n"
            + "الخدمة: {{service_name}} مع {{professional_name}}\n"
            + "رقم الحجز: {{booking_reference}}",
            ManageAr),
        Customer(MessageEvent.BookingRescheduled, "en",
            "Hi {{customer_name}}, your booking at {{shop_name}} has been moved.\n"
            + "New time: {{booking_date}} at {{booking_time}}\n"
            + "Service: {{service_name}} with {{professional_name}}\n"
            + "Booking reference: {{booking_reference}}",
            ManageEn),
        Customer(MessageEvent.BookingCancelled, "ar",
            "مرحباً {{customer_name}}، تم إلغاء حجزك في {{shop_name}} يوم {{booking_date}} الساعة {{booking_time}} ({{service_name}}).\n"
            + "رقم الحجز: {{booking_reference}}. يسعدنا حجزك من جديد في أي وقت.",
            new TemplateButton("احجز من جديد", TemplateButtonTarget.ShopPage)),
        Customer(MessageEvent.BookingCancelled, "en",
            "Hi {{customer_name}}, your booking at {{shop_name}} on {{booking_date}} at {{booking_time}} ({{service_name}}) has been cancelled.\n"
            + "Booking reference: {{booking_reference}}. You are welcome to book again any time.",
            new TemplateButton("Book again", TemplateButtonTarget.ShopPage)),
        Customer(MessageEvent.BookingReminder, "ar",
            "تذكير: موعدك في {{shop_name}} بعد {{time_remaining}}، الساعة {{booking_time}}.\n"
            + "الخدمة: {{service_name}} مع {{professional_name}}\n"
            + "العنوان: {{address}}",
            DirectionsAr, ManageAr),
        Customer(MessageEvent.BookingReminder, "en",
            "Reminder: your appointment at {{shop_name}} is in {{time_remaining}}, at {{booking_time}}.\n"
            + "Service: {{service_name}} with {{professional_name}}\n"
            + "Address: {{address}}",
            DirectionsEn, ManageEn),
        Professional(MessageEvent.BookingConfirmed, "ar",
            "لديك حجز جديد مع {{customer_name}} يوم {{booking_date}} الساعة {{booking_time}}.\n"
            + "الخدمة: {{service_name}} ({{duration}})\n"
            + "المحل: {{shop_name}} · رقم الحجز: {{booking_reference}}"),
        Professional(MessageEvent.BookingConfirmed, "en",
            "You have a new booking with {{customer_name}} on {{booking_date}} at {{booking_time}}.\n"
            + "Service: {{service_name}} ({{duration}})\n"
            + "Shop: {{shop_name}} · Booking reference: {{booking_reference}}"),
        Professional(MessageEvent.BookingRescheduled, "ar",
            "تم تعديل حجز {{customer_name}}: الموعد الجديد {{booking_date}} الساعة {{booking_time}}.\n"
            + "الخدمة: {{service_name}} · {{shop_name}}"),
        Professional(MessageEvent.BookingRescheduled, "en",
            "{{customer_name}}'s booking has moved: new time {{booking_date}} at {{booking_time}}.\n"
            + "Service: {{service_name}} · {{shop_name}}"),
        Professional(MessageEvent.BookingCancelled, "ar",
            "تم إلغاء حجز {{customer_name}} يوم {{booking_date}} الساعة {{booking_time}} ({{service_name}}) في {{shop_name}}."),
        Professional(MessageEvent.BookingCancelled, "en",
            "{{customer_name}}'s booking on {{booking_date}} at {{booking_time}} ({{service_name}}) at {{shop_name}} has been cancelled."),
        Professional(MessageEvent.BookingReminder, "ar",
            "لديك حجز مع {{customer_name}} بعد {{time_remaining}}، الساعة {{booking_time}}.\n"
            + "الخدمة: {{service_name}} ({{duration}})"),
        Professional(MessageEvent.BookingReminder, "en",
            "You have a booking with {{customer_name}} in {{time_remaining}}, at {{booking_time}}.\n"
            + "Service: {{service_name}} ({{duration}})"),
    ];

    /// <summary>A deterministic id from a name, so every environment gets the same template ids.</summary>
    public static Guid StableId(string name)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"trimme:{name}"))[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }

    private static (TemplateKey, TemplateContent) Customer(MessageEvent @event, string locale, string body, params TemplateButton[] buttons) =>
        (new TemplateKey(@event, MessageAudience.Customer, locale), new TemplateContent(body, buttons, ProviderName(@event, MessageAudience.Customer)));

    private static (TemplateKey, TemplateContent) Professional(MessageEvent @event, string locale, string body) =>
        (new TemplateKey(@event, MessageAudience.Professional, locale), new TemplateContent(body, [], ProviderName(@event, MessageAudience.Professional)));

    /// <summary>For example <c>trimme_customer_booking_confirmed</c> (one Meta template per event and audience, one language each).</summary>
    private static string ProviderName(MessageEvent @event, MessageAudience audience) =>
        $"trimme_{audience.ToString().ToLowerInvariant()}_{string.Concat(@event.ToString().Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()))}";
}
