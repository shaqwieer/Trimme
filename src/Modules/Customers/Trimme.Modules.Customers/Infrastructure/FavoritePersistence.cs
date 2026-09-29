using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Customers.Domain;

namespace Trimme.Modules.Customers.Infrastructure;

internal static class FavoriteModel
{
    /// <summary>The Professionals module's entity, referenced by name only (modules talk through contracts).</summary>
    public const string ProfessionalEntityType = "Trimme.Modules.Professionals.Domain.Professional";
}

internal sealed class FavoriteConfiguration : IEntityTypeConfiguration<Favorite>
{
    public void Configure(EntityTypeBuilder<Favorite> builder)
    {
        builder.ToTable("favorites");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.CustomerId).IsRequired();

        // A professional favorite points at a professional of its own shop (composite key: a professional has one shop).
        builder.HasShopScopedReference(FavoriteModel.ProfessionalEntityType, nameof(Favorite.ProfessionalId));

        // Saving twice is a no-op: one row per customer and shop, and per customer and professional.
        builder.HasIndex(f => new { f.CustomerId, f.ShopId }).IsUnique().HasFilter("professional_id IS NULL")
            .HasDatabaseName("ix_favorites_customer_id_shop_id_shop");
        builder.HasIndex(f => new { f.CustomerId, f.ProfessionalId }).IsUnique().HasFilter("professional_id IS NOT NULL")
            .HasDatabaseName("ix_favorites_customer_id_professional_id");
    }
}
