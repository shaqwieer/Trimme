using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Media;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Professionals.Domain;

namespace Trimme.Modules.Professionals.Infrastructure;

internal sealed class ProfessionalConfiguration : IEntityTypeConfiguration<Professional>
{
    public void Configure(EntityTypeBuilder<Professional> builder)
    {
        builder.ToTable("professionals");
        builder.HasKey(p => p.Id);

        // (shop_id, id) is what children reference, so the database rejects a row that points at another shop's professional.
        builder.HasShopScopedKey();
        builder.HasIndex(p => new { p.ShopId, p.Slug }).IsUnique();
        builder.HasIndex(p => new { p.ShopId, p.Status });

        builder.Property(p => p.Slug).HasMaxLength(60);
        builder.Property(p => p.NameAr).HasMaxLength(Professional.MaxNameLength);
        builder.Property(p => p.NameEn).HasMaxLength(Professional.MaxNameLength);
        builder.Property(p => p.SpecialtyAr).HasMaxLength(Professional.MaxSpecialtyLength);
        builder.Property(p => p.SpecialtyEn).HasMaxLength(Professional.MaxSpecialtyLength);
        builder.Property(p => p.BioAr).HasMaxLength(Professional.MaxBioLength);
        builder.Property(p => p.BioEn).HasMaxLength(Professional.MaxBioLength);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<StoredMedia>().WithMany().HasForeignKey(p => p.AvatarMediaId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class ProfessionalContactConfiguration : IEntityTypeConfiguration<ProfessionalContact>
{
    public void Configure(EntityTypeBuilder<ProfessionalContact> builder)
    {
        builder.ToTable("professional_contacts");
        builder.HasKey(c => c.ProfessionalId);
        builder.HasShopScopedReference<ProfessionalContact, Professional>(nameof(ProfessionalContact.ProfessionalId), DeleteBehavior.Cascade);

        builder.Property(c => c.ProtectedWhatsApp).HasColumnName("protected_whatsapp").HasMaxLength(1000);
        builder.Property(c => c.WhatsAppLookupHash).HasColumnName("whatsapp_lookup_hash").HasMaxLength(64);
        builder.Property(c => c.WhatsAppMasked).HasColumnName("whatsapp_masked").HasMaxLength(32);
        builder.Property(c => c.Verification).HasColumnName("whatsapp_verification").HasConversion<string>().HasMaxLength(20);

        // One number, one professional, platform-wide: a person cannot be listed as a second professional in another shop.
        builder.HasIndex(c => c.WhatsAppLookupHash).IsUnique().HasFilter("whatsapp_lookup_hash IS NOT NULL");
        builder.Ignore(c => c.HasNumber);
        builder.Ignore(c => c.CanReceiveNotifications);
    }
}

/// <summary><see cref="IProfessionalDirectory"/> for other modules; reads through the caller's data scope.</summary>
internal sealed class ProfessionalDirectory(TrimmeDbContext db) : IProfessionalDirectory
{
    public async Task<ProfessionalSummary?> FindAsync(ProfessionalId professionalId, CancellationToken cancellationToken) =>
        await db.Set<Professional>().AsNoTracking()
            .Where(p => p.Id == professionalId)
            .Select(p => new ProfessionalSummary(p.Id, p.ShopId, p.NameAr, p.NameEn, p.Status == ProfessionalStatus.Active))
            .SingleOrDefaultAsync(cancellationToken);
}
