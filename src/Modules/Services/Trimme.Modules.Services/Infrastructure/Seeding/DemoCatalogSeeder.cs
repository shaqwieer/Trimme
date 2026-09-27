using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.Modules.Services.Domain;

namespace Trimme.Modules.Services.Infrastructure.Seeding;

/// <summary>
/// Categories, then different services, prices and durations per demo shop, a package each, and assignments
/// (spec §20). Idempotent by fixed ids; development only.
/// </summary>
internal sealed class DemoCatalogSeeder : IDevSeeder
{
    private static readonly DateTimeOffset SeededAt = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);

    private static readonly (Guid Id, string Ar, string En, string Icon)[] Categories =
    [
        (Guid.Parse("0199a0de-5a10-7000-8000-000000000301"), "قص الشعر", "Haircuts", "scissors"),
        (Guid.Parse("0199a0de-5a10-7000-8000-000000000302"), "اللحية", "Beard", "user"),
        (Guid.Parse("0199a0de-5a10-7000-8000-000000000303"), "العناية", "Grooming & care", "heart"),
        (Guid.Parse("0199a0de-5a10-7000-8000-000000000304"), "الأطفال", "Kids", "users"),
        (Guid.Parse("0199a0de-5a10-7000-8000-000000000305"), "الصبغة", "Coloring", "layers"),
    ];

    private static readonly DemoService[] Services =
    [
        new(Guid.Parse("0199a0de-5a10-7000-8000-000000000401"), DemoData.AlAsala.Id, 0, "حلاقة شعر", "Haircut", "قص وتصفيف مع غسيل", 60m, 30, true),
        new(Guid.Parse("0199a0de-5a10-7000-8000-000000000402"), DemoData.AlAsala.Id, 1, "تهذيب لحية", "Beard trim", null, 35m, 20, true),
        new(Guid.Parse("0199a0de-5a10-7000-8000-000000000403"), DemoData.AlAsala.Id, 3, "حلاقة أطفال", "Kids' haircut", "للأعمار حتى ١٢ سنة", 45m, 25, true),
        new(Guid.Parse("0199a0de-5a10-7000-8000-000000000404"), DemoData.AlAsala.Id, 2, "عناية بالوجه", "Facial care", null, 70m, 40, true),
        new(Guid.Parse("0199a0de-5a10-7000-8000-000000000405"), DemoData.AlAsala.Id, 4, "صبغة شعر", "Hair coloring", null, 120m, 60, false),
        new(Guid.Parse("0199a0de-5a10-7000-8000-000000000411"), DemoData.BarberHouse.Id, 0, "قص وتصفيف", "Cut & style", null, 55m, 30, true),
        new(Guid.Parse("0199a0de-5a10-7000-8000-000000000412"), DemoData.BarberHouse.Id, 1, "تحديد لحية", "Beard line-up", null, 30m, 15, true),
        new(Guid.Parse("0199a0de-5a10-7000-8000-000000000413"), DemoData.BarberHouse.Id, 3, "حلاقة أطفال", "Kids' haircut", null, 40m, 20, true),
        new(Guid.Parse("0199a0de-5a10-7000-8000-000000000414"), DemoData.BarberHouse.Id, 2, "تنظيف بشرة", "Skin cleansing", null, 90m, 45, true),
    ];

    private static readonly (Guid Id, ShopId Shop, string Ar, string En, decimal Price, int Duration, Guid[] Items)[] Packages =
    [
        (Guid.Parse("0199a0de-5a10-7000-8000-000000000501"), DemoData.AlAsala.Id, "باقة شعر ولحية", "Hair & beard", 85m, 50,
            [Guid.Parse("0199a0de-5a10-7000-8000-000000000401"), Guid.Parse("0199a0de-5a10-7000-8000-000000000402")]),
        (Guid.Parse("0199a0de-5a10-7000-8000-000000000511"), DemoData.BarberHouse.Id, "باقة القص واللحية", "Cut & beard", 75m, 45,
            [Guid.Parse("0199a0de-5a10-7000-8000-000000000411"), Guid.Parse("0199a0de-5a10-7000-8000-000000000412")]),
    ];

    /// <summary>Professional (DemoData) → assigned services of their own shop.</summary>
    private static readonly (Guid Professional, int[] Services)[] Assignments =
    [
        (Guid.Parse("0199a0de-5a10-7000-8000-000000000101"), [0x401, 0x402]),
        (Guid.Parse("0199a0de-5a10-7000-8000-000000000102"), [0x402, 0x404]),
        (Guid.Parse("0199a0de-5a10-7000-8000-000000000103"), [0x401, 0x403]),
        (Guid.Parse("0199a0de-5a10-7000-8000-000000000201"), [0x413, 0x411]),
        (Guid.Parse("0199a0de-5a10-7000-8000-000000000202"), [0x411, 0x412, 0x414]),
    ];

    /// <summary>After the demo professionals (250).</summary>
    public int Order => 300;

    public string Name => "services-demo";

    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<TrimmeDbContext>();
        using var scope = services.GetRequiredService<ISystemDataScope>().Begin();

        foreach (var (id, ar, en, icon) in Categories)
        {
            if (!await db.Set<ServiceCategory>().AnyAsync(c => c.Id == new ServiceCategoryId(id), cancellationToken))
            {
                db.Add(ServiceCategory.Create(new ServiceCategoryId(id), ar, en, icon, Array.FindIndex(Categories, c => c.Id == id) + 1, SeededAt));
            }
        }

        foreach (var s in Services)
        {
            if (await db.Set<ShopService>().AnyAsync(x => x.Id == new ShopServiceId(s.Id), cancellationToken))
            {
                continue;
            }

            var service = ShopService.Create(
                new ShopServiceId(s.Id), s.Shop, CatalogText.Create(s.NameAr, s.NameEn, s.DescriptionAr, null),
                new ServiceCategoryId(Categories[s.Category].Id), s.Price, s.Duration, onlineBookable: true, Array.IndexOf(Services, s) + 1, SeededAt).Value;
            if (!s.Active)
            {
                service.SetActive(false, SeededAt);
            }

            db.Add(service);
        }

        await db.SaveChangesAsync(cancellationToken);

        foreach (var (id, shop, ar, en, price, duration, items) in Packages)
        {
            if (!await db.Set<ServicePackage>().AnyAsync(p => p.Id == new ServicePackageId(id), cancellationToken))
            {
                db.Add(ServicePackage.Create(
                    new ServicePackageId(id), shop, CatalogText.Create(ar, en, null, null), price, duration,
                    [.. items.Select(i => new ShopServiceId(i))], 1, SeededAt).Value);
            }
        }

        foreach (var (professional, suffixes) in Assignments)
        {
            var professionalId = new ProfessionalId(professional);
            var demo = DemoData.Professionals.Single(p => p.Id == professional);
            foreach (var suffix in suffixes)
            {
                var serviceId = new ShopServiceId(Guid.Parse($"0199a0de-5a10-7000-8000-000000000{suffix:x3}"));
                if (!await db.Set<ProfessionalServiceAssignment>().AnyAsync(a => a.ProfessionalId == professionalId && a.ServiceId == serviceId, cancellationToken))
                {
                    db.Add(new ProfessionalServiceAssignment(demo.ShopId, professionalId, serviceId, SeededAt));
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private sealed record DemoService(Guid Id, ShopId Shop, int Category, string NameAr, string NameEn, string? DescriptionAr, decimal Price, int Duration, bool Active);
}
