using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trimme.Modules.Administration.Domain;

namespace Trimme.Modules.Administration.Infrastructure;

internal sealed class LegalDocumentConfiguration : IEntityTypeConfiguration<LegalDocument>
{
    public void Configure(EntityTypeBuilder<LegalDocument> builder)
    {
        builder.ToTable("legal_documents");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.Locale).HasMaxLength(5);
        builder.Property(d => d.Content).HasColumnType("jsonb");
        builder.HasIndex(d => new { d.Kind, d.Locale }).IsUnique();
    }
}
