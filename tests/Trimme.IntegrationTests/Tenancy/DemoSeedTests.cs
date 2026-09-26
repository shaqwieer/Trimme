using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.Modules.Identity.Domain;
using Trimme.Modules.Identity.Infrastructure.Persistence;
using Trimme.Modules.Shops.Domain;

namespace Trimme.IntegrationTests.Tenancy;

/// <summary>R-FND-11: the development seed is deterministic and idempotent.</summary>
public sealed class DemoSeedTests(PostgresFixture postgres)
{
    [Fact]
    public async Task DevSeed_IsDeterministic_AndIdempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = await SeedTwiceAsync("seed_a", ct);
        var second = await SeedTwiceAsync("seed_b", ct);

        first.ShouldBe(second, "two fresh databases seeded the same way hold identical demo rows");
        first.Shops.ShouldBe(DemoData.Shops.Select(s => $"{s.Id}|{s.Slug}|Active").Order());
        first.Users.Count.ShouldBe(4);
    }

    private async Task<SeedSnapshot> SeedTwiceAsync(string prefix, CancellationToken ct)
    {
        await using var factory = await TrimmeApiFactory.CreateMigratedAsync(postgres, prefix, ct);
        for (var run = 0; run < 2; run++)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            foreach (var seeder in scope.ServiceProvider.GetServices<IDevSeeder>().OrderBy(s => s.Order))
            {
                await seeder.SeedAsync(scope.ServiceProvider, ct);
            }
        }

        await using var read = factory.Services.CreateAsyncScope();
        var db = read.ServiceProvider.GetRequiredService<TrimmeDbContext>();
        var shops = await db.Set<Shop>().AsNoTracking().Select(s => $"{s.Id.Value}|{s.Slug}|{s.Status}").ToListAsync(ct);
        var users = await db.Set<ApplicationUser>().AsNoTracking()
            .Where(u => u.UserType == UserType.ShopUser)
            .Select(u => $"{u.Id}|{u.Email}|{u.ShopId}")
            .ToListAsync(ct);

        var userManager = read.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var owner = await userManager.FindByEmailAsync(DemoData.AlAsala.OwnerEmail);
        (await userManager.GetRolesAsync(owner!)).ShouldBe([SystemRoles.ShopOwner]);

        return new SeedSnapshot([.. shops.Order()], [.. users.Order()]);
    }

    private sealed record SeedSnapshot(IReadOnlyList<string> Shops, IReadOnlyList<string> Users)
    {
        public bool Equals(SeedSnapshot? other) => other is not null && Shops.SequenceEqual(other.Shops) && Users.SequenceEqual(other.Users);

        public override int GetHashCode() => Shops.Count;
    }
}
