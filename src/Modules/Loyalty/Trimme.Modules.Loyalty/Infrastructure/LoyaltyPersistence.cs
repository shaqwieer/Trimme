using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trimme.BuildingBlocks.Application.Loyalty;
using Trimme.BuildingBlocks.Application.Privacy;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Loyalty.Application;
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

internal sealed class LoyaltySettingsConfiguration : IEntityTypeConfiguration<LoyaltySettings>
{
    public void Configure(EntityTypeBuilder<LoyaltySettings> builder)
    {
        builder.ToTable("settings", t =>
        {
            t.HasCheckConstraint("ck_settings_points", $"first_visit_points BETWEEN 1 AND {LoyaltySettings.MaxVisitPoints} AND visit_points BETWEEN 1 AND {LoyaltySettings.MaxVisitPoints} AND points_for_reward BETWEEN {LoyaltySettings.MinPointsForReward} AND {LoyaltySettings.MaxPointsForReward}");
            t.HasCheckConstraint("ck_settings_reward", "(reward_type = 'FreeService' AND reward_service_id IS NOT NULL) OR (reward_type = 'AmountOff' AND reward_amount BETWEEN 5 AND 1000)");
        });
        builder.HasKey(s => s.Id);
        builder.HasIndex(s => s.ShopId).IsUnique();
        builder.Property(s => s.RewardType).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.InactivityRule).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.RewardAmount).HasPrecision(10, 2);
        builder.Property(s => s.RewardServiceNameAr).HasMaxLength(LoyaltySettings.MaxNameLength);
        builder.Property(s => s.RewardServiceNameEn).HasMaxLength(LoyaltySettings.MaxNameLength);
    }
}

internal sealed class LoyaltyAccountConfiguration : IEntityTypeConfiguration<LoyaltyAccount>
{
    public void Configure(EntityTypeBuilder<LoyaltyAccount> builder)
    {
        builder.ToTable("accounts", t => t.HasCheckConstraint("ck_accounts_points", "points >= 0"));
        builder.HasKey(a => a.Id);
        builder.Property(a => a.MemberKey).HasMaxLength(LoyaltyAccount.MemberKeyLength);

        // One card per member and shop.
        builder.HasIndex(a => new { a.ShopId, a.MemberKey }).IsUnique();
        builder.HasIndex(a => a.MemberKey);
    }
}

internal sealed class LoyaltyEntryConfiguration : IEntityTypeConfiguration<LoyaltyEntry>
{
    public void Configure(EntityTypeBuilder<LoyaltyEntry> builder)
    {
        builder.ToTable("entries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.MemberKey).HasMaxLength(LoyaltyAccount.MemberKeyLength);
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.ClosedByName).HasMaxLength(LoyaltySettings.MaxNameLength);

        // A booking earns once and redeems once, however often its completion is processed.
        builder.HasIndex(e => new { e.BookingId, e.Kind }).IsUnique().HasFilter("booking_id IS NOT NULL");
        builder.HasIndex(e => new { e.ShopId, e.MemberKey, e.At });
        builder.HasIndex(e => new { e.ShopId, e.At });
    }
}

internal sealed class LoyaltyRedemptionConfiguration : IEntityTypeConfiguration<LoyaltyRedemption>
{
    public void Configure(EntityTypeBuilder<LoyaltyRedemption> builder)
    {
        builder.ToTable("redemptions", t =>
        {
            t.HasCheckConstraint("ck_redemptions_points", "points >= 1");
            t.HasCheckConstraint("ck_redemptions_discount", "discount >= 0");
        });
        builder.HasKey(r => r.Id);
        builder.Property(r => r.MemberKey).HasMaxLength(LoyaltyAccount.MemberKeyLength);
        builder.Property(r => r.RewardType).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Discount).HasPrecision(10, 2);
        builder.Property(r => r.TitleAr).HasMaxLength(LoyaltySettings.MaxNameLength);
        builder.Property(r => r.TitleEn).HasMaxLength(LoyaltySettings.MaxNameLength);

        // One reward per booking.
        builder.HasIndex(r => r.BookingId).IsUnique();
        builder.HasIndex(r => new { r.ShopId, r.MemberKey, r.Status });
        builder.HasIndex(r => r.CustomerId);
    }
}

/// <summary>
/// <see cref="ILoyaltyRewardHolder"/>. A D-132 coupon is read through the customer's own data scope (another customer's
/// is not found). A points reward (D-149) is held for the member of the mobile: the online account's, or the walk-in's
/// typed number. Both are added to the shared unit of work, so they are saved with the booking or not at all.
/// </summary>
internal sealed class LoyaltyRewardHolder(
    TrimmeDbContext db, ICurrentCustomer customer, LoyaltyLedger ledger, ILoyaltyAddOnStatus addOn, ILoyaltyMemberKeys keys, IPersonalDataProtector protector)
    : ILoyaltyRewardHolder
{
    public async Task<Result<HeldLoyaltyReward>> HoldAsync(Guid rewardId, ShopId shopId, Guid bookingId, decimal price, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var id = new LoyaltyRewardId(rewardId);
        if (customer.CustomerId is not { } customerId
            || await db.Set<LoyaltyReward>().SingleOrDefaultAsync(r => r.Id == id && r.ShopId == shopId && r.CustomerId == customerId, cancellationToken) is not { } reward)
        {
            return LoyaltyErrors.RewardUnavailable();
        }

        var held = reward.Hold(bookingId, now);
        return held.IsFailure ? held.Error : new HeldLoyaltyReward(reward.Id.Value, reward.TitleAr, reward.TitleEn, reward.DiscountOn(price));
    }

    public async Task<Result<HeldPointsReward>> HoldPointsRewardAsync(PointsRewardHold request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var memberKey = request.CustomerId is { } customerId
            ? await keys.ForCustomerAsync(customerId, cancellationToken)
            : request.WalkInPhoneE164 is { } phone ? LoyaltyMembers.KeyOf(protector, phone) : null;
        if (memberKey is null)
        {
            return LoyaltyPointErrors.NoRewardReady();
        }

        if (!await addOn.IsActiveAsync(request.ShopId, cancellationToken) || await ledger.SettingsAsync(request.ShopId, cancellationToken) is not { } settings)
        {
            return LoyaltyPointErrors.NotRunning();
        }

        // One hold at a time per member and shop, so two bookings can never use the same points.
        await ledger.LockAsync(request.ShopId, memberKey, cancellationToken);
        var account = await ledger.AccountAsync(request.ShopId, memberKey, track: false, cancellationToken);
        var held = await ledger.HeldAsync(request.ShopId, memberKey, null, cancellationToken);
        if (LoyaltyBalance.Available(account, settings, held, request.Now) < settings.PointsForReward)
        {
            return LoyaltyPointErrors.NoRewardReady();
        }

        if (LoyaltyBalance.Discount(settings, request.Price, [.. request.Services.Select(s => (s.ServiceId, s.Price))]) is not { } discount)
        {
            return LoyaltyPointErrors.ServiceNotBooked();
        }

        var redemption = LoyaltyRedemption.Hold(request.ShopId, request.CustomerId, memberKey, request.BookingId, settings, discount, request.Now);
        db.Add(redemption);
        return new HeldPointsReward(redemption.Id.Value, redemption.TitleAr, redemption.TitleEn, discount);
    }
}
