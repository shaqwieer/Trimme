using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Discovery;
using Trimme.BuildingBlocks.Application.Media;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Platform;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Professionals.Domain;

namespace Trimme.Modules.Professionals.Application.Public;

/// <summary>A service or package the professional does, with the shop's own price and duration.</summary>
public sealed record PublicProfessionalOfferResponse(Guid Id, bool IsPackage, string NameAr, string? NameEn, decimal Price, string Currency, int DurationMinutes);

/// <summary>
/// A professional's public page (<c>/shops/{slug}/professionals/{proSlug}</c>): profile, their shop, rating and the
/// published services they do. Never carries a phone or WhatsApp number (R-PRO-02). Cached (D-093).
/// </summary>
public sealed record PublicProfessionalDetailResponse(
    Guid Id,
    string Slug,
    string NameAr,
    string NameEn,
    string? SpecialtyAr,
    string? SpecialtyEn,
    string? BioAr,
    string? BioEn,
    string? AvatarUrl,
    Guid ShopId,
    string ShopSlug,
    string ShopNameAr,
    string ShopNameEn,
    decimal Rating,
    int ReviewCount,
    IReadOnlyList<PublicProfessionalOfferResponse> Offers);

/// <summary>A bookable start: the instant and its local time (<c>HH:mm</c>, shop time zone).</summary>
public sealed record NextSlotResponse(DateTimeOffset StartsAt, string LocalTime);

/// <summary>
/// The professional's earliest bookable starts (the design's "earliest times available today"): the first day with any,
/// within a week, for their shortest online-bookable offer. Not cached. <c>Bookable</c> is false, with the reason, when
/// the shop takes no online bookings.
/// </summary>
public sealed record ProfessionalNextSlotsResponse(
    bool Bookable,
    string? BlockedReason,
    PublicProfessionalOfferResponse? Offer,
    DateOnly? Date,
    IReadOnlyList<NextSlotResponse> Slots,
    int RemainingCount);

internal sealed record GetPublicProfessionalQuery(string ShopSlug, string ProfessionalSlug) : IQuery<PublicProfessionalDetailResponse?>;

internal sealed record GetProfessionalNextSlotsQuery(string ShopSlug, string ProfessionalSlug) : IQuery<ProfessionalNextSlotsResponse?>;

internal static class PublicProfessionalQueries
{
    public const int NextSlotsDays = 7;
    public const int ShownSlots = 3;

    public static PublicProfessionalOfferResponse ToResponse(OfferSummary o) =>
        new(o.Id, o.IsPackage, o.NameAr, o.NameEn, o.Price, o.Currency, o.DurationMinutes);

    /// <summary>The active shop and its active professional, read in the public scope; null when either is not published.</summary>
    public static async Task<(ShopSummary Shop, Professional Professional)?> FindAsync(
        TrimmeDbContext db, IPublicDataScope scope, IShopDirectory shops, string shopSlug, string professionalSlug, CancellationToken cancellationToken)
    {
        ShopSummary? shop;
        using (scope.Begin(shopId: null))
        {
            shop = await shops.FindBySlugAsync(shopSlug, cancellationToken);
        }

        if (shop is not { Status: ShopStatus.Active })
        {
            return null;
        }

        var slug = professionalSlug.Trim().ToLowerInvariant();
        using var _ = scope.Begin(shop.Id);
        var professional = await db.Set<Professional>().AsNoTracking()
            .SingleOrDefaultAsync(p => p.ShopId == shop.Id && p.Slug == slug && p.Status == ProfessionalStatus.Active, cancellationToken);
        return professional is null ? null : (shop, professional);
    }
}

internal sealed class GetPublicProfessionalHandler(TrimmeDbContext db, IPublicDataScope scope, IShopDirectory shops, IShopOfferReader offers, IRatingReader ratings)
    : IQueryHandler<GetPublicProfessionalQuery, PublicProfessionalDetailResponse?>
{
    public async Task<PublicProfessionalDetailResponse?> Handle(GetPublicProfessionalQuery query, CancellationToken cancellationToken)
    {
        if (await PublicProfessionalQueries.FindAsync(db, scope, shops, query.ShopSlug, query.ProfessionalSlug, cancellationToken) is not var (shop, p))
        {
            return null;
        }

        IReadOnlyList<OfferSummary> list;
        using (scope.Begin(shop.Id))
        {
            list = await offers.ListForProfessionalAsync(shop.Id, p.Id, cancellationToken);
        }

        var rating = (await ratings.GetAsync(RatingSubject.Professional, [p.Id.Value], cancellationToken)).GetValueOrDefault(p.Id.Value) ?? RatingSummary.Empty;
        return new PublicProfessionalDetailResponse(
            p.Id.Value, p.Slug, p.NameAr, p.NameEn, p.SpecialtyAr, p.SpecialtyEn, p.BioAr, p.BioEn, MediaRules.Url(p.AvatarMediaId),
            shop.Id.Value, shop.Slug, shop.NameAr, shop.NameEn, rating.Average, rating.Count,
            [.. list.Select(PublicProfessionalQueries.ToResponse)]);
    }
}

internal sealed class GetProfessionalNextSlotsHandler(
    TrimmeDbContext db,
    IPublicDataScope scope,
    IShopDirectory shops,
    IShopBookability bookability,
    IShopOfferReader offers,
    ISlotProbe probe,
    TimeProvider clock) : IQueryHandler<GetProfessionalNextSlotsQuery, ProfessionalNextSlotsResponse?>
{
    /// <summary>Enough starts to count the rest of one day.</summary>
    private const int MaxProbeSlots = 500;

    public async Task<ProfessionalNextSlotsResponse?> Handle(GetProfessionalNextSlotsQuery query, CancellationToken cancellationToken)
    {
        if (await PublicProfessionalQueries.FindAsync(db, scope, shops, query.ShopSlug, query.ProfessionalSlug, cancellationToken) is not var (shop, p))
        {
            return null;
        }

        using var _ = scope.Begin(shop.Id);
        var gate = await bookability.GetAsync(shop.Id, cancellationToken);
        if (!gate.AcceptsOnlineBookings)
        {
            return new ProfessionalNextSlotsResponse(false, gate.BlockedReason, null, null, [], 0);
        }

        var offer = (await offers.ListProbeOffersAsync(shop.Id, cancellationToken))
            .Where(o => o.ProfessionalIds.Contains(p.Id))
            .OrderBy(o => o.Offer.DurationMinutes).ThenBy(o => o.Offer.Price)
            .FirstOrDefault();
        if (offer is null)
        {
            return new ProfessionalNextSlotsResponse(true, null, null, null, [], 0);
        }

        var today = probe.Today(shop.TimeZone, clock.GetUtcNow());
        for (var day = today; day < today.AddDays(PublicProfessionalQueries.NextSlotsDays); day = day.AddDays(1))
        {
            var slots = await probe.ProbeAsync(shop.Id, shop.TimeZone, offer.Offer.DurationMinutes, [p.Id], day, day, MaxProbeSlots, cancellationToken);
            if (slots.Count > 0)
            {
                return new ProfessionalNextSlotsResponse(
                    true, null, PublicProfessionalQueries.ToResponse(offer.Offer), day,
                    [.. slots.Take(PublicProfessionalQueries.ShownSlots).Select(s => new NextSlotResponse(s.StartsAt, s.LocalTime))],
                    Math.Max(0, slots.Count - PublicProfessionalQueries.ShownSlots));
            }
        }

        return new ProfessionalNextSlotsResponse(true, null, PublicProfessionalQueries.ToResponse(offer.Offer), null, [], 0);
    }
}
