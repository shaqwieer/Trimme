using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.BuildingBlocks.Application.Discovery;

/// <summary>
/// Published prices and offers of many shops at once, for discovery (implemented by the Services module, D-091). It reads
/// through the caller's data scope: discovery opens <c>IPublicDataScope.BeginMany</c> for the candidate shops first.
/// </summary>
public interface IShopOfferReader
{
    /// <summary>A summary per shop that has at least one published service or package; shops without one are absent.</summary>
    Task<IReadOnlyDictionary<ShopId, ShopOfferSummary>> SummarizeAsync(
        IReadOnlyCollection<ShopId> shopIds, OfferMatch match, CancellationToken cancellationToken);

    /// <summary>The published services and packages <paramref name="professionalId"/> can do, in the shop's display order.</summary>
    Task<IReadOnlyList<OfferSummary>> ListForProfessionalAsync(ShopId shopId, ProfessionalId professionalId, CancellationToken cancellationToken);

    /// <summary>The published, online-bookable offers of one shop with the professionals eligible for each.</summary>
    Task<IReadOnlyList<ProbeOffer>> ListProbeOffersAsync(ShopId shopId, CancellationToken cancellationToken);
}

/// <summary>What a discovery search asks for: a platform category, a normalized text query (<c>SearchText</c>), or neither.</summary>
public sealed record OfferMatch(Guid? CategoryId, string? NormalizedQuery)
{
    public static readonly OfferMatch None = new(null, null);

    public bool IsEmpty => CategoryId is null && string.IsNullOrEmpty(NormalizedQuery);
}

/// <summary>A published service or package as discovery shows it (a shop's own name, price and duration).</summary>
public sealed record OfferSummary(
    Guid Id, bool IsPackage, string NameAr, string? NameEn, decimal Price, string Currency, int DurationMinutes, bool OnlineBookable);

/// <summary>An online-bookable offer and the professionals who can do it (every item, for a package).</summary>
public sealed record ProbeOffer(OfferSummary Offer, IReadOnlyList<ProfessionalId> ProfessionalIds);

/// <param name="MinPrice">The cheapest published service or package.</param>
/// <param name="Currency">The currency of the prices.</param>
/// <param name="MinPriceByCategory">The cheapest published service per platform category (DV-S11 "from X").</param>
/// <param name="Matched">
/// The cheapest published offer matching the search (its category, or its name contains every query word); null when
/// the search asks for nothing or nothing matches.
/// </param>
/// <param name="Probes">
/// What the earliest-slot probe books, each with the professionals assigned to it (their status is not checked here):
/// the matched offer when it is online-bookable and assigned; otherwise each professional's shortest online-bookable offer,
/// one entry per distinct offer (D-091). Empty when nothing can be booked online.
/// </param>
public sealed record ShopOfferSummary(
    decimal MinPrice,
    string Currency,
    IReadOnlyDictionary<Guid, decimal> MinPriceByCategory,
    OfferSummary? Matched,
    IReadOnlyList<ProbeOffer> Probes);

/// <summary>
/// Opening status and hours of shops (implemented by the Availability module). Reads through the caller's data scope.
/// </summary>
public interface IShopOpeningReader
{
    /// <summary>Open now, closes at or opens next, per shop, computed from opening hours and closures at <paramref name="now"/>.</summary>
    Task<IReadOnlyDictionary<ShopId, ShopOpenStatus>> GetStatusesAsync(
        IReadOnlyDictionary<ShopId, string> timeZones, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>The shop's weekly opening hours (none when it has not set them).</summary>
    Task<IReadOnlyList<OpeningInterval>> GetWeekAsync(ShopId shopId, CancellationToken cancellationToken);
}

/// <param name="IsOpenNow">A window of the shop's hours contains now, on a day that is not closed.</param>
/// <param name="ClosesAt">When the current window ends (windows that touch are joined); null when closed.</param>
/// <param name="NextOpensAt">When it opens next within a week; null when open or when nothing opens in the next week.</param>
public sealed record ShopOpenStatus(bool IsOpenNow, DateTimeOffset? ClosesAt, DateTimeOffset? NextOpensAt);

/// <summary>A weekly opening window in minutes from the weekday's local midnight; the end may pass midnight (D-082).</summary>
public sealed record OpeningInterval(DayOfWeek Day, int StartMinute, int EndMinute);

/// <summary>
/// Genuinely bookable starts of one offer, for discovery's bounded probe (implemented by the Availability module, D-091).
/// It applies every online rule (lead time, horizon, the slot grid, collisions) and reads through the caller's scope.
/// </summary>
public interface ISlotProbe
{
    /// <summary>
    /// Bookable starts of an item of <paramref name="durationMinutes"/> for these professionals over local dates <paramref name="from"/>…<paramref name="to"/>,
    /// earliest first, at most <paramref name="maxSlots"/>. <paramref name="ignoreBookingId"/> is a booking being rescheduled,
    /// whose own time does not block it.
    /// </summary>
    Task<IReadOnlyList<ProbedSlot>> ProbeAsync(
        ShopId shopId,
        string timeZone,
        int durationMinutes,
        IReadOnlyList<ProfessionalId> professionalIds,
        DateOnly from,
        DateOnly to,
        int maxSlots,
        CancellationToken cancellationToken,
        Guid? ignoreBookingId = null);

    /// <summary>The shop's local date at <paramref name="now"/>.</summary>
    DateOnly Today(string timeZone, DateTimeOffset now);
}

/// <param name="StartsAt">The start instant.</param>
/// <param name="Date">Its local date.</param>
/// <param name="LocalTime">Its local time, <c>HH:mm</c>.</param>
/// <param name="ProfessionalIds">Every professional free for the whole item.</param>
/// <param name="Period">Morning, Afternoon or Evening (the slot grid's groups, D-009).</param>
public sealed record ProbedSlot(DateTimeOffset StartsAt, DateOnly Date, string LocalTime, IReadOnlyList<ProfessionalId> ProfessionalIds, string Period);

/// <summary>
/// Rating aggregates (implemented by the Reviews module, D-092). They are a platform read model with no personal data,
/// so no data scope is needed.
/// </summary>
public interface IRatingReader
{
    Task<IReadOnlyDictionary<Guid, RatingSummary>> GetAsync(RatingSubject subject, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}

public enum RatingSubject
{
    Shop,
    Professional,
}

/// <param name="Average">Mean rating rounded to one decimal; 0 when there are no reviews.</param>
/// <param name="Count">Published reviews.</param>
/// <param name="Histogram">Published reviews per star, index 0 = one star … index 4 = five stars.</param>
public sealed record RatingSummary(decimal Average, int Count, IReadOnlyList<int> Histogram)
{
    public static readonly RatingSummary Empty = new(0, 0, [0, 0, 0, 0, 0]);
}

/// <summary>
/// Public cards of shops by id, for the customer's favorites (implemented by the Shops module, D-098). Only shops
/// discovery would list are returned (active, visible, with something to book), in the order of the ids given.
/// </summary>
public interface IShopCards
{
    Task<IReadOnlyList<ShopCard>> GetAsync(IReadOnlyCollection<ShopId> shopIds, CancellationToken cancellationToken);
}

/// <summary>A published shop as the favorites list shows it (the shop's own business data only).</summary>
public sealed record ShopCard(
    ShopId Id,
    string Slug,
    string NameAr,
    string NameEn,
    bool IsVerified,
    string? District,
    decimal Rating,
    int ReviewCount,
    decimal? MinPrice,
    string? Currency,
    bool IsOpenNow,
    DateTimeOffset? ClosesAt,
    DateTimeOffset? NextOpensAt,
    string TimeZone,
    string? CoverUrl,
    string? LogoUrl);
