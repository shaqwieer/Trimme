using Shouldly;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.Modules.Bookings.Application;
using Trimme.Modules.Bookings.Domain;
using Trimme.Modules.Customers.Domain;
using Trimme.Modules.Reviews.Domain;

namespace Trimme.UnitTests.Bookings;

/// <summary>
/// Phase 12 domain rules (D-017, D-097, D-098): when a visit counts as completed and can be rated (Review action and
/// deadline), review tags, and favorites.
/// </summary>
public sealed class CustomerAccountDomainTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 4, 7, 0, 0, TimeSpan.Zero);
    private static readonly ShopId Shop = new(Guid.CreateVersion7());
    private static readonly Guid Customer = Guid.CreateVersion7();
    private static readonly BookedProfessional Majed = new(new ProfessionalId(Guid.CreateVersion7()), "ماجد", "Majed");

    private static Booking Seeded(Guid? customer, params BookingStatus[] path) =>
        Booking.Seeded(
            new BookingId(Guid.CreateVersion7()), Shop, customer, "سارة العنزي", Majed,
            new BookedItem(Guid.CreateVersion7(), null, "قص وتصفيف", null, 85m, "SAR", 30, []), Start, BookingChannel.Online, path, null, Start.AddDays(-1));

    private static CustomerBookingView View(DateTimeOffset now, params (Booking Booking, int Stars)[] rated) =>
        new(now, 120, 7, rated.ToDictionary(r => r.Booking.Id.Value, r => r.Stars));

    [Fact]
    public void CompletedAt_IsWhenTheVisitWasCompleted_AndNullOtherwise()
    {
        Seeded(Customer, BookingStatus.Confirmed, BookingStatus.Arrived, BookingStatus.Completed).CompletedAt.ShouldBe(Start.AddMinutes(30));
        Seeded(Customer, BookingStatus.Confirmed).CompletedAt.ShouldBeNull();
        Seeded(Customer, BookingStatus.Confirmed, BookingStatus.NoShow).CompletedAt.ShouldBeNull();
    }

    [Fact]
    public void ReviewAction_IsOfferedUntilTheWindowCloses_OnlyForAnUnratedCustomerVisit()
    {
        var visit = Seeded(Customer, BookingStatus.Confirmed, BookingStatus.Arrived, BookingStatus.Completed);
        var deadline = Start.AddMinutes(30).AddDays(7);

        var open = BookingMapping.ToCustomer(visit, null, View(deadline));
        open.AllowedActions.ShouldBe([CustomerBookingAction.Review]);
        open.ReviewDeadline.ShouldBe(deadline);
        open.ReviewRating.ShouldBeNull();

        BookingMapping.ToCustomer(visit, null, View(deadline.AddSeconds(1))).AllowedActions.ShouldBeEmpty();

        var rated = BookingMapping.ToCustomer(visit, null, View(Start.AddDays(1), (visit, 4)));
        rated.AllowedActions.ShouldBeEmpty();
        rated.ReviewRating.ShouldBe(4);
        rated.ReviewDeadline.ShouldBeNull();

        // A walk-in has no customer account to review with; an upcoming visit can be changed, not rated.
        BookingMapping.ToCustomer(Seeded(null, BookingStatus.Confirmed, BookingStatus.Arrived, BookingStatus.Completed), null, View(Start.AddDays(1)))
            .AllowedActions.ShouldBeEmpty();
        BookingMapping.ToCustomer(Seeded(Customer, BookingStatus.Confirmed), null, View(Start.AddDays(-1)))
            .AllowedActions.ShouldBe([CustomerBookingAction.Cancel, CustomerBookingAction.Reschedule]);
    }

    [Fact]
    public void Review_KeepsEachTagOnce_InTheFixedOrder()
    {
        var booking = new ReviewedBooking(Guid.CreateVersion7(), Shop, Customer, "سارة العنزي", Majed.Id, "قص وتصفيف", null, Start);
        var review = Review.Create(
            new ReviewId(Guid.CreateVersion7()), booking, 5, "  ممتاز  ", Start.AddDays(1),
            [ReviewTag.Price, ReviewTag.Punctuality, ReviewTag.Price]).Value;

        review.Tags.ShouldBe([ReviewTag.Punctuality, ReviewTag.Price]);
        review.Comment.ShouldBe("ممتاز");
        review.AuthorName.ShouldBe("سارة ع.");
        Review.Create(new ReviewId(Guid.CreateVersion7()), booking, 5, null, Start.AddDays(1)).Value.Tags.ShouldBeEmpty();
    }

    [Fact]
    public void Favorite_ForAShopOrAProfessional_BelongsToItsCustomer()
    {
        var shop = Favorite.ForShop(new FavoriteId(Guid.CreateVersion7()), Customer, Shop, Start);
        shop.ShopId.ShouldBe(Shop);
        shop.ProfessionalId.ShouldBeNull();
        shop.CustomerId.ShouldBe(Customer);

        var professional = Favorite.ForProfessional(new FavoriteId(Guid.CreateVersion7()), Customer, Shop, Majed.Id, Start);
        professional.ShopId.ShouldBe(Shop, "the professional's own shop, so the row stays in its tenant");
        professional.ProfessionalId.ShouldBe(Majed.Id);
        FavoriteErrors.LimitReached().Code.ShouldBe("favorites.limit_reached");
    }
}
