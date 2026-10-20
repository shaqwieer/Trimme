using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trimme.BuildingBlocks.Application.Loyalty;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Loyalty.Domain;

namespace Trimme.Modules.Loyalty.Infrastructure;

internal sealed class LoyaltyProgramConfiguration : IEntityTypeConfiguration<LoyaltyProgram>
{
    public void Configure(EntityTypeBuilder<LoyaltyProgram> builder)
    {
        builder.ToTable("programs", t =>
        {
            t.HasCheckConstraint("ck_programs_required_visits", $"required_visits BETWEEN {LoyaltyProgram.MinVisits} AND {LoyaltyProgram.MaxVisits}");
            t.HasCheckConstraint("ck_programs_discount_percent", "discount_percent IS NULL OR discount_percent BETWEEN 1 AND 100");
            t.HasCheckConstraint("ck_programs_first_visit_stamps", "first_visit_stamps >= 1 AND first_visit_stamps < required_visits");
        });
        builder.HasKey(p => p.Id);
        builder.HasIndex(p => p.ShopId).IsUnique();
        builder.Property(p => p.RewardKind).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.RewardTitleAr).HasMaxLength(LoyaltyProgram.MaxTitleLength);
        builder.Property(p => p.RewardTitleEn).HasMaxLength(LoyaltyProgram.MaxTitleLength);
    }
}

internal sealed class LoyaltyStampConfiguration : IEntityTypeConfiguration<LoyaltyStamp>
{
    public void Configure(EntityTypeBuilder<LoyaltyStamp> builder)
    {
        builder.ToTable("stamps", t => t.HasCheckConstraint("ck_stamps_number", "number >= 1"));
        builder.HasKey(s => s.Id);

        // A completed booking's stamps are numbered from 1 (more than one on a first visit, D-133), so a booking is
        // stamped once. The booking's key type belongs to the Bookings module, so this is a unique index rather than a
        // foreign key (as for reviews, D-092).
        builder.HasIndex(s => new { s.BookingId, s.Number }).IsUnique();
        builder.HasIndex(s => new { s.ShopId, s.CustomerId, s.RewardId });
        builder.HasIndex(s => s.CustomerId);
        builder.HasOne<LoyaltyReward>().WithMany().HasForeignKey(s => s.RewardId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LoyaltyRewardConfiguration : IEntityTypeConfiguration<LoyaltyReward>
{
    public void Configure(EntityTypeBuilder<LoyaltyReward> builder)
    {
        builder.ToTable("rewards");
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => r.Code).IsUnique();
        builder.HasIndex(r => new { r.ShopId, r.CustomerId, r.Status });
        builder.HasIndex(r => r.CustomerId);
        builder.HasIndex(r => r.BookingId).HasFilter("booking_id IS NOT NULL");
        builder.Property(r => r.Code).HasMaxLength(LoyaltyReward.CodeLength).IsFixedLength();
        builder.Property(r => r.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.TitleAr).HasMaxLength(LoyaltyProgram.MaxTitleLength);
        builder.Property(r => r.TitleEn).HasMaxLength(LoyaltyProgram.MaxTitleLength);
    }
}

/// <summary>
/// <see cref="ILoyaltyRewardHolder"/>: reads the reward through the customer's own data scope (another customer's reward
/// is simply not found) and marks it held on the shared unit of work, so it is saved with the booking or not at all.
/// </summary>
internal sealed class LoyaltyRewardHolder(TrimmeDbContext db, ICurrentCustomer customer) : ILoyaltyRewardHolder
{
    public async Task<Result<HeldLoyaltyReward>> HoldAsync(Guid rewardId, ShopId shopId, Guid bookingId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var id = new LoyaltyRewardId(rewardId);
        if (customer.CustomerId is not { } customerId
            || await db.Set<LoyaltyReward>().SingleOrDefaultAsync(r => r.Id == id && r.ShopId == shopId && r.CustomerId == customerId, cancellationToken) is not { } reward)
        {
            return LoyaltyErrors.RewardUnavailable();
        }

        var held = reward.Hold(bookingId, now);
        return held.IsFailure ? held.Error : new HeldLoyaltyReward(reward.Id.Value, reward.TitleAr, reward.TitleEn);
    }
}
