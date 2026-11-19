using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Loyalty;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Scheduling;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Bookings.Domain;

namespace Trimme.Modules.Bookings.Application;

// The owner's reports and expected income (D-147, «٢-٤ التقارير والدخل المتوقع»): for each barber and the whole shop, over
// a run of the shop's business days, what happened to the bookings that started then. No payment is taken online, so
// income is only ever "expected": the booked price of completed visits, a free loyalty service counting zero.

/// <param name="Bookings">Bookings that started in the period, cancellations included.</param>
/// <param name="Completed">Finished («انتهت الخدمة»).</param>
/// <param name="NoShows">«ما حضر».</param>
/// <param name="Cancelled">Cancelled by the customer or the shop.</param>
/// <param name="LateCancellations">Cancelled in the last quarter hour before the start, or after it.</param>
/// <param name="MovedIn">Bookings given to this barber by a colleague («نقل الحجز»).</param>
/// <param name="MovedOut">Bookings this barber gave to a colleague.</param>
/// <param name="Postponed">Times a booking got a new time from the shop: a walk-in or phone booking changed, or an offer the customer accepted.</param>
/// <param name="ClosedLate">Closed while red («انقفل متأخر»).</param>
/// <param name="Unclosed">Still open a quarter hour past their end.</param>
/// <param name="Breaks">The barber's own breaks on those days.</param>
/// <param name="BreakMinutes">Their minutes.</param>
/// <param name="AverageMinutes">Average actual length («وصل» to «انتهت الخدمة») of completed visits not closed late; null without any.</param>
/// <param name="ShortVisits">Completed visits shorter than five minutes (perhaps finished without a service).</param>
/// <param name="ExpectedIncome">Expected income of completed visits.</param>
public sealed record ReportCountsResponse(
    int Bookings,
    int Completed,
    int NoShows,
    int Cancelled,
    int LateCancellations,
    int MovedIn,
    int MovedOut,
    int Postponed,
    int ClosedLate,
    int Unclosed,
    int Breaks,
    int BreakMinutes,
    int? AverageMinutes,
    int ShortVisits,
    decimal ExpectedIncome);

public sealed record BarberReportResponse(Guid ProfessionalId, string NameAr, string NameEn, ReportCountsResponse Counts);

/// <summary>One booking of the period: its outcome, its actual times and length, and what it is expected to bring in.</summary>
public sealed record ReportVisitResponse(
    Guid Id,
    string Reference,
    Guid ProfessionalId,
    string ProfessionalNameAr,
    string ProfessionalNameEn,
    string CustomerName,
    string ItemNameAr,
    string? ItemNameEn,
    BookingChannel Channel,
    BookingStatus Status,
    DateTimeOffset StartsAt,
    DateTimeOffset? ActualStartAt,
    DateTimeOffset? ActualEndAt,
    int? ActualMinutes,
    bool UnderFiveMinutes,
    bool ClosedLate,
    string? ClosedBy,
    bool CancelledLate,
    bool Unclosed,
    decimal ExpectedIncome,
    bool LoyaltyReward);

/// <summary>Who closed the visits that stamped loyalty circles or used rewards, by name.</summary>
public sealed record LoyaltyByActorResponse(string Name, int Stamps, int Redemptions);

public sealed record LoyaltyReportResponse(int Stamps, int Redemptions, IReadOnlyList<LoyaltyByActorResponse> ByActor);

/// <summary>
/// The owner's report for the shop-local dates <c>From</c>…<c>To</c> (at most 31). The counts and the income cover every
/// booking of the period; the <c>Visits</c> table lists the first <see cref="GetShopReportHandler.MaxVisits"/> of them
/// (<c>VisitsTruncated</c> when there were more). <c>Loyalty</c> is null when the programme is off.
/// </summary>
public sealed record ShopReportResponse(
    DateOnly From,
    DateOnly To,
    string TimeZone,
    string Currency,
    ReportCountsResponse Shop,
    IReadOnlyList<BarberReportResponse> Barbers,
    IReadOnlyList<ReportVisitResponse> Visits,
    LoyaltyReportResponse? Loyalty,
    bool VisitsTruncated = false);

internal sealed record GetShopReportQuery(DateOnly From, DateOnly To) : IQuery<Result<ShopReportResponse>>;

internal sealed class GetShopReportHandler(
    TrimmeDbContext db,
    ICurrentTenant tenant,
    IShopDirectory shops,
    IProfessionalDirectory professionals,
    IBarberBreakReport breaks,
    ILoyaltyReportSource loyalty,
    TimeProvider clock) : IQueryHandler<GetShopReportQuery, Result<ShopReportResponse>>
{
    public const int MaxDays = 31;

    /// <summary>A visit shorter than this is highlighted: «انتهت الخدمة» was perhaps pressed without a service.</summary>
    public const int ShortMinutes = 5;

    /// <summary>The bookings table lists this many at most; the counts never stop there.</summary>
    public const int MaxVisits = 500;

    public async Task<Result<ShopReportResponse>> Handle(GetShopReportQuery query, CancellationToken cancellationToken)
    {
        if (tenant.ShopId is not { } shopId || await shops.FindAsync(shopId, cancellationToken) is not { } shop)
        {
            return BookingErrors.NotFound();
        }

        if (query.To < query.From || query.To.DayNumber - query.From.DayNumber >= MaxDays)
        {
            return Error.Validation("validation.failed", "The period is out of range.",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["to"] = ["validation.out_of_range"] });
        }

        var zone = TimeZoneInfo.FindSystemTimeZoneById(shop.TimeZone);
        var start = BusinessDays.StartOf(query.From, zone);
        var end = BusinessDays.StartOf(query.To.AddDays(1), zone);
        var now = clock.GetUtcNow();
        // Every booking of the period: a month of one shop is a few thousand rows at most.
        var bookings = await db.Set<Booking>().AsNoTracking()
            .Where(b => b.StartsAt >= start && b.StartsAt < end)
            .OrderBy(b => b.StartsAt).ThenBy(b => b.Id)
            .ToListAsync(cancellationToken);

        var terms = await loyalty.TermsAsync([.. bookings.Where(b => b.LoyaltyRewardId is not null).Select(b => b.LoyaltyRewardId!.Value)], cancellationToken);
        var visits = bookings.Select(b => Visit(b, now, terms)).ToList();
        var breakTotals = await breaks.TotalsAsync(shopId, query.From, query.To, cancellationToken);
        var moves = Moves(bookings);

        var team = await professionals.ListByShopAsync(shopId, cancellationToken);
        var barbers = team
            .Select(p => new BarberReportResponse(
                p.Id.Value, p.NameAr, p.NameEn,
                Counts(bookings.Where(b => b.ProfessionalId == p.Id), visits, now, moves.In.GetValueOrDefault(p.Id), moves.Out.GetValueOrDefault(p.Id),
                    breakTotals.GetValueOrDefault(p.Id))))
            // A barber who is off now still shows when the period has something of theirs.
            .Where(r => team.First(p => p.Id.Value == r.ProfessionalId).IsActive || r.Counts.Bookings > 0 || r.Counts.Breaks > 0)
            .ToList();
        var shopCounts = Counts(bookings, visits, now, moves.In.Values.Sum(), moves.Out.Values.Sum(),
            new BreakTotals(breakTotals.Values.Sum(t => t.Count), breakTotals.Values.Sum(t => t.Minutes)));

        var activity = await loyalty.ActivityAsync(shopId, start, end, cancellationToken);
        LoyaltyReportResponse? loyaltyReport = null;
        if (activity.ProgrammeActive || activity.Stamps.Count > 0 || activity.Redemptions.Count > 0)
        {
            var ids = activity.Stamps.Select(s => s.BookingId).Concat(activity.Redemptions.Where(r => r.BookingId is not null).Select(r => r.BookingId!.Value))
                .Distinct().Select(id => new BookingId(id)).ToList();
            var closers = await db.Set<Booking>().AsNoTracking().Where(b => ids.Contains(b.Id)).ToListAsync(cancellationToken);
            var nameOf = closers.ToDictionary(b => b.Id.Value, b => b.ClosingEntry?.ActorName ?? b.ProfessionalNameAr);
            string Name(Guid? bookingId) => bookingId is { } id && nameOf.TryGetValue(id, out var name) ? name : "—";
            var byActor = activity.Stamps.Select(s => (Name: Name(s.BookingId), Stamp: 1, Redemption: 0))
                .Concat(activity.Redemptions.Select(r => (Name: Name(r.BookingId), Stamp: 0, Redemption: 1)))
                .GroupBy(x => x.Name, StringComparer.Ordinal)
                .Select(g => new LoyaltyByActorResponse(g.Key, g.Sum(x => x.Stamp), g.Sum(x => x.Redemption)))
                .OrderByDescending(x => x.Stamps + x.Redemptions).ThenBy(x => x.Name, StringComparer.Ordinal)
                .ToList();
            loyaltyReport = new LoyaltyReportResponse(activity.Stamps.Count, activity.Redemptions.Count, byActor);
        }

        return new ShopReportResponse(
            query.From, query.To, shop.TimeZone, bookings.FirstOrDefault()?.Currency ?? "SAR", shopCounts, barbers, [.. visits.Take(MaxVisits)], loyaltyReport,
            visits.Count > MaxVisits);
    }

    private static ReportVisitResponse Visit(Booking b, DateTimeOffset now, IReadOnlyDictionary<Guid, LoyaltyRewardTerms> terms)
    {
        var started = b.ActualStartAt;
        var finished = b.CompletedAt;
        int? minutes = started is { } s && finished is { } f && f >= s ? (int)Math.Round((f - s).TotalMinutes) : null;
        return new ReportVisitResponse(
            b.Id.Value, b.Reference, b.ProfessionalId.Value, b.ProfessionalNameAr, b.ProfessionalNameEn, b.CustomerName, b.ItemNameAr, b.ItemNameEn,
            b.Channel, b.Status, b.StartsAt, started, finished, minutes, minutes < ShortMinutes, b.ClosedLate, b.ClosingEntry?.ActorName, b.CancelledLate,
            b.IsOverdue(now), ExpectedIncome(b, terms), b.LoyaltyRewardId is not null || b.LoyaltyRedemptionId is not null);
    }

    /// <summary>
    /// The amount due of a completed visit: a points reward (D-149) is already taken off it; a D-132 coupon takes off its
    /// value (a free service: all of it).
    /// </summary>
    private static decimal ExpectedIncome(Booking b, IReadOnlyDictionary<Guid, LoyaltyRewardTerms> terms)
    {
        if (b.Status != BookingStatus.Completed)
        {
            return 0m;
        }

        if (b.LoyaltyRewardId is not { } rewardId || !terms.TryGetValue(rewardId, out var reward))
        {
            return b.AmountDue;
        }

        return reward.FreeService
            ? 0m
            : decimal.Round(b.AmountDue * (100 - Math.Clamp(reward.DiscountPercent ?? 0, 0, 100)) / 100m, 2);
    }

    private static ReportCountsResponse Counts(
        IEnumerable<Booking> scope, IReadOnlyList<ReportVisitResponse> visits, DateTimeOffset now, int movedIn, int movedOut, BreakTotals? breakTotals)
    {
        var bookings = scope.ToList();
        var ids = bookings.Select(b => b.Id.Value).ToHashSet();
        var theirs = visits.Where(v => ids.Contains(v.Id)).ToList();
        var timed = theirs.Where(v => v.Status == BookingStatus.Completed && v.ActualMinutes is not null && !v.ClosedLate).ToList();
        return new ReportCountsResponse(
            bookings.Count,
            bookings.Count(b => b.Status == BookingStatus.Completed),
            bookings.Count(b => b.Status == BookingStatus.NoShow),
            bookings.Count(b => BookingRules.IsCancelled(b.Status)),
            bookings.Count(b => b.CancelledLate),
            movedIn,
            movedOut,
            bookings.Sum(b => b.History.Count(h =>
                h.Kind == BookingEventKind.PostponementAccepted
                || (h.Kind == BookingEventKind.Rescheduled && h.ActorType is ActorType.ShopUser or ActorType.PlatformAdmin))),
            bookings.Count(b => b.ClosedLate),
            bookings.Count(b => b.IsOverdue(now)),
            breakTotals?.Count ?? 0,
            breakTotals?.Minutes ?? 0,
            timed.Count > 0 ? (int)Math.Round(timed.Average(v => v.ActualMinutes!.Value)) : null,
            theirs.Count(v => v.Status == BookingStatus.Completed && v.UnderFiveMinutes),
            theirs.Sum(v => v.ExpectedIncome));
    }

    /// <summary>
    /// Every «نقل الحجز» of these bookings: from the barber it left to the one it went to (the next change's previous
    /// barber, or the booking's barber now).
    /// </summary>
    private static (Dictionary<ProfessionalId, int> In, Dictionary<ProfessionalId, int> Out) Moves(IReadOnlyList<Booking> bookings)
    {
        var moveIn = new Dictionary<ProfessionalId, int>();
        var moveOut = new Dictionary<ProfessionalId, int>();
        foreach (var booking in bookings)
        {
            var changes = booking.History.Where(h => h.PreviousProfessionalId is not null).OrderBy(h => h.OccurredAt).ThenBy(h => h.Id).ToList();
            for (var i = 0; i < changes.Count; i++)
            {
                if (changes[i].Kind != BookingEventKind.ProfessionalChanged)
                {
                    continue;
                }

                var from = new ProfessionalId(changes[i].PreviousProfessionalId!.Value);
                var to = i + 1 < changes.Count ? new ProfessionalId(changes[i + 1].PreviousProfessionalId!.Value) : booking.ProfessionalId;
                moveOut[from] = moveOut.GetValueOrDefault(from) + 1;
                moveIn[to] = moveIn.GetValueOrDefault(to) + 1;
            }
        }

        return (moveIn, moveOut);
    }
}
