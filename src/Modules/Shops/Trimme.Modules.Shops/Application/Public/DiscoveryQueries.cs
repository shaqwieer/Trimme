using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Discovery;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Platform;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Domain.Text;
using Trimme.Modules.Shops.Domain;

namespace Trimme.Modules.Shops.Application.Public;

public enum DiscoverySort
{
    /// <summary>Nearest first (the default with a location).</summary>
    Distance,

    /// <summary>Highest rated first, then most reviewed (the default without a location).</summary>
    Rating,

    /// <summary>Earliest bookable slot first (today and tomorrow, bounded probe, D-091).</summary>
    Earliest,
}

/// <summary>The offer a search matched, or the one a price pin shows (the shop's own name, price and duration).</summary>
public sealed record DiscoveryOfferResponse(Guid Id, bool IsPackage, string NameAr, string? NameEn, decimal Price, string Currency, int DurationMinutes);

/// <summary>
/// A shop in discovery results. <c>PinPrice</c> is the matched offer's price, else the shop's lowest (the map pin and
/// the "from" price, mapRules #1). <c>DistanceKm</c> is set only when the search has a location. <c>EarliestSlotAt</c> is
/// the first bookable start today or tomorrow (null when there is none, or when the shop was not probed). No phone numbers.
/// </summary>
public sealed record ShopSearchItemResponse(
    Guid Id,
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
    decimal Rating,
    int ReviewCount,
    decimal MinPrice,
    decimal PinPrice,
    string Currency,
    DiscoveryOfferResponse? MatchedOffer,
    bool IsOpenNow,
    DateTimeOffset? ClosesAt,
    DateTimeOffset? NextOpensAt,
    bool AcceptsOnlineBookings,
    DateTimeOffset? EarliestSlotAt,
    string TimeZone,
    string? CoverUrl,
    string? LogoUrl);

/// <summary>The lowest and highest pin price of the results before the price filter (the price slider's range).</summary>
public sealed record PriceRangeResponse(decimal Min, decimal Max);

public sealed record ShopSearchResponse(
    IReadOnlyList<ShopSearchItemResponse> Items, int Page, int PageSize, int Total, double? RadiusKm, DiscoverySort Sort, PriceRangeResponse? PriceRange);

/// <summary>A platform category offered nearby, with the lowest price among those shops (DV-S11, D-025: never a global price).</summary>
public sealed record PopularCategoryResponse(Guid CategoryId, decimal MinPrice, string Currency, int ShopCount);

/// <summary>Real platform totals for the landing page; the web hides any that is zero (no fabricated figures).</summary>
public sealed record DiscoveryStatsResponse(int ShopCount, int ProfessionalCount, decimal AverageRating, int ReviewCount);

/// <summary>A district with listed shops, and where it is (the average of its shops), for manual location (DV-A21).</summary>
public sealed record DiscoveryAreaResponse(string City, string? District, double Latitude, double Longitude, int ShopCount);

/// <summary>The districts customers can pick, and the platform's default map centre.</summary>
public sealed record DiscoveryAreasResponse(IReadOnlyList<DiscoveryAreaResponse> Areas, double DefaultLatitude, double DefaultLongitude, int DefaultZoom);

public sealed record SitemapShopResponse(string Slug, DateTimeOffset UpdatedAt, IReadOnlyList<string> ProfessionalSlugs);

/// <summary>Every shop listed in discovery, with its active professionals' slugs (the sitemap, R-WEB-10).</summary>
public sealed record SitemapResponse(IReadOnlyList<SitemapShopResponse> Shops);

internal sealed record SearchShopsQuery(
    DiscoveryArea Area,
    string? Query,
    Guid? CategoryId,
    bool OpenNow,
    bool VerifiedOnly,
    bool BookableToday,
    decimal? MinPrice,
    decimal? MaxPrice,
    DiscoverySort? Sort,
    int Page,
    int PageSize) : IQuery<Result<ShopSearchResponse>>;

internal sealed record PopularCategoriesQuery(DiscoveryArea Area) : IQuery<Result<IReadOnlyList<PopularCategoryResponse>>>;

internal sealed record DiscoveryStatsQuery : IQuery<DiscoveryStatsResponse>;

internal sealed record DiscoveryAreasQuery : IQuery<DiscoveryAreasResponse>;

internal sealed record SitemapQuery : IQuery<SitemapResponse>;

internal sealed record TopProfessionalsQuery(DiscoveryArea Area, int Limit) : IQuery<Result<IReadOnlyList<TopProfessionalResponse>>>;

/// <summary>A highly rated professional of a listed shop (only stored ratings; no contact data).</summary>
public sealed record TopProfessionalResponse(
    Guid Id,
    string Slug,
    string NameAr,
    string NameEn,
    string? SpecialtyAr,
    string? SpecialtyEn,
    string? AvatarUrl,
    string ShopSlug,
    string ShopNameAr,
    string ShopNameEn,
    decimal Rating,
    int ReviewCount);

internal static class DiscoveryRules
{
    public const double DefaultRadiusKm = 10;
    public const double MaxRadiusKm = 50;
    public const int MaxPageSize = 50;
    public const int MaxQueryLength = 100;

    public static Result<DiscoveryArea> Area(double? latitude, double? longitude, double? radiusKm, string? city)
    {
        if ((latitude is null) != (longitude is null))
        {
            return Invalid(latitude is null ? "lat" : "lng", "validation.required");
        }

        if (latitude is < -90 or > 90 || double.IsNaN(latitude ?? 0))
        {
            return Invalid("lat", "validation.out_of_range");
        }

        if (longitude is < -180 or > 180 || double.IsNaN(longitude ?? 0))
        {
            return Invalid("lng", "validation.out_of_range");
        }

        if (radiusKm is { } radius && (radius is <= 0 or > MaxRadiusKm || double.IsNaN(radius)))
        {
            return Invalid("radiusKm", "validation.out_of_range");
        }

        return new DiscoveryArea(latitude, longitude, radiusKm ?? DefaultRadiusKm, string.IsNullOrWhiteSpace(city) ? null : city.Trim());
    }

    public static Error Invalid(string field, string code) =>
        Error.Validation("validation.failed", "The search is invalid.", new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [code] });

    public static DiscoveryOfferResponse? ToResponse(OfferSummary? offer) =>
        offer is null ? null : new DiscoveryOfferResponse(offer.Id, offer.IsPackage, offer.NameAr, offer.NameEn, offer.Price, offer.Currency, offer.DurationMinutes);
}

/// <summary>
/// Discovery search (R-CUS-03/04, D-091): nearby or city shops, text and category match, open now, verified, bookable
/// today, price range, sorted by distance, rating or earliest slot, paged. Anonymous and customer callers see the same.
/// </summary>
internal sealed class SearchShopsHandler(DiscoveryCatalog catalog) : IQueryHandler<SearchShopsQuery, Result<ShopSearchResponse>>
{
    public async Task<Result<ShopSearchResponse>> Handle(SearchShopsQuery query, CancellationToken cancellationToken)
    {
        if (query.Query is { Length: > DiscoveryRules.MaxQueryLength })
        {
            return DiscoveryRules.Invalid("q", "validation.too_long");
        }

        if (query.MinPrice is < 0 || query.MaxPrice is < 0 || (query.MinPrice is { } min && query.MaxPrice is { } max && min > max))
        {
            return DiscoveryRules.Invalid("minPrice", "validation.out_of_range");
        }

        var sort = query.Sort ?? (query.Area.HasOrigin ? DiscoverySort.Distance : DiscoverySort.Rating);
        if (sort == DiscoverySort.Distance && !query.Area.HasOrigin)
        {
            sort = DiscoverySort.Rating;
        }

        var text = SearchText.Normalize(query.Query);
        var candidates = await catalog.CandidatesAsync(query.Area, cancellationToken);
        var (scope, listed) = await catalog.ListAsync(candidates, new OfferMatch(query.CategoryId, text), withOpening: true, cancellationToken);
        using (scope)
        {
            IEnumerable<DiscoveryShop> results = listed;
            if (text.Length > 0)
            {
                results = results.Where(s => s.Offers.Matched is not null || SearchText.Matches(s.Shop.SearchText, text));
            }

            if (query.CategoryId is { } category)
            {
                results = results.Where(s => s.Offers.MinPriceByCategory.ContainsKey(category));
            }

            if (query.VerifiedOnly)
            {
                results = results.Where(s => s.Shop.IsVerified);
            }

            if (query.OpenNow)
            {
                results = results.Where(s => s.Opening is { IsOpenNow: true });
            }

            var filtered = results.ToList();
            PriceRangeResponse? priceRange = filtered.Count == 0
                ? null
                : new PriceRangeResponse(filtered.Min(PinPrice), filtered.Max(PinPrice));
            filtered = [.. filtered.Where(s => (query.MinPrice is not { } lo || PinPrice(s) >= lo) && (query.MaxPrice is not { } hi || PinPrice(s) <= hi))];

            // A stable base order first (distance, else rating), then the earliest-slot probe on the first shops only.
            filtered = [.. filtered.OrderBy(s => s.Shop.DistanceKm ?? double.MaxValue)
                .ThenByDescending(s => s.Rating.Average).ThenByDescending(s => s.Rating.Count).ThenBy(s => s.Shop.Id.Value)];
            if (sort == DiscoverySort.Rating)
            {
                filtered = [.. filtered.OrderByDescending(s => s.Rating.Average).ThenByDescending(s => s.Rating.Count)
                    .ThenBy(s => s.Shop.DistanceKm ?? double.MaxValue).ThenBy(s => s.Shop.Id.Value)];
            }

            Dictionary<ShopId, DateTimeOffset?> earliest = [];
            if (sort == DiscoverySort.Earliest || query.BookableToday)
            {
                earliest = await catalog.EarliestAsync([.. filtered.Take(DiscoveryCatalog.MaxProbedShops)], cancellationToken);
            }

            // Bookable today needs a probed slot today: shops beyond the probed ones are left out (D-091).
            if (query.BookableToday)
            {
                filtered = [.. filtered.Where(s => earliest.GetValueOrDefault(s.Shop.Id) is { } at && IsToday(s.Shop.TimeZone, at, catalog.Now))];
            }

            // Earliest first; shops without a slot in the probe window, or beyond the probed ones, follow in the base order.
            if (sort == DiscoverySort.Earliest)
            {
                filtered = [.. filtered.OrderBy(s => earliest.GetValueOrDefault(s.Shop.Id) ?? DateTimeOffset.MaxValue)];
            }

            var pageSize = Math.Clamp(query.PageSize, 1, DiscoveryRules.MaxPageSize);
            var page = Math.Max(1, query.Page);
            var pageItems = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            var missing = pageItems.Where(s => !earliest.ContainsKey(s.Shop.Id)).ToList();
            foreach (var (id, at) in await catalog.EarliestAsync(missing, cancellationToken))
            {
                earliest[id] = at;
            }

            return new ShopSearchResponse(
                [.. pageItems.Select(s => ToItem(s, earliest.GetValueOrDefault(s.Shop.Id)))],
                page,
                pageSize,
                filtered.Count,
                query.Area.HasOrigin ? query.Area.RadiusKm : null,
                sort,
                priceRange);
        }
    }

    private static decimal PinPrice(DiscoveryShop shop) => shop.Offers.Matched?.Price ?? shop.Offers.MinPrice;

    private static bool IsToday(string timeZone, DateTimeOffset at, DateTimeOffset now)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
        return TimeZoneInfo.ConvertTime(at, zone).Date == TimeZoneInfo.ConvertTime(now, zone).Date;
    }

    private static ShopSearchItemResponse ToItem(DiscoveryShop s, DateTimeOffset? earliest) =>
        new(
            s.Shop.Id.Value,
            s.Shop.Slug,
            s.Shop.NameAr,
            s.Shop.NameEn,
            s.Shop.Category,
            s.Shop.IsVerified,
            s.Shop.District,
            s.Shop.City,
            s.Shop.Latitude,
            s.Shop.Longitude,
            s.Shop.DistanceKm is { } km ? Math.Round(km, 2) : null,
            s.Rating.Average,
            s.Rating.Count,
            s.Offers.MinPrice,
            PinPrice(s),
            s.Offers.Currency,
            DiscoveryRules.ToResponse(s.Offers.Matched),
            s.Opening?.IsOpenNow ?? false,
            s.Opening?.ClosesAt,
            s.Opening?.NextOpensAt,
            s.Shop.AcceptsOnlineBookings,
            earliest,
            s.Shop.TimeZone,
            DiscoveryCatalog.MediaUrl(s.Shop.CoverMediaId),
            DiscoveryCatalog.MediaUrl(s.Shop.LogoMediaId));
}

/// <summary>Categories offered by the shops around a point (or everywhere), with their lowest price (DV-S11).</summary>
internal sealed class PopularCategoriesHandler(DiscoveryCatalog catalog) : IQueryHandler<PopularCategoriesQuery, Result<IReadOnlyList<PopularCategoryResponse>>>
{
    public async Task<Result<IReadOnlyList<PopularCategoryResponse>>> Handle(PopularCategoriesQuery query, CancellationToken cancellationToken)
    {
        var candidates = await catalog.CandidatesAsync(query.Area, cancellationToken);
        var (scope, listed) = await catalog.ListAsync(candidates, OfferMatch.None, withOpening: false, cancellationToken);
        using (scope)
        {
            IReadOnlyList<PopularCategoryResponse> categories =
            [
                .. listed.SelectMany(s => s.Offers.MinPriceByCategory.Select(c => (Category: c.Key, Price: c.Value, s.Offers.Currency)))
                    .GroupBy(x => x.Category)
                    .Select(g => new PopularCategoryResponse(g.Key, g.Min(x => x.Price), g.First().Currency, g.Count()))
                    .OrderByDescending(c => c.ShopCount).ThenBy(c => c.MinPrice),
            ];
            return Result.Success(categories);
        }
    }
}

/// <summary>Landing-page totals over the shops listed in discovery.</summary>
internal sealed class DiscoveryStatsHandler(DiscoveryCatalog catalog, IProfessionalDirectory professionals) : IQueryHandler<DiscoveryStatsQuery, DiscoveryStatsResponse>
{
    public async Task<DiscoveryStatsResponse> Handle(DiscoveryStatsQuery query, CancellationToken cancellationToken)
    {
        var candidates = await catalog.CandidatesAsync(new DiscoveryArea(null, null, DiscoveryRules.DefaultRadiusKm, null), cancellationToken);
        var (scope, listed) = await catalog.ListAsync(candidates, OfferMatch.None, withOpening: false, cancellationToken);
        using (scope)
        {
            var professionalCount = (await professionals.ListActiveProfilesAsync([.. listed.Select(s => s.Shop.Id)], cancellationToken)).Count;
            var reviewCount = listed.Sum(s => s.Rating.Count);
            var average = reviewCount == 0
                ? 0
                : Math.Round(listed.Sum(s => s.Rating.Average * s.Rating.Count) / reviewCount, 1, MidpointRounding.AwayFromZero);
            return new DiscoveryStatsResponse(listed.Count, professionalCount, average, reviewCount);
        }
    }
}

/// <summary>Districts and cities with listed shops, for the manual location picker and the city listing.</summary>
internal sealed class DiscoveryAreasHandler(DiscoveryCatalog catalog, IPlatformSettings settings) : IQueryHandler<DiscoveryAreasQuery, DiscoveryAreasResponse>
{
    public async Task<DiscoveryAreasResponse> Handle(DiscoveryAreasQuery query, CancellationToken cancellationToken)
    {
        var candidates = await catalog.CandidatesAsync(new DiscoveryArea(null, null, DiscoveryRules.DefaultRadiusKm, null), cancellationToken);
        var (scope, listed) = await catalog.ListAsync(candidates, OfferMatch.None, withOpening: false, cancellationToken);
        using (scope)
        {
            var platform = await settings.GetAsync(cancellationToken);
            var areas = listed
                .Where(s => !string.IsNullOrWhiteSpace(s.Shop.City))
                .GroupBy(s => (City: s.Shop.City!, s.Shop.District))
                .Select(g => new DiscoveryAreaResponse(
                    g.Key.City, g.Key.District, Math.Round(g.Average(s => s.Shop.Latitude), 5), Math.Round(g.Average(s => s.Shop.Longitude), 5), g.Count()))
                .OrderBy(a => a.City, StringComparer.Ordinal).ThenBy(a => a.District, StringComparer.Ordinal)
                .ToList();
            return new DiscoveryAreasResponse(areas, platform.MapDefaultLatitude, platform.MapDefaultLongitude, platform.MapDefaultZoom);
        }
    }
}

/// <summary>The listed shops and their active professionals, for <c>sitemap.xml</c>.</summary>
internal sealed class SitemapHandler(DiscoveryCatalog catalog, IProfessionalDirectory professionals) : IQueryHandler<SitemapQuery, SitemapResponse>
{
    public async Task<SitemapResponse> Handle(SitemapQuery query, CancellationToken cancellationToken)
    {
        var candidates = await catalog.CandidatesAsync(new DiscoveryArea(null, null, DiscoveryRules.DefaultRadiusKm, null), cancellationToken);
        var (scope, listed) = await catalog.ListAsync(candidates, OfferMatch.None, withOpening: false, cancellationToken);
        using (scope)
        {
            var byShop = (await professionals.ListActiveProfilesAsync([.. listed.Select(s => s.Shop.Id)], cancellationToken))
                .GroupBy(p => p.ShopId).ToDictionary(g => g.Key, g => g.Select(p => p.Slug).ToList());
            return new SitemapResponse(
            [
                .. listed.OrderBy(s => s.Shop.Slug, StringComparer.Ordinal).Select(s => new SitemapShopResponse(
                    s.Shop.Slug, s.Shop.UpdatedAt, byShop.GetValueOrDefault(s.Shop.Id) ?? [])),
            ]);
        }
    }
}

/// <summary>The best-rated professionals of the shops around a point (or everywhere), for the discover page.</summary>
internal sealed class TopProfessionalsHandler(DiscoveryCatalog catalog, IProfessionalDirectory professionals, IRatingReader ratings)
    : IQueryHandler<TopProfessionalsQuery, Result<IReadOnlyList<TopProfessionalResponse>>>
{
    public const int MaxLimit = 12;

    public async Task<Result<IReadOnlyList<TopProfessionalResponse>>> Handle(TopProfessionalsQuery query, CancellationToken cancellationToken)
    {
        var candidates = await catalog.CandidatesAsync(query.Area, cancellationToken);
        var (scope, listed) = await catalog.ListAsync(candidates, OfferMatch.None, withOpening: false, cancellationToken);
        using (scope)
        {
            var shops = listed.ToDictionary(s => s.Shop.Id, s => s.Shop);
            var cards = await professionals.ListActiveProfilesAsync([.. shops.Keys], cancellationToken);
            var rated = await ratings.GetAsync(RatingSubject.Professional, [.. cards.Select(c => c.Id.Value)], cancellationToken);
            IReadOnlyList<TopProfessionalResponse> top =
            [
                .. cards.Where(c => rated.ContainsKey(c.Id.Value))
                    .OrderByDescending(c => rated[c.Id.Value].Average).ThenByDescending(c => rated[c.Id.Value].Count).ThenBy(c => c.Id.Value)
                    .Take(Math.Clamp(query.Limit, 1, MaxLimit))
                    .Select(c => new TopProfessionalResponse(
                        c.Id.Value, c.Slug, c.NameAr, c.NameEn, c.SpecialtyAr, c.SpecialtyEn, c.AvatarUrl,
                        shops[c.ShopId].Slug, shops[c.ShopId].NameAr, shops[c.ShopId].NameEn, rated[c.Id.Value].Average, rated[c.Id.Value].Count)),
            ];
            return Result.Success(top);
        }
    }
}
