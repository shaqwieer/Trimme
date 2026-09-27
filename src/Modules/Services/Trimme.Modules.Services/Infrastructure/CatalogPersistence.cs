using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Services.Domain;

namespace Trimme.Modules.Services.Infrastructure;

internal static class CatalogModel
{
    /// <summary>The Professionals module's entity, referenced by name only (modules talk through contracts, D-073).</summary>
    public const string ProfessionalEntityType = "Trimme.Modules.Professionals.Domain.Professional";
}

internal sealed class ServiceCategoryConfiguration : IEntityTypeConfiguration<ServiceCategory>
{
    public void Configure(EntityTypeBuilder<ServiceCategory> builder)
    {
        builder.ToTable("service_categories");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.NameAr).HasMaxLength(80);
        builder.Property(c => c.NameEn).HasMaxLength(80);
        builder.Property(c => c.Icon).HasMaxLength(20);
        builder.HasIndex(c => c.DisplayOrder);
    }
}

internal sealed class ShopServiceConfiguration : IEntityTypeConfiguration<ShopService>
{
    public void Configure(EntityTypeBuilder<ShopService> builder)
    {
        builder.ToTable("shop_services");
        builder.HasKey(s => s.Id);
        builder.HasShopScopedKey();
        builder.Property(s => s.NameAr).HasMaxLength(CatalogRules.MaxNameLength);
        builder.Property(s => s.NameEn).HasMaxLength(CatalogRules.MaxNameLength);
        builder.Property(s => s.DescriptionAr).HasMaxLength(CatalogRules.MaxDescriptionLength);
        builder.Property(s => s.DescriptionEn).HasMaxLength(CatalogRules.MaxDescriptionLength);
        builder.Property(s => s.Price).HasPrecision(10, 2);
        builder.Property(s => s.Currency).HasMaxLength(3).IsFixedLength();
        builder.Property(s => s.Moderation).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.ModerationReason).HasMaxLength(500);
        builder.HasOne<ServiceCategory>().WithMany().HasForeignKey(s => s.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => new { s.ShopId, s.DisplayOrder });
        builder.HasIndex(s => new { s.ShopId, s.IsArchived, s.IsActive });
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_shop_services_price", "price >= 0");
            t.HasCheckConstraint("ck_shop_services_duration", "duration_minutes BETWEEN 5 AND 480 AND duration_minutes % 5 = 0");
        });
        builder.Ignore(s => s.IsPubliclyAvailable);
    }
}

internal sealed class ServicePackageConfiguration : IEntityTypeConfiguration<ServicePackage>
{
    public void Configure(EntityTypeBuilder<ServicePackage> builder)
    {
        builder.ToTable("service_packages");
        builder.HasKey(p => p.Id);
        builder.HasShopScopedKey();
        builder.Property(p => p.NameAr).HasMaxLength(CatalogRules.MaxNameLength);
        builder.Property(p => p.NameEn).HasMaxLength(CatalogRules.MaxNameLength);
        builder.Property(p => p.DescriptionAr).HasMaxLength(CatalogRules.MaxDescriptionLength);
        builder.Property(p => p.DescriptionEn).HasMaxLength(CatalogRules.MaxDescriptionLength);
        builder.Property(p => p.Price).HasPrecision(10, 2);
        builder.Property(p => p.Currency).HasMaxLength(3).IsFixedLength();
        builder.Property(p => p.Moderation).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.ModerationReason).HasMaxLength(500);
        builder.HasIndex(p => new { p.ShopId, p.DisplayOrder });
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_service_packages_price", "price >= 0");
            t.HasCheckConstraint("ck_service_packages_duration", "duration_minutes BETWEEN 5 AND 480 AND duration_minutes % 5 = 0");
        });

        // Items belong to the package through (shop_id, package_id) → (shop_id, id).
        builder.HasMany(p => p.Items).WithOne()
            .HasForeignKey(i => new { i.ShopId, i.PackageId })
            .HasPrincipalKey(p => new { p.ShopId, p.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ServicePackageItemConfiguration : IEntityTypeConfiguration<ServicePackageItem>
{
    public void Configure(EntityTypeBuilder<ServicePackageItem> builder)
    {
        builder.ToTable("service_package_items");
        builder.HasKey(i => new { i.PackageId, i.ServiceId });

        // The item's service must belong to the same shop as the package (database-enforced).
        builder.HasShopScopedReference<ServicePackageItem, ShopService>(nameof(ServicePackageItem.ServiceId));
    }
}

internal sealed class ProfessionalServiceAssignmentConfiguration : IEntityTypeConfiguration<ProfessionalServiceAssignment>
{
    public void Configure(EntityTypeBuilder<ProfessionalServiceAssignment> builder)
    {
        builder.ToTable("professional_services");
        builder.HasKey(a => new { a.ProfessionalId, a.ServiceId });
        builder.HasShopScopedReference<ProfessionalServiceAssignment, ShopService>(nameof(ProfessionalServiceAssignment.ServiceId), DeleteBehavior.Cascade);
        builder.HasShopScopedReference(CatalogModel.ProfessionalEntityType, nameof(ProfessionalServiceAssignment.ProfessionalId), DeleteBehavior.Cascade);
        builder.HasIndex(a => new { a.ShopId, a.ServiceId });
    }
}

/// <summary>Package items keep a service in use: it can be archived, not deleted (R-SVC-02).</summary>
internal sealed class PackageServiceUsage(TrimmeDbContext db) : IShopServiceUsage
{
    public Task<bool> IsInUseAsync(ShopId shopId, Guid serviceId, CancellationToken cancellationToken)
    {
        var id = new ShopServiceId(serviceId);
        return db.Set<ServicePackageItem>().AnyAsync(i => i.ShopId == shopId && i.ServiceId == id, cancellationToken);
    }
}
