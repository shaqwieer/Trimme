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

public enum OverviewCategoryKind
{
    Category,
    Packages,
    Uncategorised,
}

/// <summary>Bookings of one platform category (services are shop-owned, so they are never grouped by a global service, DV-S02).</summary>
public sealed record OverviewCategory(OverviewCategoryKind Kind, Guid? CategoryId, string? NameAr, string? NameEn, int Bookings);

public sealed record OverviewTopShop(Guid ShopId, string Slug, string NameAr, string NameEn, int Bookings, decimal CancellationRate, decimal Rating, int ReviewCount);

/// <summary>
/// <c>Days</c>: The KPI window: today only (1), or the last 7 or 30 days including today.
/// <c>AppointmentsToday</c>: Today's bookings (the whole platform day) without cancellations.
/// <c>Period</c>: Bookings that started from the window's first day until now.
/// <c>Previous</c>: The same elapsed length immediately before, for the deltas.
/// <c>Trend</c>: The last 14 days, oldest first, every day present.
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
    IReadOnlyList<OverviewCategory> PopularCategories,
    IReadOnlyList<OverviewTopShop> TopShops);

internal sealed record GetAdminOverviewQuery(int Days) : IQuery<Result<AdminOverviewResponse>>;

internal sealed class GetAdminOverviewHandler(
    IAdminDataScope scope,
    IPlatformSettings settings,
    IBookingStatistics bookings,
    IShopStatistics shops,
    IProfessionalStatistics professionals,
    ICustomerStatistics customers,
    IServiceCategoryLookup categories,
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

    private async Task<IReadOnlyList<OverviewCategory>> PopularAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var items = await bookings.ItemsAsync(from, to, cancellationToken);
        var byService = await categories.CategoriesOfAsync([.. items.Where(i => i.ServiceId is not null).Select(i => i.ServiceId!.Value)], cancellationToken);
        var buckets = new Dictionary<(OverviewCategoryKind Kind, Guid? Id), (ServiceCategoryRef? Category, int Count)>();
        foreach (var item in items)
        {
            var category = item.ServiceId is { } serviceId ? byService.GetValueOrDefault(serviceId) : null;
            var key = item.PackageId is not null
                ? (OverviewCategoryKind.Packages, (Guid?)null)
                : category is null ? (OverviewCategoryKind.Uncategorised, null) : (OverviewCategoryKind.Category, category.Id);
            buckets[key] = (category, buckets.GetValueOrDefault(key).Count + item.Count);
        }

        return
        [
            .. buckets.OrderByDescending(b => b.Value.Count).ThenBy(b => b.Key.Kind).ThenBy(b => b.Value.Category?.NameEn, StringComparer.Ordinal)
                .Take(ListSize)
                .Select(b => new OverviewCategory(b.Key.Kind, b.Key.Id, b.Value.Category?.NameAr, b.Value.Category?.NameEn, b.Value.Count)),
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
