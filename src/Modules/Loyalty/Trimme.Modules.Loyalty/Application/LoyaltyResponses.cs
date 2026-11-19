using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.Modules.Loyalty.Domain;

namespace Trimme.Modules.Loyalty.Application;

/// <summary>
/// A shop's loyalty rules (D-149): <c>FirstVisitPoints</c> for the first visit and <c>VisitPoints</c> for each later one;
/// a reward every <c>PointsForReward</c>: a service free (<c>RewardServiceId</c>, with its names) or <c>RewardAmount</c>
/// SAR off; and without visits, nothing (<c>None</c>), a wipe after <c>InactivityDays</c>, or one point off per period of
/// <c>InactivityDays</c>.
/// </summary>
public sealed record LoyaltyRulesResponse(
    int FirstVisitPoints,
    int VisitPoints,
    int PointsForReward,
    LoyaltyRewardType RewardType,
    Guid? RewardServiceId,
    string? RewardServiceNameAr,
    string? RewardServiceNameEn,
    decimal? RewardAmount,
    LoyaltyInactivityRule InactivityRule,
    int? InactivityDays);

/// <summary>
/// How the programme is used at one shop: members (numbers with a card), the rewards ready on their cards, held for
/// upcoming bookings and used, and the points earned so far.
/// </summary>
public sealed record LoyaltyStatsResponse(int Members, int RewardsReady, int RewardsHeld, int RewardsRedeemed, int PointsEarned);

public sealed record LoyaltyShopResponse(Guid Id, string Slug, string NameAr, string NameEn);

/// <summary>A D-132 coupon as its customer sees it: available (until <c>ExpiresAt</c>) or held for <c>BookingId</c>.</summary>
public sealed record MyLoyaltyRewardResponse(
    Guid Id,
    string Code,
    LoyaltyRewardKind Kind,
    int? DiscountPercent,
    string TitleAr,
    string? TitleEn,
    LoyaltyRewardStatus Status,
    DateTimeOffset IssuedAt,
    DateTimeOffset? ExpiresAt,
    Guid? BookingId);

/// <summary>
/// A line of the card's history: points earned at a visit (with the barber who closed it), a reward used, or points lost
/// without visits. A used reward carries its title (D-152, the client's 2-5: «استخدمت: حلاقة شعر مجانية — ١٩ نوفمبر»):
/// a points reward's, or a D-132 coupon's (<c>Redeemed</c> with 0 points, dated when the visit was completed).
/// </summary>
public sealed record LoyaltyHistoryEntryResponse(
    DateTimeOffset At, LoyaltyEntryKind Kind, int Points, Guid? BookingId, string? BarberName, string? RewardTitleAr = null, string? RewardTitleEn = null);

/// <summary>
/// The customer's card at one shop (D-149), keyed by their account's mobile: whether the shop runs loyalty now and its
/// rules; the available points, the full cards ready to use (<c>RewardsReady</c>), the points on the card being filled
/// (<c>CurrentCard</c>: 8 points for a reward of 6 is 1 ready and 2 here, «+٢») and how many are left
/// (<c>PointsToNext</c>); rewards held for upcoming bookings; the history (newest first); and D-132 coupons still usable.
/// </summary>
public sealed record MyShopLoyaltyResponse(
    LoyaltyShopResponse Shop,
    bool Active,
    LoyaltyRulesResponse? Rules,
    int Points,
    int RewardsReady,
    int CurrentCard,
    int PointsToNext,
    int RewardsHeld,
    IReadOnlyList<LoyaltyHistoryEntryResponse> History,
    IReadOnlyList<MyLoyaltyRewardResponse> Coupons);

public sealed record MyLoyaltyResponse(IReadOnlyList<MyShopLoyaltyResponse> Items);

/// <summary>A shop's loyalty rules as the owner or the admin edits them: the add-on in force, the rules (null before the first save) and their version.</summary>
public sealed record LoyaltyRulesEditor(bool Included, LoyaltyRulesResponse? Rules, uint? Version);

internal static class LoyaltyMapping
{
    public static LoyaltyRulesResponse Rules(LoyaltySettings s) =>
        new(s.FirstVisitPoints, s.VisitPoints, s.PointsForReward, s.RewardType, s.RewardServiceId, s.RewardServiceNameAr, s.RewardServiceNameEn,
            s.RewardAmount, s.InactivityRule, s.InactivityDays);

    public static LoyaltyShopResponse Shop(ShopSummary s) => new(s.Id.Value, s.Slug, s.NameAr, s.NameEn);

    public static MyLoyaltyRewardResponse Reward(LoyaltyReward r) =>
        new(r.Id.Value, r.Code, r.Kind, r.DiscountPercent, r.TitleAr, r.TitleEn, r.Status, r.IssuedAt, r.ExpiresAt, r.BookingId);

    /// <summary>A history line; a used points reward carries the title of the reward used (<paramref name="used"/>).</summary>
    public static LoyaltyHistoryEntryResponse Entry(LoyaltyEntry e, LoyaltyRedemption? used = null) =>
        new(e.At, e.Kind, e.Points, e.BookingId, e.ClosedByName, used?.TitleAr, used?.TitleEn);

    /// <summary>A D-132 coupon used at a completed visit, as a history line (D-152).</summary>
    public static LoyaltyHistoryEntryResponse CouponUsed(LoyaltyReward r) =>
        new(r.RedeemedAt ?? r.IssuedAt, LoyaltyEntryKind.Redeemed, 0, r.BookingId, null, r.TitleAr, r.TitleEn);
}
