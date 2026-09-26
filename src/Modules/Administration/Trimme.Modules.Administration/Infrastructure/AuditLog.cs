using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trimme.BuildingBlocks.Application.Auditing;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Observability;
using Trimme.Modules.Administration.Domain;

namespace Trimme.Modules.Administration.Infrastructure;

/// <summary>Adds audit entries to the current unit of work; the caller's SaveChanges commits them with the change.</summary>
internal sealed class AuditLog(TrimmeDbContext db, ICurrentUser user, TimeProvider clock, IHttpContextAccessor http) : IAuditLog
{
    public const string SystemActor = "System";

    public void Record(AuditRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        db.Add(new AuditEntry(
            EntityId.New<AuditEntryId>(),
            clock.GetUtcNow(),
            user.UserId,
            user.UserType ?? SystemActor,
            record.Action,
            record.EntityType,
            record.EntityId,
            record.ShopId?.Value,
            record.Summary,
            record.Reason,
            http.HttpContext is { } context ? CorrelationId.Get(context) : null));
    }
}

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_entries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.ActorType).HasMaxLength(20);
        builder.Property(e => e.Action).HasMaxLength(80);
        builder.Property(e => e.EntityType).HasMaxLength(60);
        builder.Property(e => e.EntityId).HasMaxLength(64);
        builder.Property(e => e.Summary).HasMaxLength(500);
        builder.Property(e => e.Reason).HasMaxLength(500);
        builder.Property(e => e.CorrelationId).HasMaxLength(64);
        builder.HasIndex(e => e.OccurredAt);
        builder.HasIndex(e => new { e.EntityType, e.EntityId });
        builder.HasIndex(e => e.ShopId);
    }
}
