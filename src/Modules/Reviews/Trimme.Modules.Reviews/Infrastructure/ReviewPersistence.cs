using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trimme.BuildingBlocks.Application.Discovery;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Reviews.Domain;

namespace Trimme.Modules.Reviews.Infrastructure;

internal static class ReviewModel
{
    /// <summary>The Professionals module's entity, referenced by name only (modules talk through contracts).</summary>
    public const string ProfessionalEntityType = "Trimme.Modules.Professionals.Domain.Professional";
}

internal sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.ToTable("reviews", t => t.HasCheckConstraint("ck_reviews_rating", "rating BETWEEN 1 AND 5"));
        builder.HasKey(r => r.Id);

        // One review per booking (D-017). The booking's key type belongs to the Bookings module, so this is a unique
        // column rather than a foreign key; the review command checks the booking through IBookingReviewSource (D-092).
        builder.HasIndex(r => r.BookingId).IsUnique();
        builder.HasShopScopedReference(ReviewModel.ProfessionalEntityType, nameof(Review.ProfessionalId));
        builder.HasIndex(r => new { r.ShopId, r.Status, r.CreatedAt });
        builder.HasIndex(r => new { r.ProfessionalId, r.Status, r.CreatedAt });
        builder.HasIndex(r => r.CustomerId);

        builder.Property(r => r.Comment).HasMaxLength(Review.MaxCommentLength);
        builder.Property(r => r.AuthorName).HasMaxLength(Review.MaxNameLength);
        builder.Property(r => r.ItemNameAr).HasMaxLength(Review.MaxNameLength);
        builder.Property(r => r.ItemNameEn).HasMaxLength(Review.MaxNameLength);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
    }
}

internal sealed class RatingAggregateConfiguration : IEntityTypeConfiguration<RatingAggregate>
{
    public void Configure(EntityTypeBuilder<RatingAggregate> builder)
    {
        builder.ToTable("rating_aggregates", t => t.HasCheckConstraint("ck_rating_aggregates_count", "count >= 0 AND count = stars1 + stars2 + stars3 + stars4 + stars5"));
        builder.HasKey(a => new { a.Subject, a.SubjectId });
        builder.Property(a => a.Subject).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(a => a.ShopId);
        builder.Ignore(a => a.Average);
        builder.Ignore(a => a.Histogram);
    }
}

/// <summary><see cref="IRatingReader"/>: the rating aggregates, a platform read model (no data scope needed).</summary>
internal sealed class RatingReader(TrimmeDbContext db) : IRatingReader
{
    public async Task<IReadOnlyDictionary<Guid, RatingSummary>> GetAsync(RatingSubject subject, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, RatingSummary>();
        }

        var kind = subject == RatingSubject.Shop ? RatingSubjectKind.Shop : RatingSubjectKind.Professional;
        var wanted = ids.Distinct().ToArray();
        var rows = await db.Set<RatingAggregate>().AsNoTracking()
            .Where(a => a.Subject == kind && wanted.Contains(a.SubjectId) && a.Count > 0)
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(a => a.SubjectId, a => new RatingSummary(a.Average, a.Count, a.Histogram));
    }
}
