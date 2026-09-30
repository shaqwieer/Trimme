using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Platform;
using Trimme.BuildingBlocks.Application.Qr;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.QrAnalytics.Domain;

namespace Trimme.Modules.QrAnalytics.Application;

// The QR read side shared by the admin and shop screens (R-AD-09, D-114). It reads as far as the caller may see: the admin
// handlers open the admin data scope around it; a shop user sees only the shop's own codes (tenant filter) and, through
// their ids, only their scans and bookings.

/// <summary>A code with its target's names, the printed URL and its figures over the chosen period.</summary>
public sealed record QrCodeResponse(
    Guid Id,
    string Code,
    string Url,
    Guid ShopId,
    string ShopSlug,
    string ShopNameAr,
    string ShopNameEn,
    QrTargetType TargetType,
    Guid? ProfessionalId,
    string? ProfessionalNameAr,
    string? ProfessionalNameEn,
    string? Label,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeactivatedAt,
    int Visits,
    int Bookings,
    uint Version);

/// <summary>Scans, the bookings they brought, and the share of scans that led to a booking (percent, one decimal, ≤ 100).</summary>
public sealed record QrFigures(int Visits, int Bookings, int ConvertedVisits, double ConversionRate);

public sealed record QrShopFigures(Guid ShopId, string ShopNameAr, string ShopNameEn, int Visits, int Bookings, double ConversionRate);

/// <summary>
/// The analytics of a period of local days [<c>From</c>, <c>To</c>] (platform time zone). Bookings count when they were
/// made in the period; a scan counts as converted when any booking was credited to it.
/// </summary>
public sealed record QrAnalyticsResponse(DateOnly From, DateOnly To, int AttributionDays, QrFigures Totals, IReadOnlyList<QrShopFigures> ByShop);

/// <summary>A period of local days turned into instants.</summary>
internal sealed record QrPeriod(DateOnly From, DateOnly To, DateTimeOffset Start, DateTimeOffset End)
{
    public const int MaxDays = 366;
    public const int DefaultDays = 30;
}

internal sealed class QrReadModel(
    TrimmeDbContext db,
    IShopDirectory shops,
    IProfessionalDirectory professionals,
    IQrBookingReader bookings,
    IPlatformSettings settings,
    IOptions<QrOptions> options,
    IOptions<QrLinkOptions> links,
    TimeProvider clock)
{
    public int AttributionDays => options.Value.AttributionDays;

    public string UrlFor(string code) => links.Value.UrlFor(code);

    /// <summary>
    /// [from, to] in the platform's days; without <paramref name="from"/>, the last <paramref name="days"/> days (default 30)
    /// up to <paramref name="to"/> (default today). Null when invalid.
    /// </summary>
    public async Task<QrPeriod?> PeriodAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken, int? days = null)
    {
        if (days is < 1 or > QrPeriod.MaxDays)
        {
            return null;
        }

        var platform = await settings.GetAsync(cancellationToken);
        var today = platform.LocalDate(clock.GetUtcNow());
        var last = to ?? today;
        var first = from ?? last.AddDays(-((days ?? QrPeriod.DefaultDays) - 1));
        if (first > last || last.DayNumber - first.DayNumber + 1 > QrPeriod.MaxDays)
        {
            return null;
        }

        var zone = TimeZoneInfo.FindSystemTimeZoneById(platform.TimeZone);
        DateTimeOffset Start(DateOnly day)
        {
            var local = day.ToDateTime(TimeOnly.MinValue);
            return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
        }

        return new QrPeriod(first, last, Start(first), Start(last.AddDays(1)));
    }

    /// <summary>Visits and credited bookings per code in the period (for the lists).</summary>
    public async Task<IReadOnlyDictionary<QrCodeLinkId, (int Visits, int Bookings)>> PerCodeAsync(
        IReadOnlyCollection<QrCodeLinkId> linkIds, QrPeriod period, CancellationToken cancellationToken)
    {
        if (linkIds.Count == 0)
        {
            return new Dictionary<QrCodeLinkId, (int, int)>();
        }

        var ids = linkIds.ToArray();
        var visits = await db.Set<QrVisit>().AsNoTracking()
            .Where(v => ids.Contains(v.LinkId) && v.VisitedAt >= period.Start && v.VisitedAt < period.End)
            .GroupBy(v => v.LinkId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        var credited = (await bookings.ListAsync(period.Start, period.End, [.. ids.Select(i => i.Value)], cancellationToken))
            .GroupBy(b => b.LinkId)
            .ToDictionary(g => new QrCodeLinkId(g.Key), g => g.Count());
        return ids.ToDictionary(id => id, id => (visits.GetValueOrDefault(id), credited.GetValueOrDefault(id)));
    }

    /// <summary>
    /// The period's figures, per shop. <paramref name="linkIds"/> limits them to these codes (a shop's own); null means
    /// every code the caller may see (admins).
    /// </summary>
    public async Task<(QrFigures Totals, IReadOnlyList<QrShopFigures> ByShop)> FiguresAsync(
        QrPeriod period, ShopId? shopId, IReadOnlyCollection<QrCodeLinkId>? linkIds, CancellationToken cancellationToken)
    {
        var visits = db.Set<QrVisit>().AsNoTracking().Where(v => v.VisitedAt >= period.Start && v.VisitedAt < period.End);
        if (shopId is { } shop)
        {
            visits = visits.Where(v => v.ShopId == shop);
        }

        QrCodeLinkId[]? ids = linkIds is null ? null : [.. linkIds];
        if (ids is not null)
        {
            visits = visits.Where(v => ids.Contains(v.LinkId));
        }

        var visitsByShop = await visits.GroupBy(v => v.ShopId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

        // Bookings made up to the attribution window after the period can still convert a scan of the period.
        var credited = await bookings.ListAsync(
            period.Start, period.End.AddDays(AttributionDays), ids?.Select(i => i.Value).ToArray(), cancellationToken);
        if (shopId is { } only)
        {
            credited = [.. credited.Where(b => b.ShopId == only)];
        }

        var bookingsByShop = credited.Where(b => b.CreatedAt < period.End).GroupBy(b => b.ShopId).ToDictionary(g => g.Key, g => g.Count());
        var creditedVisits = credited.Where(b => b.VisitId is not null).Select(b => new QrVisitId(b.VisitId!.Value)).Distinct().ToArray();
        var convertedByShop = creditedVisits.Length == 0
            ? new Dictionary<ShopId, int>()
            : await visits.Where(v => creditedVisits.Contains(v.Id)).GroupBy(v => v.ShopId).Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

        var shopIds = visitsByShop.Keys.Union(bookingsByShop.Keys).ToArray();
        var names = await shops.FindManyAsync(shopIds, cancellationToken);
        var byShop = shopIds
            .Select(id => new QrShopFigures(
                id.Value,
                names.GetValueOrDefault(id)?.NameAr ?? string.Empty,
                names.GetValueOrDefault(id)?.NameEn ?? string.Empty,
                visitsByShop.GetValueOrDefault(id),
                bookingsByShop.GetValueOrDefault(id),
                Rate(convertedByShop.GetValueOrDefault(id), visitsByShop.GetValueOrDefault(id))))
            .OrderByDescending(s => s.Visits).ThenByDescending(s => s.Bookings).ThenBy(s => s.ShopNameAr, StringComparer.Ordinal)
            .ToList();

        var totalVisits = visitsByShop.Values.Sum();
        var converted = convertedByShop.Values.Sum();
        return (new QrFigures(totalVisits, bookingsByShop.Values.Sum(), converted, Rate(converted, totalVisits)), byShop);
    }

    /// <summary>The codes as responses with their names and the period's figures, in the given order.</summary>
    public async Task<IReadOnlyList<QrCodeResponse>> MapAsync(IReadOnlyList<QrCodeLink> page, QrPeriod period, CancellationToken cancellationToken)
    {
        var shopNames = await shops.FindManyAsync([.. page.Select(l => l.ShopId).Distinct()], cancellationToken);
        var professionalNames = await professionals.FindManyAsync(
            [.. page.Where(l => l.ProfessionalId is not null).Select(l => l.ProfessionalId!.Value).Distinct()], cancellationToken);
        var figures = await PerCodeAsync([.. page.Select(l => l.Id)], period, cancellationToken);
        return
        [
            .. page.Select(l =>
            {
                var shop = shopNames.GetValueOrDefault(l.ShopId);
                var professional = l.ProfessionalId is { } p ? professionalNames.GetValueOrDefault(p) : null;
                var (visits, credited) = figures.GetValueOrDefault(l.Id);
                return new QrCodeResponse(
                    l.Id.Value, l.Code, UrlFor(l.Code), l.ShopId.Value, shop?.Slug ?? string.Empty, shop?.NameAr ?? string.Empty, shop?.NameEn ?? string.Empty,
                    l.TargetType, l.ProfessionalId?.Value, professional?.NameAr, professional?.NameEn, l.Label, l.IsActive, l.CreatedAt, l.DeactivatedAt,
                    visits, credited, l.Version);
            }),
        ];
    }

    public static double Rate(int converted, int visits) => visits == 0 ? 0 : Math.Round(Math.Min(100.0, 100.0 * converted / visits), 1);
}
