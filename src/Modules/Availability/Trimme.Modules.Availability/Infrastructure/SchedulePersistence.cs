using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Availability.Domain;

namespace Trimme.Modules.Availability.Infrastructure;

internal static class ScheduleModel
{
    /// <summary>The Professionals module's entity, referenced by name only (modules talk through contracts).</summary>
    public const string ProfessionalEntityType = "Trimme.Modules.Professionals.Domain.Professional";
}

internal sealed class ShopOpeningHoursConfiguration : IEntityTypeConfiguration<ShopOpeningHours>
{
    public void Configure(EntityTypeBuilder<ShopOpeningHours> builder)
    {
        builder.ToTable("shop_opening_hours");
        builder.HasKey(h => h.Id);
        builder.HasIndex(h => h.ShopId).IsUnique();
        builder.OwnsMany(h => h.Intervals, i => i.ToJson());
    }
}

internal sealed class ProfessionalWorkingHoursConfiguration : IEntityTypeConfiguration<ProfessionalWorkingHours>
{
    public void Configure(EntityTypeBuilder<ProfessionalWorkingHours> builder)
    {
        builder.ToTable("professional_working_hours");
        builder.HasKey(h => h.Id);
        builder.HasIndex(h => h.ProfessionalId).IsUnique();
        builder.HasShopScopedReference(ScheduleModel.ProfessionalEntityType, nameof(ProfessionalWorkingHours.ProfessionalId), DeleteBehavior.Cascade);
        builder.OwnsMany(h => h.Intervals, i => i.ToJson());
    }
}

internal sealed class ShopClosureConfiguration : IEntityTypeConfiguration<ShopClosure>
{
    public void Configure(EntityTypeBuilder<ShopClosure> builder)
    {
        builder.ToTable("shop_closures", t => t.HasCheckConstraint("ck_shop_closures_range", "end_date >= start_date"));
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Reason).HasMaxLength(ScheduleRules.MaxNoteLength);
        builder.HasIndex(c => new { c.ShopId, c.EndDate });
    }
}

internal sealed class ScheduleBreakConfiguration : IEntityTypeConfiguration<ScheduleBreak>
{
    public void Configure(EntityTypeBuilder<ScheduleBreak> builder)
    {
        builder.ToTable("breaks", t =>
        {
            t.HasCheckConstraint("ck_breaks_minutes", "start_minute >= 0 AND end_minute <= 1440 AND end_minute > start_minute");
            t.HasCheckConstraint("ck_breaks_recurrence", "(date IS NULL) <> (cardinality(weekdays) = 0)");
        });
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Label).HasMaxLength(ScheduleRules.MaxLabelLength);
        builder.PrimitiveCollection(b => b.Weekdays);

        // A professional-specific break belongs to one of the shop's own professionals (null = everyone).
        builder.HasShopScopedReference(ScheduleModel.ProfessionalEntityType, nameof(ScheduleBreak.ProfessionalId), DeleteBehavior.Cascade);
        builder.HasIndex(b => new { b.ShopId, b.ProfessionalId });
    }
}

internal sealed class ProfessionalTimeOffConfiguration : IEntityTypeConfiguration<ProfessionalTimeOff>
{
    public void Configure(EntityTypeBuilder<ProfessionalTimeOff> builder)
    {
        builder.ToTable("professional_time_off", t => t.HasCheckConstraint("ck_professional_time_off_range", "ends_at > starts_at"));
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.Note).HasMaxLength(ScheduleRules.MaxNoteLength);
        builder.HasShopScopedReference(ScheduleModel.ProfessionalEntityType, nameof(ProfessionalTimeOff.ProfessionalId), DeleteBehavior.Cascade);
        builder.HasIndex(t => new { t.ShopId, t.ProfessionalId, t.EndsAt });
    }
}
