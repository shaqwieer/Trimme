using Shouldly;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Domain.Text;
using Trimme.Modules.Availability.Domain.Engine;
using Trimme.Modules.Reviews.Domain;

namespace Trimme.UnitTests.Discovery;

/// <summary>
/// Phase 11 pure rules: search-text normalization (D-091), the open-now status (same engine rules as the slots), the
/// public reviewer name (D-017) and the rating aggregate (D-092).
/// </summary>
public sealed class DiscoveryDomainTests
{
    private static readonly TimeZoneInfo Riyadh = TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");

    /// <summary>Sunday 4 October 2026 (Riyadh).</summary>
    private static readonly DateOnly Sunday = new(2026, 10, 4);

    private static DateTimeOffset Local(DateOnly date, int hour, int minute = 0) =>
        new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.FromHours(3)).AddHours(hour).AddMinutes(minute);

    private static int M(int hour, int minute = 0) => (hour * 60) + minute;

    [Theory]
    [InlineData("صالون الأصالة", "صالون الاصاله")]
    [InlineData("إبداع", "ابداع")]
    [InlineData("آفاق", "افاق")]
    [InlineData("مُصطفى", "مصطفي")]
    [InlineData("حلاقــة", "حلاقه")]
    [InlineData("مؤسسة", "موسسه")]
    [InlineData("Barber   House!", "barber house")]
    [InlineData("قص ٢٠٢٦", "قص 2026")]
    [InlineData("  ", "")]
    [InlineData(null, "")]
    public void SearchText_FoldsArabicVariants_CaseAndDigits(string? input, string expected) =>
        SearchText.Normalize(input).ShouldBe(expected);

    [Fact]
    public void SearchText_MatchesEveryQueryWord_InAnyOrder()
    {
        var text = SearchText.Normalize("صالون الأصالة للحلاقة Al Asala Barbershop الملقا");
        SearchText.Matches(text, SearchText.Normalize("الاصاله")).ShouldBeTrue();
        SearchText.Matches(text, SearchText.Normalize("barbershop الملقا")).ShouldBeTrue();
        SearchText.Matches(text, SearchText.Normalize("ASALA")).ShouldBeTrue();
        SearchText.Matches(text, SearchText.Normalize("حطين")).ShouldBeFalse();
        SearchText.Matches(text, string.Empty).ShouldBeTrue();
    }

    [Fact]
    public void OpenStatus_IsOpen_InsideAWindow_AndClosesAtItsEnd()
    {
        var shop = new ShopCalendar(Riyadh, [new WeeklyInterval(DayOfWeek.Sunday, M(9), M(23))], []);

        var (open, closes, next) = AvailabilityEngine.OpenStatus(shop, Local(Sunday, 12));

        open.ShouldBeTrue();
        closes.ShouldBe(Local(Sunday, 23));
        next.ShouldBeNull();
    }

    [Fact]
    public void OpenStatus_WhenClosed_GivesTheNextOpening_EvenDaysAhead()
    {
        var shop = new ShopCalendar(Riyadh, [new WeeklyInterval(DayOfWeek.Wednesday, M(14), M(22))], []);

        var (open, closes, next) = AvailabilityEngine.OpenStatus(shop, Local(Sunday, 23));

        open.ShouldBeFalse();
        closes.ShouldBeNull();
        next.ShouldBe(Local(Sunday.AddDays(3), 14));
    }

    [Fact]
    public void OpenStatus_WindowPastMidnight_IsOpenAfterMidnight_AndJoinsTouchingWindows()
    {
        // Saturday 21:00–02:00 (runs into Sunday) and Sunday 02:00–04:00 touch, so the shop closes at 04:00.
        var shop = new ShopCalendar(
            Riyadh,
            [new WeeklyInterval(DayOfWeek.Saturday, M(21), M(26)), new WeeklyInterval(DayOfWeek.Sunday, M(2), M(4))],
            []);

        var (open, closes, _) = AvailabilityEngine.OpenStatus(shop, Local(Sunday, 1, 30));

        open.ShouldBeTrue();
        closes.ShouldBe(Local(Sunday, 4));
    }

    [Fact]
    public void OpenStatus_AClosedDay_IsClosed_AndTheNextOpeningSkipsIt()
    {
        var hours = new[] { new WeeklyInterval(DayOfWeek.Sunday, M(9), M(17)), new WeeklyInterval(DayOfWeek.Monday, M(9), M(17)) };
        var shop = new ShopCalendar(Riyadh, hours, [new DateRange(Sunday, Sunday)]);

        var (open, _, next) = AvailabilityEngine.OpenStatus(shop, Local(Sunday, 10));

        open.ShouldBeFalse();
        next.ShouldBe(Local(Sunday.AddDays(1), 9));
    }

    [Fact]
    public void OpenStatus_WithoutHours_IsClosed_WithNoNextOpening()
    {
        var (open, closes, next) = AvailabilityEngine.OpenStatus(new ShopCalendar(Riyadh, [], []), Local(Sunday, 10));

        open.ShouldBeFalse();
        closes.ShouldBeNull();
        next.ShouldBeNull();
    }

    [Theory]
    [InlineData("محمد العنزي", "محمد ع.")]
    [InlineData("خالد الدوسري", "خالد د.")]
    [InlineData("نورة بنت سعد", "نورة س.")]
    [InlineData("Sara smith", "Sara S.")]
    [InlineData("فيصل", "فيصل")]
    [InlineData("  ", "")]
    public void ReviewAuthor_IsTheFirstNameAndTheSurnameInitial(string fullName, string expected) =>
        ReviewAuthor.DisplayName(fullName).ShouldBe(expected);

    [Fact]
    public void Review_NeedsACompletedBooking_AValidRating_AndABoundedComment()
    {
        var booking = new ReviewedBooking(
            Guid.NewGuid(), new ShopId(Guid.NewGuid()), Guid.NewGuid(), "نورة السبيعي", new ProfessionalId(Guid.NewGuid()), "حلاقة شعر", "Haircut", null);
        var now = Local(Sunday, 12);

        Review.Create(new ReviewId(Guid.NewGuid()), booking, 5, null, now).Error!.Code.ShouldBe("review.booking_not_completed");

        var completed = booking with { CompletedAt = now.AddHours(-2) };
        Review.Create(new ReviewId(Guid.NewGuid()), completed, 0, null, now).IsFailure.ShouldBeTrue();
        Review.Create(new ReviewId(Guid.NewGuid()), completed, 6, null, now).IsFailure.ShouldBeTrue();
        Review.Create(new ReviewId(Guid.NewGuid()), completed, 4, new string('x', Review.MaxCommentLength + 1), now).IsFailure.ShouldBeTrue();

        var review = Review.Create(new ReviewId(Guid.NewGuid()), completed, 4, "  ممتاز  ", now).Value;
        review.AuthorName.ShouldBe("نورة س.");
        review.Comment.ShouldBe("ممتاز");
        review.Status.ShouldBe(ReviewStatus.Published);
        review.ItemNameAr.ShouldBe("حلاقة شعر");
    }

    [Fact]
    public void RatingAggregate_KeepsCountSumAndHistogram_AndRoundsTheAverage()
    {
        var aggregate = RatingAggregate.For(RatingSubjectKind.Shop, Guid.NewGuid(), new ShopId(Guid.NewGuid()));
        aggregate.Average.ShouldBe(0);

        aggregate.Apply(5, 1, Local(Sunday, 10));
        aggregate.Apply(4, 1, Local(Sunday, 10));
        aggregate.Apply(4, 1, Local(Sunday, 10));

        aggregate.Count.ShouldBe(3);
        aggregate.Average.ShouldBe(4.3m);
        aggregate.Histogram.ShouldBe([0, 0, 0, 2, 1]);

        aggregate.Apply(4, -1, Local(Sunday, 11));
        aggregate.Count.ShouldBe(2);
        aggregate.Average.ShouldBe(4.5m);
        aggregate.Histogram.ShouldBe([0, 0, 0, 1, 1]);
        Should.Throw<ArgumentOutOfRangeException>(() => aggregate.Apply(6, 1, Local(Sunday, 11)));
    }
}
