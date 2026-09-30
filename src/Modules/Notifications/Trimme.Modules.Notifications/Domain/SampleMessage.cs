using Trimme.BuildingBlocks.Application.Notifications;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.Modules.Notifications.Domain;

/// <summary>
/// Fixed sample data for template previews and test sends (R-NTF-08): no real booking, customer, shop or number is ever
/// used. The booking is tomorrow at 17:30 in Riyadh.
/// </summary>
public static class SampleMessage
{
    public static (NotifiableBooking Booking, ShopSummary Shop) For(string locale, DateTimeOffset now)
    {
        var arabic = MessageFormat.IsArabic(locale);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");
        var tomorrow = TimeZoneInfo.ConvertTime(now, zone).Date.AddDays(1).AddHours(17).AddMinutes(30);
        var start = new DateTimeOffset(tomorrow, zone.GetUtcOffset(tomorrow));
        var shopId = new ShopId(Guid.Empty);
        var booking = new NotifiableBooking(
            Guid.Empty, shopId, null, arabic ? "سارة العتيبي" : "Sara Alotaibi", "TRM4K7QZ", new ProfessionalId(Guid.Empty),
            "ماجد الحربي", "Majed Alharbi", "قص شعر وتصفيف", "Haircut and styling", 85m, "SAR", 45, start, start.AddMinutes(45), "Confirmed", "Online");
        var shop = new ShopSummary(
            shopId, "sample-shop", "صالون الأصالة", "Al Asala Salon", ShopStatus.Active, "Asia/Riyadh", null, false,
            arabic ? "طريق الأمير محمد بن عبدالعزيز، حي العليا، الرياض" : "Prince Mohammed bin Abdulaziz Rd, Al Olaya, Riyadh");
        return (booking, shop);
    }
}
