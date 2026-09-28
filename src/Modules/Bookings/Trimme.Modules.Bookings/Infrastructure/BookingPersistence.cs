using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Bookings.Domain;

namespace Trimme.Modules.Bookings.Infrastructure;

internal static class BookingModel
{
    /// <summary>The Professionals module's entity, referenced by name only (modules talk through contracts).</summary>
    public const string ProfessionalEntityType = "Trimme.Modules.Professionals.Domain.Professional";

    /// <summary>
    /// The generated <c>during</c> range and the exclusion constraint over it (R-BKG-03). The constraint and the same-shop
    /// service/package keys are added by SQL in the <c>Bookings</c> migration: EF cannot express an exclusion constraint,
    /// and the snapshot keeps plain ids (the catalogue's typed keys belong to the Services module).
    /// </summary>
    public const string DuringColumn = "during";

    public const string ExclusionConstraint = "ex_bookings_professional_overlap";
}

internal sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("bookings", t =>
        {
            t.HasCheckConstraint("ck_bookings_range", "ends_at > starts_at");
            t.HasCheckConstraint("ck_bookings_item", "(service_id IS NULL) <> (package_id IS NULL)");
        });
        builder.HasKey(b => b.Id);
        builder.HasShopScopedKey();
        builder.HasShopScopedReference(BookingModel.ProfessionalEntityType, nameof(Booking.ProfessionalId));

        builder.Property(b => b.CustomerName).HasMaxLength(BookingRules.MaxNameLength);
        builder.Property(b => b.Reference).HasMaxLength(BookingRules.ReferenceLength).IsFixedLength();
        builder.HasIndex(b => b.Reference).IsUnique();
        builder.Property(b => b.ProfessionalNameAr).HasMaxLength(BookingRules.MaxNameLength);
        builder.Property(b => b.ProfessionalNameEn).HasMaxLength(BookingRules.MaxNameLength);
        builder.Property(b => b.ItemNameAr).HasMaxLength(BookingRules.MaxNameLength);
        builder.Property(b => b.ItemNameEn).HasMaxLength(BookingRules.MaxNameLength);
        builder.Property(b => b.Price).HasPrecision(10, 2);
        builder.Property(b => b.AmountDue).HasPrecision(10, 2);
        builder.Property(b => b.Currency).HasMaxLength(3).IsFixedLength();
        builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(24);
        builder.Property(b => b.Channel).HasConversion<string>().HasMaxLength(12);
        builder.Property(b => b.PaymentStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(b => b.CustomerNote).HasMaxLength(BookingRules.MaxNoteLength);
        builder.Property(b => b.CancellationReason).HasMaxLength(BookingRules.MaxReasonLength);
        builder.Property<NpgsqlRange<DateTime>>("During").HasColumnName(BookingModel.DuringColumn)
            .HasComputedColumnSql("tstzrange(starts_at, ends_at, '[)')", stored: true);

        builder.HasIndex(b => new { b.ShopId, b.StartsAt });
        builder.HasIndex(b => new { b.CustomerId, b.StartsAt });
        builder.HasIndex(b => new { b.ProfessionalId, b.StartsAt });

        builder.OwnsMany(b => b.PackageItems, i => i.ToJson());
        builder.OwnsMany(b => b.History, history =>
        {
            history.ToTable("booking_history");
            history.WithOwner().HasForeignKey("BookingId");
            history.HasKey(h => h.Id);
            history.Property(h => h.Id).UseIdentityAlwaysColumn();
            history.Property(h => h.Kind).HasConversion<string>().HasMaxLength(20);
            history.Property(h => h.FromStatus).HasConversion<string>().HasMaxLength(24);
            history.Property(h => h.ToStatus).HasConversion<string>().HasMaxLength(24);
            history.Property(h => h.ActorType).HasConversion<string>().HasMaxLength(20);
            history.Property(h => h.Reason).HasMaxLength(BookingRules.MaxReasonLength);
        });
        builder.Navigation(b => b.History).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(b => b.IsActive);
    }
}

internal sealed class BookingNoteConfiguration : IEntityTypeConfiguration<BookingNote>
{
    public void Configure(EntityTypeBuilder<BookingNote> builder)
    {
        builder.ToTable("booking_notes");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Text).HasMaxLength(BookingRules.MaxNoteLength);
        builder.HasShopScopedReference<BookingNote, Booking>(nameof(BookingNote.BookingId), DeleteBehavior.Cascade);
        builder.HasIndex(n => new { n.BookingId, n.CreatedAt });
    }
}
