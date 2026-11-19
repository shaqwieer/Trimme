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
            (BookingStatus.Confirmed, BookingStatus.Completed),
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
        // Long enough after the 30-minute booking for every time rule to allow it: it is red, so even Confirmed → Completed
        // (closing it late, D-147) is open.
        var at = Start.AddMinutes(50);
        var booking = InStatus(from);
        var historyBefore = booking.History.Count;
        var result = booking.ApplyShopTransition(to, ShopUser, "العميل اتصل", at);

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
                h => h.OccurredAt.ShouldBe(at));
        }
        else
        {
            result.Error!.Code.ShouldBe("booking.invalid_transition");
            booking.Status.ShouldBe(from);
            booking.History.Count.ShouldBe(historyBefore);
        }
    }

    [Fact]
    public void FinishingEarly_AfterArrival_EndsTheVisitThen()
    {
        // «انتهت الخدمة» (D-135) after «وصل» (D-147): a 30-minute visit finished after 20½ minutes ends at the 20th minute.
        var early = Online();
        early.ApplyShopTransition(BookingStatus.Arrived, ShopUser, null, Start).IsSuccess.ShouldBeTrue();
        early.ApplyShopTransition(BookingStatus.Completed, ShopUser, null, Start.AddMinutes(20).AddSeconds(30)).IsSuccess.ShouldBeTrue();
        early.Status.ShouldBe(BookingStatus.Completed);
        early.EndsAt.ShouldBe(Start.AddMinutes(20));
        early.DurationMinutes.ShouldBe(30, "the booked service keeps its duration");

        // Finished late, or before the start after an early arrival: the booked times stay.
        var late = Online();
        late.ApplyShopTransition(BookingStatus.Arrived, ShopUser, null, Start).IsSuccess.ShouldBeTrue();
        late.ApplyShopTransition(BookingStatus.Completed, ShopUser, null, Start.AddMinutes(45)).IsSuccess.ShouldBeTrue();
        late.EndsAt.ShouldBe(Start.AddMinutes(30));
        var arrivedEarly = Online();
        arrivedEarly.ApplyShopTransition(BookingStatus.Arrived, ShopUser, null, Start.AddMinutes(-40)).IsSuccess.ShouldBeTrue();
        arrivedEarly.ApplyShopTransition(BookingStatus.Completed, ShopUser, null, Start.AddMinutes(-5)).IsSuccess.ShouldBeTrue();
        arrivedEarly.EndsAt.ShouldBe(Start.AddMinutes(30));
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
    public void TimeRules_ArrivedFromAnHourBefore_NoShowAQuarterHourLate_FinishAfterArrival_CancelNeedsAReason()
    {
        var booking = Online();
        booking.ApplyShopTransition(BookingStatus.Arrived, ShopUser, null, Start.AddMinutes(-61)).Error!.Code.ShouldBe("booking.too_early");
        booking.ApplyShopTransition(BookingStatus.NoShow, ShopUser, null, Start.AddMinutes(14)).Error!.Code.ShouldBe("booking.too_early");
        booking.AllowedShopTransitions(Start.AddMinutes(-90)).ShouldBe([BookingStatus.CancelledByShop]);
        booking.AllowedShopTransitions(Start).ShouldBe([BookingStatus.Arrived, BookingStatus.CancelledByShop]);
        booking.AllowedShopTransitions(Start.AddMinutes(15)).ShouldBe([BookingStatus.Arrived, BookingStatus.NoShow, BookingStatus.CancelledByShop]);
        booking.ApplyShopTransition(BookingStatus.Completed, ShopUser, null, Start.AddMinutes(1)).Error!.Code.ShouldBe("booking.arrival_required");
        booking.ApplyShopTransition(BookingStatus.CancelledByShop, ShopUser, " ", Now).Error!.Code.ShouldBe("validation.failed");

        booking.ApplyShopTransition(BookingStatus.Arrived, ShopUser, null, Start.AddMinutes(-60)).IsSuccess.ShouldBeTrue();
        booking.ApplyShopTransition(BookingStatus.Completed, ShopUser, null, Start.AddMinutes(30)).IsSuccess.ShouldBeTrue();
        BookingStateMachine.IsTerminal(booking.Status).ShouldBeTrue();
        booking.AllowedShopTransitions(Start.AddHours(1)).ShouldBeEmpty();
    }

    [Fact]
    public void RedBookings_ActualTimes_LateFlags_AndNames_D147()
    {
        var sultan = new BookingActor(Guid.CreateVersion7(), ActorType.ShopUser, "  سلطان  ");

        // A walk-in that starts now started now; a phone booking never starts at once.
        Booking.CreateWalkIn(new BookingId(Guid.CreateVersion7()), Shop, "زائر", Faisal, Haircut(), Now, startsNow: true, null, sultan, Now)
            .ShouldSatisfyAllConditions(b => b.ActualStartAt.ShouldBe(Now), b => b.IsShopRecorded.ShouldBeTrue(), b => b.History[0].ActorName.ShouldBe("سلطان"));
        Booking.CreateWalkIn(new BookingId(Guid.CreateVersion7()), Shop, "زائر", Faisal, Haircut(), Start, startsNow: true, null, sultan, Now, BookingChannel.Phone)
            .ShouldSatisfyAllConditions(b => b.Status.ShouldBe(BookingStatus.Confirmed), b => b.Channel.ShouldBe(BookingChannel.Phone), b => b.ActualStartAt.ShouldBeNull());
        Should.Throw<ArgumentOutOfRangeException>(() =>
            Booking.CreateWalkIn(new BookingId(Guid.CreateVersion7()), Shop, "زائر", Faisal, Haircut(), Start, false, null, sultan, Now, BookingChannel.Online));

        // «وصل» then «انتهت الخدمة»: the actual start and end, on time.
        var served = Online();
        served.ApplyShopTransition(BookingStatus.Arrived, sultan, null, Start.AddMinutes(2)).IsSuccess.ShouldBeTrue();
        served.ApplyShopTransition(BookingStatus.Completed, sultan, null, Start.AddMinutes(25)).IsSuccess.ShouldBeTrue();
        served.ShouldSatisfyAllConditions(
            b => b.ActualStartAt.ShouldBe(Start.AddMinutes(2)),
            b => b.CompletedAt.ShouldBe(Start.AddMinutes(25)),
            b => b.ClosedLate.ShouldBeFalse(),
            b => b.ClosingEntry!.ActorName.ShouldBe("سلطان"));

        // Red 15 minutes after the booked end; closed then, even from Confirmed, it is «انقفل متأخر».
        var forgotten = Online();
        forgotten.IsOverdue(Start.AddMinutes(45)).ShouldBeFalse();
        forgotten.IsOverdue(Start.AddMinutes(46)).ShouldBeTrue();
        forgotten.AllowedShopTransitions(Start.AddMinutes(46)).ShouldContain(BookingStatus.Completed);
        forgotten.ApplyShopTransition(BookingStatus.Completed, sultan, null, Start.AddHours(5)).IsSuccess.ShouldBeTrue();
        forgotten.ShouldSatisfyAllConditions(
            b => b.ClosedLate.ShouldBeTrue(), b => b.ActualStartAt.ShouldBeNull(), b => b.IsOverdue(Start.AddHours(6)).ShouldBeFalse());

        // A cancellation in the last quarter hour (or after the start) is late; one before it is not.
        var early = Online();
        early.ApplyShopTransition(BookingStatus.CancelledByShop, sultan, "مرض", Start.AddMinutes(-16)).IsSuccess.ShouldBeTrue();
        early.CancelledLate.ShouldBeFalse();
        var late = Online();
        late.ApplyShopTransition(BookingStatus.CancelledByShop, sultan, "مرض", Start.AddMinutes(-14)).IsSuccess.ShouldBeTrue();
        late.CancelledLate.ShouldBeTrue();

        // Once arrived, it cannot be cancelled.
        var arrived = Online();
        arrived.ApplyShopTransition(BookingStatus.Arrived, sultan, null, Start).IsSuccess.ShouldBeTrue();
        arrived.ApplyShopTransition(BookingStatus.CancelledByShop, sultan, "مرض", Start.AddMinutes(1)).Error!.Code.ShouldBe("booking.invalid_transition");
    }

    [Fact]
    public void References_AreGhAndFiveUnambiguousCharacters_D148()
    {
        var references = Enumerable.Range(0, 200).Select(_ => BookingRules.NewReference()).ToList();
        references.ShouldAllBe(r => System.Text.RegularExpressions.Regex.IsMatch(r, "^GH-[ABCDEFGHJKMNPQRSTUVWXYZ23456789]{5}$"));
        references.Distinct().Count().ShouldBeGreaterThan(190, "drawn at random");
        Online().Reference.Length.ShouldBeLessThanOrEqualTo(BookingRules.MaxReferenceLength);
    }

    [Fact]
    public void WithNoWindow_TheCustomerCancelsAndChangesUntilTheStart_D148()
    {
        var booking = Online();
        booking.CustomerCanChange(Start.AddMinutes(-1), 0).ShouldBeTrue();
        booking.CustomerCanChange(Start.AddMinutes(1), 0).ShouldBeFalse();
        booking.CancelByCustomer(Customer, null, Start.AddMinutes(1), 0).Error!.Code.ShouldBe("booking.cancellation_cutoff_passed");
        booking.CancelByCustomer(Customer, null, Start.AddMinutes(-1), 0).IsSuccess.ShouldBeTrue();
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

    // ---------------------------------------------------------------- D-145: another barber, and the customer's window

    private static readonly BookedProfessional Omar = new(new ProfessionalId(Guid.CreateVersion7()), "عمر", "Omar");

    [Fact]
    public void ChangingTheBarber_KeepsTheTime_RecordsWhoHadIt_AndOpensTheCustomersWindowUntilTheStart()
    {
        var booking = Online(start: Now.AddMinutes(90));
        booking.CustomerCanChange(Now, cutoffMinutes: 120).ShouldBeFalse();

        booking.ChangeProfessional(Omar, ShopUser, Now).IsSuccess.ShouldBeTrue();
        booking.ProfessionalId.ShouldBe(Omar.Id);
        booking.StartsAt.ShouldBe(Now.AddMinutes(90));
        var entry = booking.History[^1];
        entry.Kind.ShouldBe(BookingEventKind.ProfessionalChanged);
        entry.PreviousProfessionalId.ShouldBe(Faisal.Id.Value);
        entry.PreviousProfessionalNameAr.ShouldBe("فيصل");

        // Inside the window, the customer may now cancel or change it, until it starts.
        booking.CustomerCanChange(Now, cutoffMinutes: 120).ShouldBeTrue();
        booking.CustomerCanChange(Now.AddMinutes(90), cutoffMinutes: 120).ShouldBeFalse();
        booking.CancelByCustomer(Customer, null, Now.AddMinutes(30), cutoffMinutes: 120).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void ChangingTheBarber_IsRefused_ForTheSameBarber_AStartedVisit_OrAFinishedBooking()
    {
        Online().ChangeProfessional(Faisal, ShopUser, Now).Error!.Code.ShouldBe("booking.same_professional");
        Online(start: Now.AddMinutes(-5)).ChangeProfessional(Omar, ShopUser, Now).IsFailure.ShouldBeTrue();
        InStatus(BookingStatus.Arrived).ChangeProfessional(Omar, ShopUser, Now).Error!.Code.ShouldBe("booking.not_reschedulable");
        InStatus(BookingStatus.Completed).ChangeProfessional(Omar, ShopUser, Now).Error!.Code.ShouldBe("booking.not_reschedulable");
    }

    [Fact]
    public void TheCustomersOwnReschedule_UsesUpTheFreeChange_AShopReschedule_DoesNot()
    {
        var booking = Online(start: Now.AddMinutes(90));
        booking.ChangeProfessional(Omar, ShopUser, Now).IsSuccess.ShouldBeTrue();

        // The shop moves the time: still the customer's free change.
        booking.Reschedule(Now.AddMinutes(100), Omar, ShopUser, Now, cutoffMinutes: null).IsSuccess.ShouldBeTrue();
        booking.CustomerCanChange(Now, cutoffMinutes: 120).ShouldBeTrue();

        // The customer chooses a time themselves: the window applies again.
        booking.Reschedule(Now.AddMinutes(110), Omar, new BookingActor(Customer, ActorType.Customer), Now, cutoffMinutes: 120).IsSuccess.ShouldBeTrue();
        booking.ProfessionalChangedAt.ShouldBeNull();
        booking.CustomerCanChange(Now, cutoffMinutes: 120).ShouldBeFalse();
    }

    [Fact]
    public void TheCleaningTime_FollowsTheEnd_ThroughEveryChange()
    {
        // D-149: a booking holds its barber until its end plus the shop's cleaning time at booking time.
        var booking = Booking.CreateOnline(
            new BookingId(Guid.CreateVersion7()), Shop, Customer, "نورة", Faisal, Haircut(), Start, false, null, Now, bufferMinutes: 10);
        booking.BufferMinutes.ShouldBe(10);
        booking.EndsAt.ShouldBe(Start.AddMinutes(30));
        booking.BlockedUntil.ShouldBe(Start.AddMinutes(40));

        // Another barber with their own length: the booking takes it, the cleaning time follows.
        var omar = new BookedProfessional(new ProfessionalId(Guid.CreateVersion7()), "عمر", "Omar");
        booking.Reschedule(Start.AddHours(1), omar, ShopUser, Now, cutoffMinutes: null, length: new BookedLength(45, new Dictionary<Guid, int>())).IsSuccess.ShouldBeTrue();
        booking.DurationMinutes.ShouldBe(45);
        booking.EndsAt.ShouldBe(Start.AddHours(1).AddMinutes(45));
        booking.BlockedUntil.ShouldBe(Start.AddHours(1).AddMinutes(55));

        // Back to the first barber at the same time, for their own 30 minutes.
        booking.ChangeProfessional(Faisal, ShopUser, Now, new BookedLength(30, new Dictionary<Guid, int>())).IsSuccess.ShouldBeTrue();
        booking.EndsAt.ShouldBe(Start.AddHours(1).AddMinutes(30));
        booking.BlockedUntil.ShouldBe(Start.AddHours(1).AddMinutes(40));

        // The shop lowered its cleaning time since: the booking placed again takes the time its new place was checked with.
        booking.Reschedule(Start.AddHours(2), Faisal, ShopUser, Now, cutoffMinutes: null, bufferMinutes: 0).IsSuccess.ShouldBeTrue();
        booking.BufferMinutes.ShouldBe(0);
        booking.BlockedUntil.ShouldBe(booking.EndsAt);

        // A walk-in without cleaning time holds the barber until its end.
        var walkIn = Booking.CreateWalkIn(new BookingId(Guid.CreateVersion7()), Shop, "زائر", Faisal, Haircut(), Start, startsNow: false, null, ShopUser, Now);
        walkIn.BlockedUntil.ShouldBe(walkIn.EndsAt);
    }
}
