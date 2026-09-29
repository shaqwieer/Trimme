using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Discovery;
using Trimme.BuildingBlocks.Application.Media;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Platform;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Shops.Domain;

namespace Trimme.Modules.Shops.Application.Public;

/// <summary>Where a customer finds the shop: the confirmed point and the address, without who confirmed it.</summary>
public sealed record PublicShopLocationResponse(
    double Latitude,
    double Longitude,
    string? AddressLine,
    string? District,
    string? City,
    string? FormattedAddress);

/// <summary>A weekly opening window: minutes from the weekday's local midnight; the end may pass midnight (up to 24 h later).</summary>
public sealed record OpeningIntervalResponse(DayOfWeek Day, int StartMinute, int EndMinute);

/// <summary>The shop's published rating (from stored reviews only; zero reviews means no rating to show).</summary>
public sealed record ShopRatingResponse(decimal Average, int Count);

/// <summary>
/// A published shop's public profile (<c>GET /public/shops/{slug}</c>). Only active shops are published. The phone is the
/// shop's own business number; professional and customer numbers are never part of a public contract (R-PRO-02). The
/// response is cached (D-093), so nothing in it depends on the time of day: open status and availability come from
/// <c>/status</c>. <c>ListedInDiscovery</c> is false while the subscription hides the shop (the page is then not indexed).
/// </summary>
public sealed record PublicShopResponse(
    Guid Id,
    string Slug,
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    ShopCategory Category,
    string? PublicPhone,
    IReadOnlyList<ShopAmenity> Amenities,
    bool IsVerified,
    string? LogoUrl,
    string? CoverUrl,
    IReadOnlyList<string> GalleryUrls,
    PublicShopLocationResponse? Location,
    string TimeZone,
    ShopRatingResponse Rating,
    decimal? MinPrice,
    string? Currency,
    IReadOnlyList<OpeningIntervalResponse> OpeningHours,
    int CancellationCutoffMinutes,
    bool ListedInDiscovery,
    DateTimeOffset UpdatedAt);

/// <summary>When a professional can next be booked online (today or tomorrow, their shortest bookable service).</summary>
public sealed record ProfessionalAvailabilityResponse(Guid ProfessionalId, DateTimeOffset? NextAvailableAt);

/// <summary>
/// The time-sensitive part of the shop page (not cached): open now, closes at or opens next, whether online booking is
/// open (and why not: <c>shop.paused</c>, <c>subscription.expired</c>…), and each active professional's next free time.
/// </summary>
public sealed record PublicShopStatusResponse(
    bool IsOpenNow,
    DateTimeOffset? ClosesAt,
    DateTimeOffset? NextOpensAt,
    bool AcceptsOnlineBookings,
    string? BlockedReason,
    IReadOnlyList<ProfessionalAvailabilityResponse> Professionals);

internal sealed record GetPublicShopQuery(string Slug) : IQuery<PublicShopResponse?>;

internal sealed record GetPublicShopStatusQuery(string Slug) : IQuery<PublicShopStatusResponse?>;

/// <summary>
/// Public read inside <see cref="IPublicDataScope"/>, so a signed-in shop user sees another shop's public page exactly
/// as an anonymous visitor does, and nothing unpublished. A shop hidden from discovery (paused, or subscription not in
/// force) keeps its page for existing links (D-013).
/// </summary>
internal sealed class GetPublicShopHandler(
    TrimmeDbContext db,
    IPublicDataScope scope,
    IShopBookability bookability,
    IShopOfferReader offers,
    IRatingReader ratings,
    IShopOpeningReader opening,
    IPlatformSettings settings) : IQueryHandler<GetPublicShopQuery, PublicShopResponse?>
{
    public async Task<PublicShopResponse?> Handle(GetPublicShopQuery query, CancellationToken cancellationToken)
    {
        var slug = query.Slug.Trim().ToLowerInvariant();
        Shop? shop;
        using (scope.Begin(shopId: null))
        {
            shop = await db.Set<Shop>().AsNoTracking()
                .SingleOrDefaultAsync(s => s.Slug == slug && s.Status == ShopStatus.Active, cancellationToken);
        }

        if (shop is null)
        {
            return null;
        }

        var rating = (await ratings.GetAsync(RatingSubject.Shop, [shop.Id.Value], cancellationToken)).GetValueOrDefault(shop.Id.Value) ?? RatingSummary.Empty;
        var platform = await settings.GetAsync(cancellationToken);
        ShopOfferSummary? summary;
        IReadOnlyList<OpeningInterval> week;
        ShopBookability gate;
        using (scope.Begin(shop.Id))
        {
            gate = await bookability.GetAsync(shop.Id, cancellationToken);
            summary = (await offers.SummarizeAsync([shop.Id], OfferMatch.None, cancellationToken)).GetValueOrDefault(shop.Id);
            week = await opening.GetWeekAsync(shop.Id, cancellationToken);
        }

        return new PublicShopResponse(
            shop.Id.Value,
            shop.Slug,
            shop.NameAr,
            shop.NameEn,
            shop.DescriptionAr,
            shop.DescriptionEn,
            shop.Category,
            shop.PublicPhone,
            shop.Amenities,
            shop.IsVerified,
            MediaRules.Url(shop.LogoMediaId),
            MediaRules.Url(shop.CoverMediaId),
            [.. ShopInputMapping.Gallery(shop).Select(image => image.Url)],
            shop.Location is { } location
                ? new PublicShopLocationResponse(location.Latitude, location.Longitude, location.AddressLine, location.District, location.City, location.FormattedAddress)
                : null,
            shop.TimeZone,
            new ShopRatingResponse(rating.Average, rating.Count),
            summary?.MinPrice,
            summary?.Currency,
            [.. week.Select(i => new OpeningIntervalResponse(i.Day, i.StartMinute, i.EndMinute))],
            platform.CancellationCutoffMinutes,
            gate.VisibleInDiscovery && shop.Location is not null && summary is not null,
            shop.UpdatedAt ?? shop.CreatedAt);
    }
}

/// <summary>
/// The shop page's live status (D-091, D-093): opening from the engine's rules, the bookability gate, and for each active
/// professional the first bookable start today or tomorrow of their shortest online-bookable offer (one engine run per
/// distinct offer).
/// </summary>
internal sealed class GetPublicShopStatusHandler(
    IPublicDataScope scope,
    IShopDirectory shops,
    IShopBookability bookability,
    IShopOfferReader offers,
    IShopOpeningReader opening,
    ISlotProbe probe,
    IProfessionalDirectory professionals,
    TimeProvider clock) : IQueryHandler<GetPublicShopStatusQuery, PublicShopStatusResponse?>
{
    /// <summary>Enough starts for every professional's first one over the probe window.</summary>
    private const int MaxProbeSlots = 2000;

    public async Task<PublicShopStatusResponse?> Handle(GetPublicShopStatusQuery query, CancellationToken cancellationToken)
    {
        ShopSummary? shop;
        using (scope.Begin(shopId: null))
        {
            shop = await shops.FindBySlugAsync(query.Slug, cancellationToken);
        }

        if (shop is not { Status: ShopStatus.Active })
        {
            return null;
        }

        var now = clock.GetUtcNow();
        using var _ = scope.Begin(shop.Id);
        var gate = await bookability.GetAsync(shop.Id, cancellationToken);
        var status = (await opening.GetStatusesAsync(new Dictionary<ShopId, string> { [shop.Id] = shop.TimeZone }, now, cancellationToken))[shop.Id];
        var active = await professionals.ListActiveProfilesAsync([shop.Id], cancellationToken);
        var next = active.ToDictionary(p => p.Id, _ => (DateTimeOffset?)null);
        if (gate.AcceptsOnlineBookings && active.Count > 0)
        {
            // Each professional's shortest bookable offer; then one probe per distinct offer.
            var probeOffers = await offers.ListProbeOffersAsync(shop.Id, cancellationToken);
            var chosen = active
                .Select(p => (p.Id, Offer: probeOffers.Where(o => o.ProfessionalIds.Contains(p.Id))
                    .OrderBy(o => o.Offer.DurationMinutes).ThenBy(o => o.Offer.Price).FirstOrDefault()))
                .Where(x => x.Offer is not null)
                .GroupBy(x => x.Offer!.Offer.Id);
            var today = probe.Today(shop.TimeZone, now);
            foreach (var group in chosen)
            {
                var offer = group.First().Offer!;
                var who = group.Select(x => x.Id).ToList();
                var slots = await probe.ProbeAsync(
                    shop.Id, shop.TimeZone, offer.Offer.DurationMinutes, who, today, today.AddDays(DiscoveryCatalog.ProbeDays - 1), MaxProbeSlots, cancellationToken);
                foreach (var professional in who)
                {
                    next[professional] = slots.FirstOrDefault(s => s.ProfessionalIds.Contains(professional))?.StartsAt;
                }
            }
        }

        return new PublicShopStatusResponse(
            status.IsOpenNow,
            status.ClosesAt,
            status.NextOpensAt,
            gate.AcceptsOnlineBookings,
            gate.BlockedReason,
            [.. active.Select(p => new ProfessionalAvailabilityResponse(p.Id.Value, next[p.Id]))]);
    }
}
