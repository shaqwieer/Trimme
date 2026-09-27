using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Subscriptions.Domain;

namespace Trimme.Modules.Subscriptions.Infrastructure;

internal sealed class SubscriptionPlanConfiguration : IEntityTypeConfiguration<SubscriptionPlan>
{
    public void Configure(EntityTypeBuilder<SubscriptionPlan> builder)
    {
        builder.ToTable("subscription_plans", t =>
        {
            t.HasCheckConstraint("ck_subscription_plans_interval", "interval_count >= 1");
            t.HasCheckConstraint("ck_subscription_plans_limits",
                "(max_professionals IS NULL OR max_professionals >= 1) AND (max_services IS NULL OR max_services >= 1) " +
                "AND (trial_days IS NULL OR trial_days BETWEEN 0 AND 365) AND (grace_days IS NULL OR grace_days BETWEEN 0 AND 365)");
        });
        builder.HasKey(p => p.Id);
        builder.Property(p => p.NameAr).HasMaxLength(SubscriptionPlan.MaxNameLength);
        builder.Property(p => p.NameEn).HasMaxLength(SubscriptionPlan.MaxNameLength);
        builder.Property(p => p.DescriptionAr).HasMaxLength(SubscriptionPlan.MaxDescriptionLength);
        builder.Property(p => p.DescriptionEn).HasMaxLength(SubscriptionPlan.MaxDescriptionLength);
        builder.Property(p => p.IntervalUnit).HasConversion<string>().HasMaxLength(10);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.OwnsMany(p => p.Features, f => f.ToJson());
        builder.HasIndex(p => p.DisplayOrder);

        // Price versions are append-only; a plan is archived, never deleted.
        builder.HasMany(p => p.Prices).WithOne().HasForeignKey(p => p.PlanId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(p => p.Prices).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class PlanPriceConfiguration : IEntityTypeConfiguration<PlanPrice>
{
    public void Configure(EntityTypeBuilder<PlanPrice> builder)
    {
        builder.ToTable("plan_prices", t => t.HasCheckConstraint("ck_plan_prices_amount", "amount >= 0"));
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Amount).HasPrecision(12, 2);
        builder.Property(p => p.Currency).HasMaxLength(3).IsFixedLength();

        // One version per date and a gap-free version sequence, even under concurrent writes.
        builder.HasIndex(p => new { p.PlanId, p.EffectiveFrom }).IsUnique();
        builder.HasIndex(p => new { p.PlanId, p.VersionNumber }).IsUnique();
    }
}

internal sealed class ShopSubscriptionConfiguration : IEntityTypeConfiguration<ShopSubscription>
{
    public void Configure(EntityTypeBuilder<ShopSubscription> builder)
    {
        builder.ToTable("shop_subscriptions", t => t.HasCheckConstraint("ck_shop_subscriptions_dates", "end_date >= start_date"));
        builder.HasKey(s => s.Id);
        builder.HasShopScopedKey();

        // One subscription per shop: two concurrent assignments cannot both succeed (the loser gets 409).
        builder.HasIndex(s => s.ShopId).IsUnique();
        builder.HasIndex(s => s.EndDate);
        builder.Property(s => s.SuspensionReason).HasMaxLength(ShopSubscription.MaxReasonLength);
        builder.HasOne<SubscriptionPlan>().WithMany().HasForeignKey(s => s.PlanId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(s => s.Periods).WithOne()
            .HasForeignKey(p => new { p.ShopId, p.SubscriptionId })
            .HasPrincipalKey(s => new { s.ShopId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(s => s.Overrides).WithOne()
            .HasForeignKey(o => new { o.ShopId, o.SubscriptionId })
            .HasPrincipalKey(s => new { s.ShopId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(s => s.Periods).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(s => s.Overrides).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(s => s.LatestPeriod);
    }
}

internal sealed class SubscriptionPeriodConfiguration : IEntityTypeConfiguration<SubscriptionPeriod>
{
    public void Configure(EntityTypeBuilder<SubscriptionPeriod> builder)
    {
        builder.ToTable("subscription_periods", t =>
        {
            t.HasCheckConstraint("ck_subscription_periods_dates", "period_end >= period_start");
            t.HasCheckConstraint("ck_subscription_periods_amount", "amount >= 0");
        });
        builder.HasKey(p => p.Id);
        builder.HasShopScopedKey();
        builder.Property(p => p.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.PlanNameAr).HasMaxLength(SubscriptionPlan.MaxNameLength);
        builder.Property(p => p.PlanNameEn).HasMaxLength(SubscriptionPlan.MaxNameLength);
        builder.Property(p => p.Amount).HasPrecision(12, 2);
        builder.Property(p => p.Currency).HasMaxLength(3).IsFixedLength();
        builder.Property(p => p.Notes).HasMaxLength(ShopSubscription.MaxReasonLength);
        builder.Property(p => p.StandardAmount).HasPrecision(12, 2);
        builder.Property(p => p.PricingReason).HasMaxLength(ShopSubscription.MaxReasonLength);

        // The snapshot points at the exact plan and price version it was charged; neither can be deleted.
        builder.HasOne<SubscriptionPlan>().WithMany().HasForeignKey(p => p.PlanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PlanPrice>().WithMany().HasForeignKey(p => p.PlanPriceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => new { p.ShopId, p.PeriodStart });
    }
}

internal sealed class SubscriptionOverrideConfiguration : IEntityTypeConfiguration<SubscriptionOverride>
{
    public void Configure(EntityTypeBuilder<SubscriptionOverride> builder)
    {
        builder.ToTable("subscription_overrides");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.PreviousAmount).HasPrecision(12, 2);
        builder.Property(o => o.NewAmount).HasPrecision(12, 2);
        builder.Property(o => o.Reason).HasMaxLength(ShopSubscription.MaxReasonLength);

        // The overridden period belongs to the same shop (database-enforced).
        builder.HasShopScopedReference<SubscriptionOverride, SubscriptionPeriod>(nameof(SubscriptionOverride.PeriodId));
    }
}

internal sealed class SubscriptionCoverageConfiguration : IEntityTypeConfiguration<SubscriptionCoverage>
{
    /// <summary>The Shops module's entity, referenced by name only (modules talk through contracts, D-073).</summary>
    public const string ShopEntityType = "Trimme.Modules.Shops.Domain.Shop";

    public void Configure(EntityTypeBuilder<SubscriptionCoverage> builder)
    {
        builder.ToTable("subscription_coverage");
        builder.HasKey(c => c.ShopId);
        builder.HasOne(ShopEntityType, navigationName: null).WithMany().HasForeignKey(nameof(SubscriptionCoverage.ShopId)).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => c.EndDate);
    }
}
