using Trimme.BuildingBlocks.Application.Discovery;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Platform;
using Trimme.BuildingBlocks.Application.Reporting;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Results;

namespace Trimme.Modules.Administration.Application.Admin;

// The platform overview (a-overview, R-AD-01, D-101). Every figure is an SQL aggregate from the module that owns the
// data, read inside one admin scope. Days follow the platform calendar (IPlatformSettings.TimeZone).

/// <summary>
/// <c>Total</c>: Bookings that started in the window, cancellations included (the rates' denominator).
/// <c>CompletionRate</c>: Completed ÷ total, in percent with one decimal; 0 when there were none.
/// </summary>
public sealed record OverviewPeriod(
    DateTimeOffset From, DateTimeOffset To, int Total, int Completed, int Cancelled, int NoShows, decimal CompletionRate, decimal CancellationRate, decimal NoShowRate);

public sealed record OverviewTrendDay(DateOnly Date, int Completed, int CancelledOrNoShow, int Other);

public enum OverviewOfferKind
{
    Service,
    Package,
}

/// <summary>
/// Bookings of one shop's service or package (D-154). Services and packages are each shop's own (DV-S02), so a row names
/// its shop: «قص الشعر · الأصالة». It replaces the platform categories, which each shop's own sections (D-149) superseded.
/// </summary>
public sealed record OverviewService(
    OverviewOfferKind Kind, Guid Id, string NameAr, string? NameEn, Guid ShopId, string ShopNameAr, string ShopNameEn, int Bookings);

public sealed record OverviewTopShop(Guid ShopId, string Slug, string NameAr, string NameEn, int Bookings, decimal CancellationRate, decimal Rating, int ReviewCount);

/// <summary>
/// <c>Days</c>: The KPI window: today only (1), or the last 7 or 30 days including today.
/// <c>AppointmentsToday</c>: Today's bookings (the whole platform day) without cancellations.
/// <c>Period</c>: Bookings that started from the window's first day until now.
/// <c>Previous</c>: The same elapsed length immediately before, for the deltas.
/// <c>Trend</c>: The last 14 days, oldest first, every day present.
/// <c>PopularServices</c>: The most booked services and packages in the window, cancellations left out.
/// </summary>
public sealed record AdminOverviewResponse(
    int Days,
    DateOnly Today,
    string TimeZone,
    int AppointmentsToday,
    int AppointmentsYesterday,
    OverviewPeriod Period,
    OverviewPeriod Previous,
    ShopCounts Shops,
    ProfessionalCounts Professionals,
    int NewCustomers,
    int NewCustomersPrevious,
    IReadOnlyList<OverviewTrendDay> Trend,
    IReadOnlyList<OverviewService> PopularServices,
    IReadOnlyList<OverviewTopShop> TopShops);

internal sealed record GetAdminOverviewQuery(int Days) : IQuery<Result<AdminOverviewResponse>>;

internal sealed class GetAdminOverviewHandler(
    IAdminDataScope scope,
    IPlatformSettings settings,
    IBookingStatistics bookings,
    IShopStatistics shops,
    IProfessionalStatistics professionals,
    ICustomerStatistics customers,
    IBookedOfferLookup offers,
    IShopDirectory directory,
    IRatingReader ratings,
    TimeProvider clock)
    : IQueryHandler<GetAdminOverviewQuery, Result<AdminOverviewResponse>>
{
    public const int TrendDays = 14;
    public const int ListSize = 5;
    public static readonly int[] AllowedDays = [1, 7, 30];

    public async Task<Result<AdminOverviewResponse>> Handle(GetAdminOverviewQuery query, CancellationToken cancellationToken)
    {
        if (!AllowedDays.Contains(query.Days))
        {
            return Error.Validation("validation.failed", "Days must be 1, 7 or 30.", new Dictionary<string, string[]> { ["days"] = ["validation.out_of_range"] });
        }

        using var _ = scope.Begin();
        var platform = await settings.GetAsync(cancellationToken);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(platform.TimeZone);
        var now = clock.GetUtcNow();
        var today = platform.LocalDate(now);
        DateTimeOffset Start(DateOnly day) => PlatformCalendar.StartOf(day, zone);

        var todayTotals = await bookings.TotalsAsync(Start(today), Start(today.AddDays(1)), cancellationToken);
        var yesterdayTotals = await bookings.TotalsAsync(Start(today.AddDays(-1)), Start(today), cancellationToken);

        var periodStart = Start(today.AddDays(1 - query.Days));
        var elapsed = now - periodStart;
        var previousStart = Start(today.AddDays(1 - (2 * query.Days)));
        var period = Period(periodStart, now, await bookings.TotalsAsync(periodStart, now, cancellationToken));
        var previous = Period(previousStart, previousStart + elapsed, await bookings.TotalsAsync(previousStart, previousStart + elapsed, cancellationToken));

        var trendStart = today.AddDays(1 - TrendDays);
        var daily = (await bookings.DailyAsync(Start(trendStart), Start(today.AddDays(1)), platform.TimeZone, cancellationToken)).ToDictionary(d => d.Date);
        var trend = Enumerable.Range(0, TrendDays)
            .Select(i => trendStart.AddDays(i))
            .Select(day => daily.TryGetValue(day, out var d) ? new OverviewTrendDay(day, d.Completed, d.CancelledOrNoShow, d.Other) : new OverviewTrendDay(day, 0, 0, 0))
            .ToList();

        return new AdminOverviewResponse(
            query.Days,
            today,
            platform.TimeZone,
            todayTotals.Kept,
            yesterdayTotals.Kept,
            period,
            previous,
            await shops.CountsAsync(cancellationToken),
            await professionals.CountsAsync(periodStart, cancellationToken),
            await customers.RegisteredAsync(periodStart, now, cancellationToken),
            await customers.RegisteredAsync(previousStart, previousStart + elapsed, cancellationToken),
            trend,
            await PopularAsync(periodStart, now, cancellationToken),
            await TopShopsAsync(periodStart, now, cancellationToken));
    }

    private static OverviewPeriod Period(DateTimeOffset from, DateTimeOffset to, BookingTotals totals) =>
        new(from, to, totals.Total, totals.Completed, totals.Cancelled, totals.NoShow,
            Percent(totals.Completed, totals.Total), Percent(totals.Cancelled, totals.Total), Percent(totals.NoShow, totals.Total));

    private static decimal Percent(int part, int total) =>
        total == 0 ? 0 : Math.Round(100m * part / total, 1, MidpointRounding.AwayFromZero);

    private async Task<IReadOnlyList<OverviewService>> PopularAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        // One count per booked service or package; a row that no longer exists (deleted) is left out.
        var counts = new Dictionary<(OverviewOfferKind Kind, Guid Id), int>();
        foreach (var item in await bookings.ItemsAsync(from, to, cancellationToken))
        {
            var key = item.PackageId is { } packageId ? (OverviewOfferKind.Package, packageId) : (OverviewOfferKind.Service, item.ServiceId!.Value);
            counts[key] = counts.GetValueOrDefault(key) + item.Count;
        }

        var named = await offers.OffersAsync(
            [.. counts.Keys.Where(k => k.Kind == OverviewOfferKind.Service).Select(k => k.Id)],
            [.. counts.Keys.Where(k => k.Kind == OverviewOfferKind.Package).Select(k => k.Id)],
            cancellationToken);
        var top = counts.Where(c => named.ContainsKey(c.Key.Id))
            .OrderByDescending(c => c.Value).ThenBy(c => c.Key.Kind).ThenBy(c => named[c.Key.Id].NameAr, StringComparer.Ordinal).ThenBy(c => c.Key.Id)
            .Take(ListSize)
            .ToList();
        var shopNames = await directory.FindManyAsync([.. top.Select(c => named[c.Key.Id].ShopId).Distinct()], cancellationToken);
        return
        [
            .. top.Select(c =>
            {
                var offer = named[c.Key.Id];
                var shop = shopNames.GetValueOrDefault(offer.ShopId);
                return new OverviewService(
                    c.Key.Kind, offer.Id, offer.NameAr, offer.NameEn, offer.ShopId.Value, shop?.NameAr ?? string.Empty, shop?.NameEn ?? string.Empty, c.Value);
            }),
        ];
    }

    private async Task<IReadOnlyList<OverviewTopShop>> TopShopsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var top = await bookings.TopShopsAsync(from, to, ListSize, cancellationToken);
        var names = await directory.FindManyAsync([.. top.Select(t => t.ShopId)], cancellationToken);
        var rated = await ratings.GetAsync(RatingSubject.Shop, [.. top.Select(t => t.ShopId.Value)], cancellationToken);
        return
        [
            .. top.Select(t =>
            {
                var shop = names.GetValueOrDefault(t.ShopId);
                var rating = rated.GetValueOrDefault(t.ShopId.Value) ?? RatingSummary.Empty;
                return new OverviewTopShop(
                    t.ShopId.Value, shop?.Slug ?? string.Empty, shop?.NameAr ?? string.Empty, shop?.NameEn ?? string.Empty, t.Total,
                    Percent(t.Cancelled, t.Total), rating.Average, rating.Count);
            }),
        ];
    }
}

/// <summary>Local days of the platform calendar as instants.</summary>
internal static class PlatformCalendar
{
    /// <summary>The first instant of <paramref name="day"/> in <paramref name="zone"/> (a skipped midnight resolves to the first instant after the gap).</summary>
    public static DateTimeOffset StartOf(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }
}
