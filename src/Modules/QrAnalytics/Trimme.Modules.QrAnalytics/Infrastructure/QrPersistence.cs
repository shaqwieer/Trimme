using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Options;
using Trimme.BuildingBlocks.Application.Qr;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.QrAnalytics.Application;
using Trimme.Modules.QrAnalytics.Domain;

namespace Trimme.Modules.QrAnalytics.Infrastructure;

internal static class QrModel
{
    /// <summary>The Professionals module's entity, referenced by name only (modules talk through contracts).</summary>
    public const string ProfessionalEntityType = "Trimme.Modules.Professionals.Domain.Professional";
}

internal sealed class QrCodeLinkConfiguration : IEntityTypeConfiguration<QrCodeLink>
{
    public void Configure(EntityTypeBuilder<QrCodeLink> builder)
    {
        builder.ToTable("qr_code_links", t =>
            t.HasCheckConstraint("ck_qr_code_links_target", "(target_type = 'Professional') = (professional_id IS NOT NULL)"));
        builder.HasKey(l => l.Id);

        // Bookings reference (shop_id, id), so a booking can only be credited to a code of its own shop (R-TEN-04).
        builder.HasShopScopedKey();

        // A professional code opens a professional of the same shop, enforced by the database.
        builder.HasShopScopedReference(QrModel.ProfessionalEntityType, nameof(QrCodeLink.ProfessionalId));
        builder.Property(l => l.Code).HasMaxLength(QrCodeFormat.Length).IsFixedLength();
        builder.HasIndex(l => l.Code).IsUnique();
        builder.Property(l => l.TargetType).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.Label).HasMaxLength(QrCodeLink.MaxLabelLength);
        builder.HasIndex(l => new { l.ShopId, l.CreatedAt });
    }
}

internal sealed class QrCodeRouteConfiguration : IEntityTypeConfiguration<QrCodeRoute>
{
    public void Configure(EntityTypeBuilder<QrCodeRoute> builder)
    {
        builder.ToTable("qr_code_routes");
        builder.HasKey(r => r.Code);
        builder.Property(r => r.Code).HasMaxLength(QrCodeFormat.Length).IsFixedLength();
        builder.HasOne<QrCodeLink>().WithOne().HasForeignKey<QrCodeRoute>(r => r.LinkId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class QrVisitConfiguration : IEntityTypeConfiguration<QrVisit>
{
    public void Configure(EntityTypeBuilder<QrVisit> builder)
    {
        builder.ToTable("qr_visits");
        builder.HasKey(v => v.Id);
        builder.HasOne<QrCodeLink>().WithMany().HasForeignKey(v => v.LinkId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(v => v.VisitorHash).HasMaxLength(QrVisit.VisitorHashLength).IsFixedLength();
        builder.Property(v => v.Device).HasConversion<string>().HasMaxLength(12);
        builder.Property(v => v.Locale).HasMaxLength(2).IsFixedLength();
        builder.HasIndex(v => new { v.LinkId, v.VisitedAt });
        builder.HasIndex(v => new { v.ShopId, v.VisitedAt });
        builder.HasIndex(v => v.VisitedAt);
    }
}

/// <summary>
/// <see cref="IQrAttributionResolver"/>: the visit from the cookie credits a booking of the same shop within the attribution
/// window (R-QR-02). Visits are a platform log (not tenant-filtered), so no data scope is needed.
/// </summary>
internal sealed class QrAttributionResolver(TrimmeDbContext db, IOptions<QrOptions> options, TimeProvider clock) : IQrAttributionResolver
{
    public async Task<QrAttribution?> ResolveAsync(Guid visitId, ShopId shopId, CancellationToken cancellationToken)
    {
        if (visitId == Guid.Empty)
        {
            return null;
        }

        var id = new QrVisitId(visitId);
        var since = clock.GetUtcNow().AddDays(-options.Value.AttributionDays);
        var visit = await db.Set<QrVisit>().AsNoTracking()
            .Where(v => v.Id == id && v.ShopId == shopId && v.VisitedAt >= since)
            .Select(v => new { v.LinkId })
            .SingleOrDefaultAsync(cancellationToken);
        return visit is null ? null : new QrAttribution(visit.LinkId.Value, visitId);
    }
}
