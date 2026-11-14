using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.Modules.Notifications.Domain;

namespace Trimme.Modules.Notifications.Infrastructure.Seeding;

/// <summary>
/// The starting wording of every template slot (spec §16, D-109, D-145): customers get six events and professionals four, in
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
            "مرحباً {{customer_name}}، تم تأكيد حجزك في {{shop_name}} بنجاح.\n"
            + "حجزت خدمة {{service_name}} مع {{professional_name}} يوم {{booking_date}} الساعة {{booking_time}}، ومدتها {{duration}} تقريباً.\n"
            + "المبلغ المطلوب {{amount}}، ويُدفع في المحل يوم الموعد.\n"
            + "عنوان المحل: {{address}}.\n"
            + "رقم حجزك هو {{booking_reference}}، ويمكنك إدارة الحجز من الزر أدناه. نتطلع لرؤيتك!",
            ManageAr, DirectionsAr),
        Customer(MessageEvent.BookingConfirmed, "en",
            "Hi {{customer_name}}, your booking at {{shop_name}} is confirmed.\n"
            + "You booked {{service_name}} with {{professional_name}} on {{booking_date}} at {{booking_time}}; it takes about {{duration}}.\n"
            + "The price is {{amount}}, paid at the shop on the day.\n"
            + "The shop's address is {{address}}.\n"
            + "Your booking reference is {{booking_reference}}. You can manage your booking with the button below.",
            ManageEn, DirectionsEn),
        Customer(MessageEvent.BookingPending, "ar",
            "مرحباً {{customer_name}}، استلمنا طلب حجزك في {{shop_name}}، وهو الآن بانتظار تأكيد المحل.\n"
            + "طلبت خدمة {{service_name}} مع {{professional_name}} يوم {{booking_date}} الساعة {{booking_time}}.\n"
            + "رقم حجزك هو {{booking_reference}}، وسنرسل لك رسالة فور تأكيد المحل للحجز.",
            ManageAr),
        Customer(MessageEvent.BookingPending, "en",
            "Hi {{customer_name}}, we received your booking request at {{shop_name}}, and it is waiting for the shop to confirm it.\n"
            + "You asked for {{service_name}} with {{professional_name}} on {{booking_date}} at {{booking_time}}.\n"
            + "Your booking reference is {{booking_reference}}. We will message you again as soon as the shop confirms.",
            ManageEn),
        Customer(MessageEvent.BookingRescheduled, "ar",
            "مرحباً {{customer_name}}، تم تغيير موعد حجزك في {{shop_name}} إلى وقت جديد.\n"
            + "موعدك الجديد يوم {{booking_date}} الساعة {{booking_time}}، لخدمة {{service_name}} مع {{professional_name}}.\n"
            + "رقم حجزك ما زال {{booking_reference}}، ويمكنك إدارة الحجز من الزر أدناه.",
            ManageAr),
        Customer(MessageEvent.BookingRescheduled, "en",
            "Hi {{customer_name}}, your booking at {{shop_name}} has been moved to a new time.\n"
            + "Your new appointment is on {{booking_date}} at {{booking_time}}, for {{service_name}} with {{professional_name}}.\n"
            + "Your booking reference is still {{booking_reference}}. You can manage your booking with the button below.",
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
            + "حجزت خدمة {{service_name}} مع {{professional_name}}.\n"
            + "عنوان المحل: {{address}}. استخدم الأزرار أدناه لمعرفة الاتجاهات أو لإدارة حجزك. نتمنى لك تجربة ممتعة.",
            DirectionsAr, ManageAr),
        Customer(MessageEvent.BookingReminder, "en",
            "Reminder: your appointment at {{shop_name}} starts in {{time_remaining}}, at {{booking_time}}.\n"
            + "You booked {{service_name}} with {{professional_name}}.\n"
            + "The shop's address is {{address}}. Use the buttons below for directions or to manage your booking. Enjoy your visit.",
            DirectionsEn, ManageEn),
        Customer(MessageEvent.BookingProfessionalChanged, "ar",
            "مرحباً {{customer_name}}، نود إعلامك بأن {{shop_name}} نقل حجزك إلى {{professional_name}} في نفس الموعد.\n"
            + "موعدك يوم {{booking_date}} الساعة {{booking_time}} لخدمة {{service_name}}، ورقم حجزك {{booking_reference}}.\n"
            + "إن لم يناسبك ذلك يمكنك تغيير الموعد أو إلغاؤه من الزر أدناه حتى بدء الموعد.",
            ManageAr),
        Customer(MessageEvent.BookingProfessionalChanged, "en",
            "Hi {{customer_name}}, {{shop_name}} has given your booking to {{professional_name}}, at the same time.\n"
            + "Your appointment is on {{booking_date}} at {{booking_time}} for {{service_name}}; your booking reference is {{booking_reference}}.\n"
            + "If this does not suit you, you can change or cancel it with the button below until it starts.",
            ManageEn),
        Professional(MessageEvent.BookingConfirmed, "ar",
            "لديك حجز جديد باسم {{customer_name}} يوم {{booking_date}} الساعة {{booking_time}}.\n"
            + "الخدمة المطلوبة هي {{service_name}}، ومدتها {{duration}} تقريباً.\n"
            + "الحجز في {{shop_name}}، ورقم الحجز {{booking_reference}}. يرجى الاستعداد للموعد في وقته المحدد. شكراً لك."),
        Professional(MessageEvent.BookingConfirmed, "en",
            "You have a new booking with {{customer_name}} on {{booking_date}} at {{booking_time}}.\n"
            + "The service is {{service_name}}, and it takes about {{duration}}.\n"
            + "The booking is at {{shop_name}}, and its reference is {{booking_reference}}. Please be ready on time. Thank you."),
        Professional(MessageEvent.BookingRescheduled, "ar",
            "تنبيه: تم تغيير موعد الحجز باسم {{customer_name}} إلى وقت جديد.\n"
            + "الموعد الجديد يوم {{booking_date}} الساعة {{booking_time}}، لخدمة {{service_name}} في {{shop_name}}. يرجى مراجعة جدولك."),
        Professional(MessageEvent.BookingRescheduled, "en",
            "Heads up: the booking for {{customer_name}} has moved to a new time.\n"
            + "The new time is {{booking_date}} at {{booking_time}}, for {{service_name}} at {{shop_name}}. Please check your schedule."),
        Professional(MessageEvent.BookingCancelled, "ar",
            "تنبيه: نود إعلامك بأنه تم إلغاء الحجز باسم {{customer_name}} يوم {{booking_date}} الساعة {{booking_time}} لخدمة {{service_name}} في {{shop_name}}.\n"
            + "أصبح هذا الوقت متاحاً الآن في جدولك."),
        Professional(MessageEvent.BookingCancelled, "en",
            "Heads up: the booking for {{customer_name}} on {{booking_date}} at {{booking_time}} ({{service_name}}) at {{shop_name}} has been cancelled.\n"
            + "That time is now free in your schedule."),
        Professional(MessageEvent.BookingReminder, "ar",
            "تذكير: لديك حجز باسم {{customer_name}} بعد {{time_remaining}}، الساعة {{booking_time}}.\n"
            + "الخدمة المطلوبة هي {{service_name}}، ومدتها {{duration}} تقريباً. يرجى الاستعداد للموعد في وقته المحدد."),
        Professional(MessageEvent.BookingReminder, "en",
            "Reminder: you have a booking with {{customer_name}} in {{time_remaining}}, at {{booking_time}}.\n"
            + "The service is {{service_name}}, and it takes about {{duration}}. Please be ready on time."),
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
