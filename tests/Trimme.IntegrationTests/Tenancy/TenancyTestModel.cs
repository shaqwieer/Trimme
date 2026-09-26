using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Shops;

namespace Trimme.IntegrationTests.Tenancy;

/// <summary>
/// Test-only shop-owned entities, mapped through the real conventions (tenant filter, stamping, tenant-root FK) and the
/// real composite-key helpers. They live in their own database (<c>EnsureCreated</c>), next to the real Shops module.
/// </summary>
internal sealed class TenancyTestModule : IModelContributor
{
    public string Schema => "tenancy_tests";

    public Assembly Assembly => typeof(TenancyTestModule).Assembly;

    public void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestItem>(item =>
        {
            item.ToTable("test_items", Schema);
            item.HasKey(i => i.Id);
            item.HasShopScopedKey();
            item.Property(i => i.Name).HasMaxLength(100);
        });

        modelBuilder.Entity<TestItemNote>(note =>
        {
            note.ToTable("test_item_notes", Schema);
            note.HasKey(n => n.Id);
            note.HasShopScopedReference<TestItemNote, TestItem>(nameof(TestItemNote.ItemId));
        });
    }
}

public readonly record struct TestItemId(Guid Value) : IEntityId<TestItemId>
{
    public static TestItemId From(Guid value) => new(value);
}

public readonly record struct TestItemNoteId(Guid Value) : IEntityId<TestItemNoteId>
{
    public static TestItemNoteId From(Guid value) => new(value);
}

public sealed class TestItem : Entity<TestItemId>, IShopOwned
{
    public TestItem(string name, ShopId shopId = default)
        : base(EntityId.New<TestItemId>())
    {
        Name = name;
        ShopId = shopId;
    }

    private TestItem()
    {
        Name = string.Empty;
    }

    public ShopId ShopId { get; private set; }

    public string Name { get; set; }

    /// <summary>Test hook to attempt moving the row to another shop.</summary>
    public void ForceShop(ShopId shopId) => ShopId = shopId;
}

public sealed class TestItemNote : Entity<TestItemNoteId>, IShopOwned
{
    public TestItemNote(TestItemId itemId, ShopId shopId = default)
        : base(EntityId.New<TestItemNoteId>())
    {
        ItemId = itemId;
        ShopId = shopId;
    }

    private TestItemNote()
    {
    }

    public ShopId ShopId { get; private set; }

    public TestItemId ItemId { get; private set; }

    /// <summary>Test hook to attempt moving the row to another shop.</summary>
    public void ForceShop(ShopId shopId) => ShopId = shopId;
}

/// <summary>A tenant that tests can switch, standing in for the request-scoped claims-based tenant.</summary>
internal sealed class SwitchableTenant : ICurrentTenant
{
    public ShopId? ShopId { get; set; }
}

internal static class TenancyTestContexts
{
    public static TrimmeDbContext Create(string connectionString, ICurrentTenant tenant)
    {
        var options = new DbContextOptionsBuilder<TrimmeDbContext>();
        options.UseTrimmeNpgsql(connectionString);
        return new TrimmeDbContext(options.Options, [new ShopsModule(), new TenancyTestModule()], tenant);
    }
}
