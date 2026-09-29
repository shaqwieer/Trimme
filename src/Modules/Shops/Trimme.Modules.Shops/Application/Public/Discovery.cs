using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Discovery;
using Trimme.BuildingBlocks.Application.Media;
using Trimme.BuildingBlocks.Application.Platform;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Shops.Domain;

namespace Trimme.Modules.Shops.Application.Public;

/// <summary>Where discovery looks: around a point (with a radius), in a city, or everywhere.</summary>
internal sealed record DiscoveryArea(double? Latitude, double? Longitude, double RadiusKm, string? City)
{
    public bool HasOrigin => Latitude is not null && Longitude is not null;
}

/// <summary>A shop discovery may list: active, located, visible (subscription and pause, D-013/D-014).</summary>
internal sealed record DiscoveryCandidate(
    ShopId Id,
    string Slug,
    string NameAr,
    string NameEn,
    ShopCategory Category,
    bool IsVerified,
    string? District,
    string? City,
    double Latitude,
    double Longitude,
    double? DistanceKm,
    string TimeZone,
    Guid? CoverMediaId,
    Guid? LogoMediaId,
    DateTimeOffset UpdatedAt,
    bool AcceptsOnlineBookings)
{
    public string SearchText { get; } = BuildingBlocks.Domain.Text.SearchText.Normalize($"{NameAr} {NameEn} {District} {Slug}");
}

/// <summary>A listed shop with its published offers, rating and (when asked for) opening status.</summary>
internal sealed record DiscoveryShop(DiscoveryCandidate Shop, ShopOfferSummary Offers, RatingSummary Rating, ShopOpenStatus? Opening);

/// <summary>
/// The discovery pipeline shared by search, popular categories, stats, areas and the sitemap (D-091):
/// <list type="number">
/// <item>active shops with a location, nearest first within the radius (PostGIS <c>ST_DWithin</c>/<c>ST_Distance</c> on the
/// GiST index), at most <see cref="MaxCandidates"/>;</item>
/// <item>only those <see cref="IShopBookability"/> lets appear in discovery;</item>
/// <item>inside one multi-shop public scope (D-090): published offers, ratings and opening status with a fixed number of
/// queries. Shops without any published service or package are not listed (there is nothing to book).</item>
/// </list>
/// </summary>
internal sealed class DiscoveryCatalog(
    TrimmeDbContext db,
    IPublicDataScope scope,
    IShopBookability bookability,
    IShopOfferReader offers,
    IRatingReader ratings,
    IShopOpeningReader opening,
    ISlotProbe probe,
    IProfessionalDirectory professionals,
    TimeProvider clock)
{
    /// <summary>At most this many candidate shops per request (nearest first). Text search runs within them.</summary>
    public const int MaxCandidates = 200;

    /// <summary>At most this many shops get the earliest-slot probe when a search sorts or filters by it (D-091).</summary>
    public const int MaxProbedShops = 24;

    /// <summary>The probe looks at today and tomorrow (the design's "earliest slot" and "tomorrow 10:00").</summary>
    public const int ProbeDays = 2;

    public DateTimeOffset Now => clock.GetUtcNow();

    /// <param name="area">Where to look.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <param name="only">When given, only these shops (the customer's favorites, D-098); the area still applies.</param>
    public async Task<IReadOnlyList<DiscoveryCandidate>> CandidatesAsync(
        DiscoveryArea area, CancellationToken cancellationToken, IReadOnlyCollection<ShopId>? only = null)
    {
        List<DiscoveryCandidate> candidates;
        using (scope.Begin(shopId: null))
        {
            var shops = db.Set<Shop>().AsNoTracking().Where(s => s.Status == ShopStatus.Active && s.Location != null);
            if (only is not null)
            {
                var ids = only.Distinct().ToArray();
                shops = shops.Where(s => ids.Contains(s.Id));
            }

            if (!string.IsNullOrWhiteSpace(area.City))
            {
                var city = area.City.Trim();
                shops = shops.Where(s => s.Location!.City == city);
            }

            if (area.HasOrigin)
            {
                var origin = Origin(area.Latitude!.Value, area.Longitude!.Value);
                var radiusMetres = area.RadiusKm * 1000;
                candidates = await shops
                    .Where(s => s.Location!.Point.IsWithinDistance(origin, radiusMetres))
                    .OrderBy(s => s.Location!.Point.Distance(origin)).ThenBy(s => s.Id)
                    .Take(MaxCandidates)
                    .Select(s => new { Shop = s, Metres = s.Location!.Point.Distance(origin) })
                    .Select(x => Candidate(x.Shop, x.Metres / 1000))
                    .ToListAsync(cancellationToken);
            }
            else
            {
                candidates = await shops.OrderBy(s => s.NameAr).ThenBy(s => s.Id)
                    .Take(MaxCandidates)
                    .Select(s => Candidate(s, null))
                    .ToListAsync(cancellationToken);
            }

            var gates = await bookability.GetManyAsync([.. candidates.Select(c => c.Id)], cancellationToken);
            candidates =
            [
                .. candidates
                    .Where(c => gates.TryGetValue(c.Id, out var gate) && gate.VisibleInDiscovery)
                    .Select(c => c with { AcceptsOnlineBookings = gates[c.Id].AcceptsOnlineBookings }),
            ];
        }

        return candidates;
    }

    /// <summary>
    /// Opens the multi-shop public scope for the candidates (dispose it when done) and loads their published offers,
    /// ratings and, when <paramref name="withOpening"/>, opening status.
    /// </summary>
    public async Task<(IDisposable Scope, IReadOnlyList<DiscoveryShop> Shops)> ListAsync(
        IReadOnlyList<DiscoveryCandidate> candidates, OfferMatch match, bool withOpening, CancellationToken cancellationToken)
    {
        var ids = candidates.Select(c => c.Id).ToList();
        var opened = scope.BeginMany(ids);
        try
        {
            var summaries = await offers.SummarizeAsync(ids, match, cancellationToken);
            var ratingRows = await ratings.GetAsync(RatingSubject.Shop, [.. ids.Select(i => i.Value)], cancellationToken);
            var openings = withOpening
                ? await opening.GetStatusesAsync(candidates.ToDictionary(c => c.Id, c => c.TimeZone), Now, cancellationToken)
                : new Dictionary<ShopId, ShopOpenStatus>();
            IReadOnlyList<DiscoveryShop> shops =
            [
                .. candidates
                    .Where(c => summaries.ContainsKey(c.Id))
                    .Select(c => new DiscoveryShop(
                        c, summaries[c.Id], ratingRows.GetValueOrDefault(c.Id.Value) ?? RatingSummary.Empty, openings.GetValueOrDefault(c.Id))),
            ];
            return (opened, shops);
        }
        catch
        {
            opened.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The earliest bookable start of each shop's probe offers (today and tomorrow, shop time), for shops that take online
    /// bookings; call inside the scope from <see cref="ListAsync"/>. Only active professionals count; one engine run per
    /// probe offer, and the earliest wins.
    /// </summary>
    public async Task<Dictionary<ShopId, DateTimeOffset?>> EarliestAsync(IReadOnlyList<DiscoveryShop> shops, CancellationToken cancellationToken)
    {
        var result = new Dictionary<ShopId, DateTimeOffset?>();
        if (shops.Count == 0)
        {
            return result;
        }

        var active = (await professionals.ListActiveProfilesAsync([.. shops.Select(s => s.Shop.Id)], cancellationToken))
            .Select(p => p.Id).ToHashSet();
        foreach (var shop in shops)
        {
            result[shop.Shop.Id] = null;
            if (!shop.Shop.AcceptsOnlineBookings)
            {
                continue;
            }

            var today = probe.Today(shop.Shop.TimeZone, Now);
            foreach (var offer in shop.Offers.Probes)
            {
                var eligible = offer.ProfessionalIds.Where(active.Contains).ToList();
                var slots = await probe.ProbeAsync(
                    shop.Shop.Id, shop.Shop.TimeZone, offer.Offer.DurationMinutes, eligible, today, today.AddDays(ProbeDays - 1), 1, cancellationToken);
                if (slots.Count > 0 && (result[shop.Shop.Id] is not { } best || slots[0].StartsAt < best))
                {
                    result[shop.Shop.Id] = slots[0].StartsAt;
                }
            }
        }

        return result;
    }

    public static Point Origin(double latitude, double longitude) => new(longitude, latitude) { SRID = ShopLocation.Srid };

    private static DiscoveryCandidate Candidate(Shop s, double? distanceKm) =>
        new(
            s.Id,
            s.Slug,
            s.NameAr,
            s.NameEn,
            s.Category,
            s.IsVerified,
            s.Location!.District,
            s.Location.City,
            s.Location.Point.Y,
            s.Location.Point.X,
            distanceKm,
            s.TimeZone,
            s.CoverMediaId == null ? null : s.CoverMediaId.Value.Value,
            s.LogoMediaId == null ? null : s.LogoMediaId.Value.Value,
            s.UpdatedAt ?? s.CreatedAt,
            AcceptsOnlineBookings: true);

    public static string? MediaUrl(Guid? mediaId) => mediaId is { } id ? MediaRules.Url(new BuildingBlocks.Domain.Media.MediaId(id)) : null;
}
