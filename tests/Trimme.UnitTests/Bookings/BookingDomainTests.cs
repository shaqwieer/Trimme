using Shouldly;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.Modules.Availability.Domain.Engine;
using Trimme.Modules.Bookings.Domain;

namespace Trimme.UnitTests.Bookings;

/// <summary>
/// R-BKG-01/02 (D-016, D-087): the state machine (every pair), its time rules, the customer cutoff, reschedule and the
/// snapshot; and the walk-in availability rule (D-088).
/// </summary>
public sealed class BookingDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Start = Now.AddDays(1);
    private static readonly ShopId Shop = new(Guid.CreateVersion7());
    private static readonly Guid Customer = Guid.CreateVersion7();
    private static readonly BookedProfessional Faisal = new(new ProfessionalId(Guid.CreateVersion7()), "فيصل", "Faisal");
    private static readonly BookingActor ShopUser = new(Guid.CreateVersion7(), ActorType.ShopUser);

    private static BookedItem Haircut(decimal price = 60m) => new(Guid.CreateVersion7(), null, "حلاقة", "Haircut", price, "SAR", 30, []);

    private static Booking Online(bool manual = false, DateTimeOffset? start = null) =>
        Booking.CreateOnline(new BookingId(Guid.CreateVersion7()), Shop, Customer, " نورة ", Faisal, Haircut(), start ?? Start, manual, " قريب ", Now);

    private static Booking InStatus(BookingStatus status)
    {
        var path = status switch
        {
            BookingStatus.Pending => new[] { BookingStatus.Pending },
            BookingStatus.Confirmed => [BookingStatus.Confirmed],
            BookingStatus.Arrived => [BookingStatus.Confirmed, BookingStatus.Arrived],
            BookingStatus.Completed => [BookingStatus.Confirmed, BookingStatus.Arrived, BookingStatus.Completed],
            BookingStatus.CancelledByCustomer => [BookingStatus.Confirmed, BookingStatus.CancelledByCustomer],
            BookingStatus.CancelledByShop => [BookingStatus.Confirmed, BookingStatus.CancelledByShop],
            _ => [BookingStatus.Confirmed, BookingStatus.NoShow],
        };
        return Booking.Seeded(new BookingId(Guid.CreateVersion7()), Shop, Customer, "نورة", Faisal, Haircut(), Start, BookingChannel.Online, path, "سبب", Now);
    }

    public static TheoryData<BookingStatus, BookingStatus, bool> EveryPair()
    {
        var allowed = new HashSet<(BookingStatus, BookingStatus)>
        {
            (BookingStatus.Pending, BookingStatus.Confirmed),
            (BookingStatus.Pending, BookingStatus.CancelledByCustomer),
            (BookingStatus.Pending, BookingStatus.CancelledByShop),
            (BookingStatus.Confirmed, BookingStatus.Arrived),
            (BookingStatus.Confirmed, BookingStatus.NoShow),
            (BookingStatus.Confirmed, BookingStatus.CancelledByCustomer),
            (BookingStatus.Confirmed, BookingStatus.CancelledByShop),
            (BookingStatus.Arrived, BookingStatus.Completed),
        };
        var data = new TheoryData<BookingStatus, BookingStatus, bool>();
        foreach (var from in Enum.GetValues<BookingStatus>())
        {
            foreach (var to in Enum.GetValues<BookingStatus>())
            {
                data.Add(from, to, allowed.Contains((from, to)));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryPair))]
    public void StateMachine_AllowsExactlyTheDesignedTransitions(BookingStatus from, BookingStatus to, bool allowed) =>
        BookingStateMachine.CanTransition(from, to).ShouldBe(allowed);

    [Theory]
    [MemberData(nameof(EveryPair))]
    public void ShopTransitions_FollowTheStateMachine_AndRecordHistory(BookingStatus from, BookingStatus to, bool allowed)
    {
        var booking = InStatus(from);
        var historyBefore = booking.History.Count;
        var result = booking.ApplyShopTransition(to, ShopUser, "العميل اتصل", Start.AddMinutes(1));

        // Customers cancel through their own command, never through a shop transition.
        var expected = allowed && to != BookingStatus.CancelledByCustomer;
        result.IsSuccess.ShouldBe(expected);
        if (expected)
        {
            booking.Status.ShouldBe(to);
            booking.History.Count.ShouldBe(historyBefore + 1);
            booking.History[^1].ShouldSatisfyAllConditions(
                h => h.FromStatus.ShouldBe(from),
                h => h.ToStatus.ShouldBe(to),
                h => h.ActorId.ShouldBe(ShopUser.Id),
                h => h.ActorType.ShouldBe(ActorType.ShopUser),
                h => h.OccurredAt.ShouldBe(Start.AddMinutes(1)));
        }
        else
        {
            result.Error!.Code.ShouldBe("booking.invalid_transition");
            booking.Status.ShouldBe(from);
            booking.History.Count.ShouldBe(historyBefore);
        }
    }

    [Fact]
    public void OnlineBookings_StartConfirmed_OrPendingUnderManualConfirmation()
    {
        var auto = Online();
        auto.Status.ShouldBe(BookingStatus.Confirmed);
        auto.CustomerName.ShouldBe("نورة");
        auto.CustomerNote.ShouldBe("قريب");
        auto.Reference.Length.ShouldBe(8);
        auto.EndsAt.ShouldBe(Start.AddMinutes(30));
        auto.PaymentStatus.ShouldBe(PaymentStatus.NotApplicable);
        auto.AmountDue.ShouldBe(60m);
        auto.History.Single().ShouldSatisfyAllConditions(h => h.Kind.ShouldBe(BookingEventKind.Created), h => h.ActorType.ShouldBe(ActorType.Customer));

        Online(manual: true).Status.ShouldBe(BookingStatus.Pending);
    }

    [Fact]
    public void WalkIns_StartingNow_AreArrived_OthersConfirmed()
    {
        Booking.CreateWalkIn(new BookingId(Guid.CreateVersion7()), Shop, "زائر", Faisal, Haircut(), Now, startsNow: true, null, ShopUser, Now)
            .ShouldSatisfyAllConditions(b => b.Status.ShouldBe(BookingStatus.Arrived), b => b.CustomerId.ShouldBeNull(), b => b.Channel.ShouldBe(BookingChannel.WalkIn));
        Booking.CreateWalkIn(new BookingId(Guid.CreateVersion7()), Shop, "زائر", Faisal, Haircut(), Start, startsNow: false, null, ShopUser, Now)
            .Status.ShouldBe(BookingStatus.Confirmed);
    }

    [Fact]
    public void TimeRules_ArrivedFromAnHourBefore_NoShowOnlyAfterTheStart_CancelNeedsAReason()
    {
        var booking = Online();
        booking.ApplyShopTransition(BookingStatus.Arrived, ShopUser, null, Start.AddMinutes(-61)).Error!.Code.ShouldBe("booking.too_early");
        booking.ApplyShopTransition(BookingStatus.NoShow, ShopUser, null, Start.AddMinutes(-1)).Error!.Code.ShouldBe("booking.too_early");
        booking.AllowedShopTransitions(Start.AddMinutes(-90)).ShouldBe([BookingStatus.CancelledByShop]);
        booking.AllowedShopTransitions(Start).ShouldBe([BookingStatus.Arrived, BookingStatus.NoShow, BookingStatus.CancelledByShop]);
        booking.ApplyShopTransition(BookingStatus.CancelledByShop, ShopUser, " ", Now).Error!.Code.ShouldBe("validation.failed");

        booking.ApplyShopTransition(BookingStatus.Arrived, ShopUser, null, Start.AddMinutes(-60)).IsSuccess.ShouldBeTrue();
        booking.ApplyShopTransition(BookingStatus.Completed, ShopUser, null, Start.AddMinutes(30)).IsSuccess.ShouldBeTrue();
        BookingStateMachine.IsTerminal(booking.Status).ShouldBeTrue();
        booking.AllowedShopTransitions(Start.AddHours(1)).ShouldBeEmpty();
    }

    [Fact]
    public void CustomerCancel_WorksUntilTheCutoff_ExactlyAtTheBoundary()
    {
        var booking = Online();
        booking.CustomerCanChange(Start.AddMinutes(-120), 120).ShouldBeTrue();
        booking.CancelByCustomer(Customer, null, Start.AddMinutes(-119), 120).Error!.Code.ShouldBe("booking.cancellation_cutoff_passed");

        booking.CancelByCustomer(Customer, " سفر ", Start.AddMinutes(-120), 120).IsSuccess.ShouldBeTrue();
        booking.Status.ShouldBe(BookingStatus.CancelledByCustomer);
        booking.CancellationReason.ShouldBe("سفر");
        booking.IsActive.ShouldBeFalse();
        booking.CancelByCustomer(Customer, null, Now, 120).Error!.Code.ShouldBe("booking.invalid_transition");
    }

    [Fact]
    public void Reschedule_KeepsTheSnapshotAndStatus_RecordsTheMove_AndRespectsTheCutoff()
    {
        var booking = Online(manual: true);
        var omar = new BookedProfessional(new ProfessionalId(Guid.CreateVersion7()), "عمر", "Omar");
        var later = Start.AddHours(3);

        booking.Reschedule(later, omar, new BookingActor(Customer, ActorType.Customer), Start.AddMinutes(-60), 120).Error!.Code.ShouldBe("booking.cancellation_cutoff_passed");
        booking.Reschedule(later, omar, new BookingActor(Customer, ActorType.Customer), Now, 120).IsSuccess.ShouldBeTrue();
        booking.ShouldSatisfyAllConditions(
            b => b.StartsAt.ShouldBe(later),
            b => b.EndsAt.ShouldBe(later.AddMinutes(30)),
            b => b.ProfessionalId.ShouldBe(omar.Id),
            b => b.ProfessionalNameEn.ShouldBe("Omar"),
            b => b.Status.ShouldBe(BookingStatus.Pending),
            b => b.Price.ShouldBe(60m),
            b => b.ItemNameAr.ShouldBe("حلاقة"));
        booking.History[^1].ShouldSatisfyAllConditions(
            h => h.Kind.ShouldBe(BookingEventKind.Rescheduled),
            h => h.PreviousStartsAt.ShouldBe(Start));

        InStatus(BookingStatus.Arrived).Reschedule(later, omar, ShopUser, Now, null).Error!.Code.ShouldBe("booking.not_reschedulable");
    }

    [Fact]
    public void ActiveStatuses_AreTheOnesTheExclusionConstraintCovers()
    {
        BookingRules.Active.Select(s => s.ToString()).ShouldBe(["Pending", "Confirmed", "Arrived"]);
        Enum.GetValues<BookingStatus>().Where(BookingRules.IsActive).ShouldBe(BookingRules.Active);
    }

    [Fact]
    public void References_AreEightUnambiguousCharacters()
    {
        var references = Enumerable.Range(0, 200).Select(_ => BookingRules.NewReference()).ToList();
        references.ShouldAllBe(r => r.Length == 8 && !r.Any(c => "01IOL".Contains(c)));
        references.Distinct().Count().ShouldBe(200);
    }

    [Fact]
    public void WalkInRule_AllowsAnyMinute_ButNotACollision()
    {
        var riyadh = TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");
        var sunday = new DateOnly(2026, 10, 4);
        DateTimeOffset At(int hour, int minute) => new DateTimeOffset(sunday.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.FromHours(3));
        var shop = new ShopCalendar(riyadh, [new WeeklyInterval(DayOfWeek.Sunday, 9 * 60, 12 * 60)], []);
        var booked = new InstantRange(At(10, 30), At(11, 0));
        var pro = new ProfessionalCalendar(Faisal.Id, null, [new BreakRule([DayOfWeek.Sunday], null, 11 * 60 + 30, 11 * 60 + 45)], [booked]);

        AvailabilityEngine.IsFree(shop, pro, At(10, 2), 25).ShouldBeTrue("any minute, no grid");
        AvailabilityEngine.IsFree(shop, pro, At(10, 2), 30).ShouldBeFalse("runs into a booking");
        AvailabilityEngine.IsFree(shop, pro, At(11, 20), 15).ShouldBeFalse("runs into the break");
        AvailabilityEngine.IsFree(shop, pro, At(8, 50), 20).ShouldBeFalse("before opening");
        AvailabilityEngine.IsFree(shop, pro, At(11, 45), 15).ShouldBeTrue("up to closing");
        AvailabilityEngine.FreeTime(shop, pro, sunday, sunday).Ranges.Count.ShouldBe(3);
    }
}
