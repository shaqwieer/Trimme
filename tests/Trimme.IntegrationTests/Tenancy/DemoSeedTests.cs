using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Trimme.BuildingBlocks.Application.Platform;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.Modules.Identity.Domain;
using Trimme.Modules.Identity.Infrastructure.Persistence;
using Trimme.Modules.Shops.Domain;
using Trimme.Modules.Subscriptions.Domain;

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
        first.Shops.ShouldBe(DemoData.Shops.Select(s => $"{s.Id}|{s.Slug}|Active").Concat(DemoData.ExtraShops.Select(s => $"{s.Id}|{s.Slug}|Active")).Order());
        first.Subscriptions.ShouldBe(
            [$"{DemoData.AlAsala.Id}|Active", $"{DemoData.BarberHouse.Id}|ExpiringSoon", $"{DemoData.LamsatAlRajul.Id}|Expired", $"{DemoData.AlMadina.Id}|Suspended"],
            ignoreOrder: true);
        first.PlanPrices.Count.ShouldBeGreaterThan(first.Plans, "at least one plan has more than one price version");
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

        var settings = await read.ServiceProvider.GetRequiredService<IPlatformSettings>().GetAsync(ct);
        var today = settings.LocalDate(DateTimeOffset.UtcNow);
        var coverage = await db.Set<SubscriptionCoverage>().AsNoTracking().ToListAsync(ct);
        var subscriptions = coverage
            .Select(c => $"{c.ShopId}|{SubscriptionStatusCalculator.Calculate(c.IsSuspended, c.EndDate, today, settings.ExpiringSoonThresholdDays)}")
            .ToList();
        var plans = await db.Set<SubscriptionPlan>().CountAsync(ct);
        var prices = await db.Set<PlanPrice>().AsNoTracking().Select(p => $"{p.PlanId.Value}|v{p.VersionNumber}|{p.Amount}|{p.EffectiveFrom}").ToListAsync(ct);

        var userManager = read.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var owner = await userManager.FindByEmailAsync(DemoData.AlAsala.OwnerEmail);
        (await userManager.GetRolesAsync(owner!)).ShouldBe([SystemRoles.ShopOwner]);

        return new SeedSnapshot([.. shops.Order()], [.. users.Order()], [.. subscriptions.Order()], plans, [.. prices.Order()]);
    }

    private sealed record SeedSnapshot(IReadOnlyList<string> Shops, IReadOnlyList<string> Users, IReadOnlyList<string> Subscriptions, int Plans, IReadOnlyList<string> PlanPrices)
    {
        public bool Equals(SeedSnapshot? other) =>
            other is not null && Shops.SequenceEqual(other.Shops) && Users.SequenceEqual(other.Users)
            && Subscriptions.SequenceEqual(other.Subscriptions) && Plans == other.Plans && PlanPrices.SequenceEqual(other.PlanPrices);

        public override int GetHashCode() => Shops.Count;
    }
}
