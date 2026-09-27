using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Media;
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
        builder.Property(s => s.DescriptionAr).HasMaxLength(Shop.MaxDescriptionLength);
        builder.Property(s => s.DescriptionEn).HasMaxLength(Shop.MaxDescriptionLength);
        builder.Property(s => s.Category).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.PublicPhone).HasMaxLength(20);
        builder.PrimitiveCollection(s => s.Amenities).ElementType(e => e.HasConversion<string>());
        builder.PrimitiveCollection(s => s.EditableFields).ElementType(e => e.HasConversion<string>());
        builder.PrimitiveCollection(s => s.GalleryMediaIds);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.TimeZone).HasMaxLength(64);
        builder.HasIndex(s => s.Status);

        // Logo and cover are stored images (D-064); the gallery is an ordered id array owned by this row.
        builder.HasOne<StoredMedia>().WithMany().HasForeignKey(s => s.LogoMediaId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<StoredMedia>().WithMany().HasForeignKey(s => s.CoverMediaId).OnDelete(DeleteBehavior.SetNull);

        builder.OwnsOne(s => s.Location, location =>
        {
            location.Property(l => l.Point).HasColumnName("location").HasColumnType("geography (point, 4326)").IsRequired();
            location.HasIndex(l => l.Point).HasMethod("gist");
            location.Property(l => l.AddressLine).HasColumnName("address_line").HasMaxLength(ShopLocation.MaxAddressLineLength);
            location.Property(l => l.District).HasColumnName("district").HasMaxLength(ShopLocation.MaxAreaLength);
            location.Property(l => l.City).HasColumnName("city").HasMaxLength(ShopLocation.MaxAreaLength);
            location.Property(l => l.FormattedAddress).HasColumnName("formatted_address").HasMaxLength(ShopLocation.MaxFormattedAddressLength);
            location.Property(l => l.Source).HasColumnName("location_source").HasConversion<string>().HasMaxLength(20);
            location.Property(l => l.ConfirmedAt).HasColumnName("location_confirmed_at");
            location.Property(l => l.ConfirmedBy).HasColumnName("location_confirmed_by");
            location.Ignore(l => l.Latitude);
            location.Ignore(l => l.Longitude);
        });
        builder.Navigation(s => s.Location).IsRequired(false);
    }
}

/// <summary><see cref="IShopDirectory"/> for other modules: a shop's identity and status.</summary>
internal sealed class ShopDirectory(TrimmeDbContext db) : IShopDirectory
{
    public async Task<ShopSummary?> FindAsync(ShopId shopId, CancellationToken cancellationToken) =>
        await Summaries(Shops().Where(s => s.Id == shopId)).SingleOrDefaultAsync(cancellationToken);

    public async Task<ShopSummary?> FindBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        var normalized = slug.Trim().ToLowerInvariant();
        return await Summaries(Shops().Where(s => s.Slug == normalized)).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<ShopId, ShopSummary>> FindManyAsync(IReadOnlyCollection<ShopId> shopIds, CancellationToken cancellationToken)
    {
        if (shopIds.Count == 0)
        {
            return new Dictionary<ShopId, ShopSummary>();
        }

        var ids = shopIds.Distinct().ToArray();
        var summaries = await Summaries(Shops().Where(s => ids.Contains(s.Id))).ToListAsync(cancellationToken);
        return summaries.ToDictionary(s => s.Id);
    }

    public async Task<IReadOnlyList<ShopId>> SearchIdsAsync(string term, int limit, CancellationToken cancellationToken)
    {
        var pattern = $"%{term.Trim()}%";
        return await Shops()
            .Where(s => EF.Functions.ILike(s.NameAr, pattern) || EF.Functions.ILike(s.NameEn, pattern) || EF.Functions.ILike(s.Slug, pattern))
            .OrderBy(s => s.Id)
            .Select(s => s.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountAsync(CancellationToken cancellationToken) => Shops().CountAsync(cancellationToken);

    private IQueryable<Shop> Shops() => db.Set<Shop>().AsNoTracking();

    private static IQueryable<ShopSummary> Summaries(IQueryable<Shop> shops) =>
        shops.Select(s => new ShopSummary(s.Id, s.Slug, s.NameAr, s.NameEn, s.Status));
}
