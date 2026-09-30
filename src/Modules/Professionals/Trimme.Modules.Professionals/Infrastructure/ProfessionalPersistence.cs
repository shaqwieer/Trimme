using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Media;
using Trimme.BuildingBlocks.Application.Notifications;
using Trimme.BuildingBlocks.Application.Privacy;
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

    public async Task<IReadOnlyList<ProfessionalSummary>> ListByShopAsync(ShopId shopId, CancellationToken cancellationToken) =>
        await db.Set<Professional>().AsNoTracking()
            .Where(p => p.ShopId == shopId)
            .OrderBy(p => p.NameAr).ThenBy(p => p.Id)
            .Select(p => new ProfessionalSummary(p.Id, p.ShopId, p.NameAr, p.NameEn, p.Status == ProfessionalStatus.Active))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<ProfessionalId, ProfessionalSummary>> FindManyAsync(
        IReadOnlyCollection<ProfessionalId> professionalIds, CancellationToken cancellationToken)
    {
        if (professionalIds.Count == 0)
        {
            return new Dictionary<ProfessionalId, ProfessionalSummary>();
        }

        var ids = professionalIds.Distinct().ToArray();
        return await db.Set<Professional>().AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new ProfessionalSummary(p.Id, p.ShopId, p.NameAr, p.NameEn, p.Status == ProfessionalStatus.Active))
            .ToDictionaryAsync(p => p.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<PublicProfessionalCard>> ListActiveProfilesAsync(IReadOnlyCollection<ShopId> shopIds, CancellationToken cancellationToken)
    {
        if (shopIds.Count == 0)
        {
            return [];
        }

        var ids = shopIds.Distinct().ToArray();
        var professionals = await db.Set<Professional>().AsNoTracking()
            .Where(p => ids.Contains(p.ShopId) && p.Status == ProfessionalStatus.Active)
            .OrderBy(p => p.NameAr).ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);
        return
        [
            .. professionals.Select(p => new PublicProfessionalCard(
                p.Id, p.ShopId, p.Slug, p.NameAr, p.NameEn, p.SpecialtyAr, p.SpecialtyEn, MediaRules.Url(p.AvatarMediaId))),
        ];
    }
}

/// <summary>
/// <see cref="IProfessionalContactReader"/>: the professional's WhatsApp number, decrypted only when notifications are on
/// (spec §16), for the notification jobs (architecture rule). Reads through the caller's scope (the system scope in jobs).
/// </summary>
internal sealed class ProfessionalContactReader(TrimmeDbContext db, IPersonalDataProtector protector, TimeProvider clock) : IProfessionalContactReader
{
    public async Task<ProfessionalContactCard?> FindAsync(ProfessionalId professionalId, CancellationToken cancellationToken)
    {
        var row = await (from professional in db.Set<Professional>().AsNoTracking()
                         join contact in db.Set<ProfessionalContact>().AsNoTracking() on professional.Id equals contact.ProfessionalId into contacts
                         from contact in contacts.DefaultIfEmpty()
                         where professional.Id == professionalId
                         select new
                         {
                             professional.Id,
                             professional.ShopId,
                             professional.NameAr,
                             professional.NameEn,
                             professional.Status,
                             Protected = contact != null && contact.NotificationsEnabled ? contact.ProtectedWhatsApp : null,
                         }).SingleOrDefaultAsync(cancellationToken);
        return row is null
            ? null
            : new ProfessionalContactCard(
                row.Id, row.ShopId, row.NameAr, row.NameEn, row.Status == ProfessionalStatus.Active,
                row.Protected is null ? null : protector.Unprotect(row.Protected, PersonalDataPurposes.ProfessionalWhatsApp));
    }

    public async Task RecordDeliveryAsync(ProfessionalId professionalId, bool delivered, CancellationToken cancellationToken)
    {
        if (await db.Set<ProfessionalContact>().SingleOrDefaultAsync(c => c.ProfessionalId == professionalId, cancellationToken) is { } contact)
        {
            contact.RecordDelivery(delivered, clock.GetUtcNow());
        }
    }
}
