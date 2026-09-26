using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.Modules.Shops.Domain;

namespace Trimme.Modules.Shops.Infrastructure.Seeding;

/// <summary>Two active Riyadh demo shops with fixed ids (idempotent; development only).</summary>
internal sealed class DemoShopsSeeder : IDevSeeder
{
    private static readonly DateTimeOffset SeededAt = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    public int Order => 200;

    public string Name => "shops-demo";

    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<TrimmeDbContext>();
        foreach (var demo in DemoData.Shops)
        {
            if (await db.Set<Shop>().AnyAsync(s => s.Id == demo.Id, cancellationToken))
            {
                continue;
            }

            var shop = Shop.Create(demo.Id, demo.Slug, demo.NameAr, demo.NameEn, Shop.DefaultTimeZone, SeededAt).Value;
            shop.Activate(SeededAt);
            db.Add(shop);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
