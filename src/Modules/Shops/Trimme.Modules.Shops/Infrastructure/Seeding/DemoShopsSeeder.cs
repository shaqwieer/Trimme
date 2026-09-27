using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.Modules.Shops.Domain;

namespace Trimme.Modules.Shops.Infrastructure.Seeding;

/// <summary>
/// Active Riyadh demo shops with fixed ids, a public profile and a map location (idempotent; development only): the two
/// main shops, and two extra ones without users that only illustrate subscription statuses (Phase 08).
/// </summary>
internal sealed class DemoShopsSeeder : IDevSeeder
{
    private static readonly DateTimeOffset SeededAt = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly Dictionary<string, DemoProfile> Profiles = new(StringComparer.Ordinal)
    {
        [DemoData.AlAsala.Slug] = new(
            "صالون رجالي متخصص في الحلاقة الكلاسيكية والتدريج وتهذيب اللحية، في قلب حي الملقا.",
            "A men's salon for classic cuts, fades and beard grooming in the heart of Al Malqa.",
            "+966114567890",
            [ShopAmenity.Parking, ShopAmenity.WiFi, ShopAmenity.WaitingArea, ShopAmenity.PrayerArea],
            Verified: true,
            24.8123, 46.6011, "طريق أنس بن مالك", "الملقا", "الرياض"),
        [DemoData.BarberHouse.Slug] = new(
            "حلاقة عصرية للرجال والأطفال في حطين، مع مواعيد دقيقة وأجواء هادئة.",
            "Modern cuts for men and kids in Hittin, on time and in a calm setting.",
            "+966112345678",
            [ShopAmenity.Parking, ShopAmenity.KidsFriendly, ShopAmenity.WheelchairAccessible],
            Verified: false,
            24.7630, 46.6010, "طريق الأمير تركي", "حطين", "الرياض"),
    };

    public int Order => 200;

    public string Name => "shops-demo";

    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<TrimmeDbContext>();
        foreach (var demo in DemoData.Shops)
        {
            var shop = await db.Set<Shop>().SingleOrDefaultAsync(s => s.Id == demo.Id, cancellationToken);
            if (shop is null)
            {
                shop = Shop.Create(demo.Id, demo.Slug, demo.NameAr, demo.NameEn, Shop.DefaultTimeZone, SeededAt).Value;
                shop.Activate(SeededAt);
                db.Add(shop);
            }

            // Phase 06 profile: applied once (shops seeded by Phase 05 have no location yet), never over later edits.
            if (shop.Location is null && Profiles.TryGetValue(demo.Slug, out var profile))
            {
                shop.UpdateProfile(
                    ShopProfile.Create(demo.NameAr, demo.NameEn, profile.DescriptionAr, profile.DescriptionEn, ShopCategory.Barbershop, profile.Phone, profile.Amenities),
                    SeededAt);
                shop.SetVerified(profile.Verified, SeededAt);
                shop.SetLocation(
                    ShopLocation.Create(
                        profile.Latitude, profile.Longitude, profile.Street, profile.District, profile.City,
                        $"{profile.Street}، حي {profile.District}، {profile.City}", LocationSource.Manual, SeededAt, confirmedBy: null).Value,
                    SeededAt);
            }
        }

        foreach (var extra in DemoData.ExtraShops)
        {
            if (await db.Set<Shop>().AnyAsync(s => s.Id == extra.Id, cancellationToken))
            {
                continue;
            }

            var shop = Shop.Create(extra.Id, extra.Slug, extra.NameAr, extra.NameEn, Shop.DefaultTimeZone, SeededAt).Value;
            shop.Activate(SeededAt);
            shop.SetLocation(
                ShopLocation.Create(
                    extra.Latitude, extra.Longitude, null, extra.District, "الرياض", $"حي {extra.District}، الرياض", LocationSource.Manual, SeededAt, confirmedBy: null).Value,
                SeededAt);
            db.Add(shop);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private sealed record DemoProfile(
        string DescriptionAr,
        string DescriptionEn,
        string Phone,
        ShopAmenity[] Amenities,
        bool Verified,
        double Latitude,
        double Longitude,
        string Street,
        string District,
        string City);
}
