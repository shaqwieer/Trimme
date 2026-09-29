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

/// <summary>What the customer liked (design c-rate "ما الذي أعجبك؟"): a fixed list, translated by the clients.</summary>
public enum ReviewTag
{
    Punctuality,
    Quality,
    Cleanliness,
    Manners,
    Price,
}

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
public sealed class Review : AggregateRoot<ReviewId>, ICustomerOwned, IPublicContent, IConcurrencyVersioned
{
    public const int MinRating = 1;
    public const int MaxRating = 5;
    public const int MaxCommentLength = 1000;
    public const int MaxNameLength = 120;
    public const int MaxReasonLength = 300;
    public const int MinReasonLength = 5;

    private Review(ReviewId id, ReviewedBooking booking, int rating, IReadOnlyCollection<ReviewTag> tags, string? comment, DateTimeOffset now)
        : base(id)
    {
        Tags = [.. tags.Distinct().Order()];
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

    public ReviewTag[] Tags { get; private set; } = [];

    public string? Comment { get; private set; }

    /// <summary>The public name: first name and surname initial (D-017, DV-S15).</summary>
    public string AuthorName { get; private set; }

    public string ItemNameAr { get; private set; }

    public string? ItemNameEn { get; private set; }

    public ReviewStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Why a staff member reported the review for moderation (<c>Admin.Reviews.Flag</c>); null when not reported.</summary>
    public string? FlagReason { get; private set; }

    public DateTimeOffset? FlaggedAt { get; private set; }

    /// <summary>Why a moderator hid the review; kept while it is hidden (D-102).</summary>
    public string? ModerationReason { get; private set; }

    /// <summary>The last hide or publish by a moderator.</summary>
    public DateTimeOffset? ModeratedAt { get; private set; }

    public uint Version { get; private set; }

    /// <summary>What moderators should look at (computed, never stored): a report, a low rating, a phone number in the text.</summary>
    public IReadOnlyList<ReviewFlag> Flags => ReviewModeration.FlagsOf(Rating, Comment, FlagReason is not null);

    /// <summary>A staff member reports a published review for a moderator (it stays published).</summary>
    public Result Flag(string? reason, DateTimeOffset now)
    {
        if (Status != ReviewStatus.Published)
        {
            return ReviewErrors.NotPublished();
        }

        if (FlagReason is not null)
        {
            return ReviewErrors.AlreadyFlagged();
        }

        if (ReviewModeration.ReasonError(reason) is { } error)
        {
            return error;
        }

        FlagReason = reason!.Trim();
        FlaggedAt = now;
        return Result.Success();
    }

    /// <summary>A moderator hides the review with a reason (D-017): it leaves the public pages and the ratings, never deleted.</summary>
    public Result Hide(string? reason, DateTimeOffset now)
    {
        if (Status == ReviewStatus.Hidden)
        {
            return ReviewErrors.AlreadyHidden();
        }

        if (ReviewModeration.ReasonError(reason) is { } error)
        {
            return error;
        }

        Status = ReviewStatus.Hidden;
        ModerationReason = reason!.Trim();
        ModeratedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// A moderator publishes the review: a hidden one returns to the pages and the ratings; a reported one is cleared
    /// (the design's «نشر»). Anything else has nothing to publish.
    /// </summary>
    public Result Publish(DateTimeOffset now)
    {
        if (Status == ReviewStatus.Published && FlagReason is null)
        {
            return ReviewErrors.NothingToPublish();
        }

        Status = ReviewStatus.Published;
        ModerationReason = null;
        FlagReason = null;
        FlaggedAt = null;
        ModeratedAt = now;
        return Result.Success();
    }

    /// <summary>A review of a completed booking, by its customer.</summary>
    public static Result<Review> Create(ReviewId id, ReviewedBooking booking, int rating, string? comment, DateTimeOffset now, IReadOnlyCollection<ReviewTag>? tags = null)
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

        return new Review(id, booking, rating, tags ?? [], text, now);
    }
}

/// <summary>Why a review is in the moderation queue (D-102).</summary>
public enum ReviewFlag
{
    /// <summary>Reported by a staff member with a reason.</summary>
    Reported,

    /// <summary>One or two stars.</summary>
    LowRating,

    /// <summary>The comment looks like it contains a phone number (eight or more digits, Latin or Arabic-Indic).</summary>
    ContainsPhone,
}

public static class ReviewModeration
{
    public const int LowRatingMax = 2;

    /// <summary>
    /// Eight or more digits (Latin, Arabic-Indic or Extended Arabic-Indic), optionally separated by one space, dot or dash.
    /// The same pattern runs in .NET and in PostgreSQL (<c>~</c>), so the list filter and the flag always agree.
    /// </summary>
    public const string PhonePattern = "([0-9٠-٩۰-۹][ .-]?){8,}";

    private static readonly System.Text.RegularExpressions.Regex Phone = new(PhonePattern, System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static bool ContainsPhone(string? text) => text is not null && Phone.IsMatch(text);

    public static IReadOnlyList<ReviewFlag> FlagsOf(int rating, string? comment, bool reported)
    {
        var flags = new List<ReviewFlag>(3);
        if (reported)
        {
            flags.Add(ReviewFlag.Reported);
        }

        if (rating <= LowRatingMax)
        {
            flags.Add(ReviewFlag.LowRating);
        }

        if (ContainsPhone(comment))
        {
            flags.Add(ReviewFlag.ContainsPhone);
        }

        return flags;
    }

    public static Error? ReasonError(string? reason) =>
        (reason?.Trim().Length ?? 0) < Review.MinReasonLength
            ? Error.Validation("validation.failed", "A reason is required.", new Dictionary<string, string[]> { ["reason"] = ["validation.reason_required"] })
            : reason!.Trim().Length > Review.MaxReasonLength
                ? Error.Validation("validation.failed", "The reason is too long.", new Dictionary<string, string[]> { ["reason"] = ["validation.too_long"] })
                : null;
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
    public static Error NotFound() => Error.NotFound("review.not_found", "The review was not found.");

    public static Error NotPublished() => Error.Conflict("review.not_published", "Only a published review can be reported.");

    public static Error AlreadyFlagged() => Error.Conflict("review.already_flagged", "The review has already been reported.");

    public static Error AlreadyHidden() => Error.Conflict("review.already_hidden", "The review is already hidden.");

    public static Error NothingToPublish() => Error.Conflict("review.nothing_to_publish", "The review is published and not reported.");

    public static Error NotCompleted() => Error.Conflict("review.booking_not_completed", "Only a completed booking can be reviewed.");

    public static Error AlreadyExists() => Error.Conflict("review.already_exists", "This booking has already been reviewed.");

    public static Error WindowClosed() =>
        Error.BusinessRule("review.window_closed", "The time to review this visit has passed.");

    public static Error BookingNotFound() => Error.NotFound("booking.not_found", "The booking was not found.");

    public static Error InvalidRating() =>
        Error.Validation("validation.failed", "The rating must be 1 to 5.",
            new Dictionary<string, string[]>(StringComparer.Ordinal) { ["rating"] = ["validation.out_of_range"] });

    public static Error CommentTooLong() =>
        Error.Validation("validation.failed", "The comment is too long.",
            new Dictionary<string, string[]>(StringComparer.Ordinal) { ["comment"] = ["validation.too_long"] });
}
