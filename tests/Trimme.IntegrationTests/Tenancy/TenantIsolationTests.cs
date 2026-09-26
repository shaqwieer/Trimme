using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.Modules.Shops.Domain;

namespace Trimme.IntegrationTests.Tenancy;

/// <summary>
/// Data-layer tenant isolation on PostgreSQL (R-TEN-02/03/04, R-TEN-06 data layer). Uses the real context, conventions
/// and helpers with test-only shop-owned entities in a dedicated database.
/// </summary>
public sealed class TenantIsolationTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly SwitchableTenant _tenant = new();
    private string _connectionString = string.Empty;
    private ShopId _shopA;
    private ShopId _shopB;

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        _connectionString = await postgres.CreateDatabaseAsync("tenancy", ct);
        await using var db = TenancyTestContexts.Create(_connectionString, _tenant);
        await db.Database.EnsureCreatedAsync(ct);

        _shopA = EntityId.New<ShopId>();
        _shopB = EntityId.New<ShopId>();
        db.Add(Shop.Create(_shopA, $"shop-a-{Guid.NewGuid():N}"[..20], "أ", "A", null, DateTimeOffset.UtcNow).Value);
        db.Add(Shop.Create(_shopB, $"shop-b-{Guid.NewGuid():N}"[..20], "ب", "B", null, DateTimeOffset.UtcNow).Value);
        await db.SaveChangesAsync(ct);

        await SeedAsTenantAsync(_shopA, "a-1", "a-2");
        await SeedAsTenantAsync(_shopB, "b-1");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task ShopUser_SeesOnlyItsOwnRows_AndOthersSeeNone()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TenancyTestContexts.Create(_connectionString, _tenant);

        _tenant.ShopId = _shopA;
        (await db.Set<TestItem>().Select(i => i.Name).OrderBy(n => n).ToListAsync(ct)).ShouldBe(["a-1", "a-2"]);
        var sql = db.Set<TestItem>().ToQueryString();
        sql.ShouldContain("shop_id");

        _tenant.ShopId = _shopB;
        (await db.Set<TestItem>().Select(i => i.Name).ToListAsync(ct)).ShouldBe(["b-1"]);

        // No tenant (anonymous, customer, admin, suspended shop, background work): deny-all.
        _tenant.ShopId = null;
        (await db.Set<TestItem>().CountAsync(ct)).ShouldBe(0);
        (await db.Set<TestItemNote>().CountAsync(ct)).ShouldBe(0);

        // Explicit bypass sees everything, and closes again on dispose.
        using (db.EnterUnrestrictedScope())
        {
            (await db.Set<TestItem>().CountAsync(ct)).ShouldBe(3);
        }

        (await db.Set<TestItem>().CountAsync(ct)).ShouldBe(0);
    }

    [Fact]
    public async Task CrossShop_ReadById_UpdateAndDelete_AreImpossible()
    {
        var ct = TestContext.Current.CancellationToken;
        var foreignId = await ItemIdAsync("b-1");

        await using var db = TenancyTestContexts.Create(_connectionString, _tenant);
        _tenant.ShopId = _shopA;

        // Guessing another shop's id finds nothing (the API turns this into 404, never 403).
        (await db.Set<TestItem>().SingleOrDefaultAsync(i => i.Id == foreignId, ct)).ShouldBeNull();
        (await db.Set<TestItem>().FindAsync([foreignId], ct)).ShouldBeNull();

        // Attaching a forged entity to update or delete it is refused before it reaches the database.
        var forged = await LoadUnrestrictedAsync(foreignId);
        db.Attach(forged);
        forged.Name = "hijacked";
        Should.Throw<TenantViolationException>(() => db.SaveChangesAsync(ct));
        db.ChangeTracker.Clear();

        db.Remove(forged);
        Should.Throw<TenantViolationException>(() => db.SaveChangesAsync(ct));
        db.ChangeTracker.Clear();

        (await LoadUnrestrictedAsync(foreignId)).Name.ShouldBe("b-1");
    }

    [Fact]
    public async Task Create_StampsTenantFromClaims()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TenancyTestContexts.Create(_connectionString, _tenant);
        _tenant.ShopId = _shopA;

        var stamped = new TestItem("stamped");
        db.Add(stamped);
        await db.SaveChangesAsync(ct);
        stamped.ShopId.ShouldBe(_shopA);

        // A client-chosen shop id for another shop is rejected, not silently accepted.
        db.Add(new TestItem("smuggled", _shopB));
        Should.Throw<TenantViolationException>(() => db.SaveChangesAsync(ct));
        db.ChangeTracker.Clear();

        // Without a tenant or an explicit scope nothing shop-owned can be created.
        _tenant.ShopId = null;
        db.Add(new TestItem("orphan", _shopA));
        Should.Throw<TenantViolationException>(() => db.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task ShopId_CannotChange_EvenInsideTheAdminScope()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TenancyTestContexts.Create(_connectionString, _tenant);
        _tenant.ShopId = null;

        using (db.EnterUnrestrictedScope())
        {
            // ShopId inside the (shop_id, id) key: EF itself refuses to change a key value.
            var item = await db.Set<TestItem>().SingleAsync(i => i.Name == "a-1", ct);
            item.ForceShop(_shopB);
            Should.Throw<InvalidOperationException>(() => db.SaveChangesAsync(ct)).Message.ShouldContain("ShopId");
            db.ChangeTracker.Clear();

            // ShopId outside any key: the tenant rule refuses it.
            var ownItem = await db.Set<TestItem>().Where(i => i.Name == "a-1").Select(i => i.Id).SingleAsync(ct);
            var note = new TestItemNote(ownItem, _shopA);
            db.Add(note);
            await db.SaveChangesAsync(ct);
            note.ForceShop(_shopB);
            Should.Throw<TenantViolationException>(() => db.SaveChangesAsync(ct));
        }
    }

    [Fact]
    public async Task CrossShopReference_RejectedByDatabase()
    {
        var ct = TestContext.Current.CancellationToken;
        var foreignItem = await ItemIdAsync("b-1");
        var ownItem = await ItemIdAsync("a-1");

        await using var db = TenancyTestContexts.Create(_connectionString, _tenant);
        using (db.EnterUnrestrictedScope())
        {
            // Same shop: accepted.
            db.Add(new TestItemNote(ownItem, _shopA));
            await db.SaveChangesAsync(ct);

            // A shop-A note pointing at a shop-B item: the composite FK (shop_id, item_id) → (shop_id, id) rejects it,
            // even though the application-level checks were bypassed.
            db.Add(new TestItemNote(foreignItem, _shopA));
            var failure = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
            failure.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
            db.ChangeTracker.Clear();

            // A row for a shop that does not exist: the tenant-root FK rejects it.
            db.Add(new TestItem("ghost", EntityId.New<ShopId>()));
            var ghost = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
            ghost.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
        }
    }

    [Fact]
    public async Task ProductionAndTestModels_DoNotShareTheCachedModel()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await TrimmeApiFactory.CreateMigratedAsync(postgres, "model_cache", ct);
        await using var scope = factory.Services.CreateAsyncScope();
        var production = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
        await using var test = TenancyTestContexts.Create(_connectionString, _tenant);

        production.Model.FindEntityType(typeof(TestItem)).ShouldBeNull();
        test.Model.FindEntityType(typeof(TestItem)).ShouldNotBeNull();
        production.Model.FindEntityType(typeof(Shop)).ShouldNotBeNull();
    }

    private async Task SeedAsTenantAsync(ShopId shop, params string[] names)
    {
        var tenant = new SwitchableTenant { ShopId = shop };
        await using var db = TenancyTestContexts.Create(_connectionString, tenant);
        foreach (var name in names)
        {
            db.Add(new TestItem(name));
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<TestItemId> ItemIdAsync(string name)
    {
        await using var db = TenancyTestContexts.Create(_connectionString, new SwitchableTenant());
        using var scope = db.EnterUnrestrictedScope();
        return await db.Set<TestItem>().Where(i => i.Name == name).Select(i => i.Id).SingleAsync(TestContext.Current.CancellationToken);
    }

    private async Task<TestItem> LoadUnrestrictedAsync(TestItemId id)
    {
        await using var db = TenancyTestContexts.Create(_connectionString, new SwitchableTenant());
        using var scope = db.EnterUnrestrictedScope();
        return await db.Set<TestItem>().AsNoTracking().SingleAsync(i => i.Id == id, TestContext.Current.CancellationToken);
    }
}
