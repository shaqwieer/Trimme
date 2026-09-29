using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.Modules.Reviews.Domain;

public readonly record struct ReviewId(Guid Value) : IEntityId<ReviewId>
{
    public static ReviewId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

/// <summary>The booking a review is about, as the Reviews module received it (<c>IBookingReviewSource</c>).</summary>
public sealed record ReviewedBooking(
    Guid BookingId,
    ShopId ShopId,
    Guid? CustomerId,
    string CustomerName,
    ProfessionalId ProfessionalId,
    string ItemNameAr,
    string? ItemNameEn,
    DateTimeOffset? CompletedAt);

public enum ReviewStatus
{
    /// <summary>Shown on the shop and professional pages (post-moderation, D-017).</summary>
    Published,

    /// <summary>Hidden by a platform admin with a reason (Phase 14); kept, never deleted.</summary>
    Hidden,
}

/// <summary>
/// A customer's review of one completed booking (D-017, D-092): shop-owned and customer-owned, one per booking. It keeps
/// the author's public display name (first name and surname initial) and the booked item's name, snapshotted when it is
/// written, so the public page never needs the customer's account. Creation by customers arrives in Phase 12.
/// </summary>
public sealed class Review : AggregateRoot<ReviewId>, ICustomerOwned, IPublicContent
{
    public const int MinRating = 1;
    public const int MaxRating = 5;
    public const int MaxCommentLength = 1000;
    public const int MaxNameLength = 120;

    private Review(ReviewId id, ReviewedBooking booking, int rating, string? comment, DateTimeOffset now)
        : base(id)
    {
        ShopId = booking.ShopId;
        CustomerId = booking.CustomerId;
        BookingId = booking.BookingId;
        ProfessionalId = booking.ProfessionalId;
        Rating = rating;
        Comment = comment;
        AuthorName = ReviewAuthor.DisplayName(booking.CustomerName);
        ItemNameAr = booking.ItemNameAr;
        ItemNameEn = booking.ItemNameEn;
        Status = ReviewStatus.Published;
        CreatedAt = now;
    }

    private Review()
    {
        AuthorName = ItemNameAr = string.Empty;
    }

    public ShopId ShopId { get; private set; }

    public Guid? CustomerId { get; private set; }

    public Guid BookingId { get; private set; }

    public ProfessionalId ProfessionalId { get; private set; }

    public int Rating { get; private set; }

    public string? Comment { get; private set; }

    /// <summary>The public name: first name and surname initial (D-017, DV-S15).</summary>
    public string AuthorName { get; private set; }

    public string ItemNameAr { get; private set; }

    public string? ItemNameEn { get; private set; }

    public ReviewStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>A review of a completed booking, by its customer.</summary>
    public static Result<Review> Create(ReviewId id, ReviewedBooking booking, int rating, string? comment, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(booking);
        if (booking.CompletedAt is null)
        {
            return ReviewErrors.NotCompleted();
        }

        if (rating is < MinRating or > MaxRating)
        {
            return ReviewErrors.InvalidRating();
        }

        var text = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        if (text is { Length: > MaxCommentLength })
        {
            return ReviewErrors.CommentTooLong();
        }

        return new Review(id, booking, rating, text, now);
    }
}

/// <summary>
/// Rating totals of one shop or professional (D-092): a platform read model with no personal data, updated in the same
/// unit of work as the review. It is deliberately not shop-owned, so discovery can sort and filter every candidate shop by
/// rating in one query (reviewed allow-list of the tenancy architecture test).
/// </summary>
public sealed class RatingAggregate : IPublicContent
{
    private RatingAggregate(RatingSubjectKind subject, Guid subjectId, ShopId shopId)
    {
        Subject = subject;
        SubjectId = subjectId;
        ShopId = shopId;
    }

    private RatingAggregate()
    {
    }

    public RatingSubjectKind Subject { get; private set; }

    public Guid SubjectId { get; private set; }

    /// <summary>The shop the subject belongs to (the shop itself for a shop row).</summary>
    public ShopId ShopId { get; private set; }

    public int Count { get; private set; }

    public int Sum { get; private set; }

    public int Stars1 { get; private set; }

    public int Stars2 { get; private set; }

    public int Stars3 { get; private set; }

    public int Stars4 { get; private set; }

    public int Stars5 { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public decimal Average => Count == 0 ? 0 : Math.Round((decimal)Sum / Count, 1, MidpointRounding.AwayFromZero);

    public IReadOnlyList<int> Histogram => [Stars1, Stars2, Stars3, Stars4, Stars5];

    public static RatingAggregate For(RatingSubjectKind subject, Guid subjectId, ShopId shopId) => new(subject, subjectId, shopId);

    /// <summary>Adds (<paramref name="sign"/> = 1) or removes (−1) one published review of <paramref name="rating"/> stars.</summary>
    public void Apply(int rating, int sign, DateTimeOffset now)
    {
        if (rating is < Review.MinRating or > Review.MaxRating || sign is not (1 or -1))
        {
            throw new ArgumentOutOfRangeException(nameof(rating));
        }

        Count += sign;
        Sum += sign * rating;
        switch (rating)
        {
            case 1: Stars1 += sign; break;
            case 2: Stars2 += sign; break;
            case 3: Stars3 += sign; break;
            case 4: Stars4 += sign; break;
            default: Stars5 += sign; break;
        }

        UpdatedAt = now;
    }
}

public enum RatingSubjectKind
{
    Shop,
    Professional,
}

/// <summary>How a reviewer is shown publicly (D-017): the first name and the initial of the last word, skipping "ال".</summary>
public static class ReviewAuthor
{
    public static string DisplayName(string fullName)
    {
        var words = (fullName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return string.Empty;
        }

        if (words.Length == 1)
        {
            return words[0];
        }

        var last = words[^1];
        if (last.StartsWith("ال", StringComparison.Ordinal) && last.Length > 2)
        {
            last = last[2..];
        }

        return $"{words[0]} {char.ToUpperInvariant(last[0])}.";
    }
}

public static class ReviewErrors
{
    public static Error NotCompleted() => Error.Conflict("review.booking_not_completed", "Only a completed booking can be reviewed.");

    public static Error InvalidRating() =>
        Error.Validation("validation.failed", "The rating must be 1 to 5.",
            new Dictionary<string, string[]>(StringComparer.Ordinal) { ["rating"] = ["validation.out_of_range"] });

    public static Error CommentTooLong() =>
        Error.Validation("validation.failed", "The comment is too long.",
            new Dictionary<string, string[]>(StringComparer.Ordinal) { ["comment"] = ["validation.too_long"] });
}
