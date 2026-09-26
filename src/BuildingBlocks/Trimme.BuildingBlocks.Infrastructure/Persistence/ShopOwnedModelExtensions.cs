using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// Composite-key pattern for shop-owned aggregates (R-TEN-04): a parent exposes the alternate key <c>(shop_id, id)</c>
/// and children reference it with <c>(shop_id, parent_id)</c>, so the database itself rejects a row that points at
/// another shop's data, whatever the application code does.
/// </summary>
public static class ShopOwnedModelExtensions
{
    public const string IdProperty = "Id";

    /// <summary>Declares the <c>(ShopId, Id)</c> alternate key that children reference.</summary>
    public static KeyBuilder HasShopScopedKey<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class, IShopOwned
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.HasAlternateKey(nameof(IShopOwned.ShopId), IdProperty);
    }

    /// <summary>
    /// References a parent of the same shop through <c>(ShopId, <paramref name="parentIdProperty"/>)</c> →
    /// <c>(ShopId, Id)</c>. The parent must declare <see cref="HasShopScopedKey{TEntity}"/>.
    /// </summary>
    public static ReferenceCollectionBuilder<TParent, TChild> HasShopScopedReference<TChild, TParent>(
        this EntityTypeBuilder<TChild> builder,
        string parentIdProperty,
        DeleteBehavior onDelete = DeleteBehavior.Restrict)
        where TChild : class, IShopOwned
        where TParent : class, IShopOwned
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.HasOne<TParent>()
            .WithMany()
            .HasForeignKey(nameof(IShopOwned.ShopId), parentIdProperty)
            .HasPrincipalKey(nameof(IShopOwned.ShopId), IdProperty)
            .OnDelete(onDelete);
    }
}
