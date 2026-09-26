using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Shops.Domain;

namespace Trimme.Modules.Shops.Infrastructure;

internal sealed class ShopConfiguration : IEntityTypeConfiguration<Shop>
{
    public void Configure(EntityTypeBuilder<Shop> builder)
    {
        builder.ToTable("shops");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Slug).HasMaxLength(60);
        builder.HasIndex(s => s.Slug).IsUnique();
        builder.Property(s => s.NameAr).HasMaxLength(Shop.MaxNameLength);
        builder.Property(s => s.NameEn).HasMaxLength(Shop.MaxNameLength);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.TimeZone).HasMaxLength(64);
        builder.HasIndex(s => s.Status);
    }
}

/// <summary><see cref="IShopDirectory"/> for other modules: a shop's identity and status.</summary>
internal sealed class ShopDirectory(TrimmeDbContext db) : IShopDirectory
{
    public async Task<ShopSummary?> FindAsync(ShopId shopId, CancellationToken cancellationToken) =>
        await db.Set<Shop>().AsNoTracking()
            .Where(s => s.Id == shopId)
            .Select(s => new ShopSummary(s.Id, s.Slug, s.NameAr, s.NameEn, s.Status))
            .SingleOrDefaultAsync(cancellationToken);
}
