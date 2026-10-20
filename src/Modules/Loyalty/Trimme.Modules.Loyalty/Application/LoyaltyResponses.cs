using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.Modules.Loyalty.Domain;

namespace Trimme.Modules.Loyalty.Application;

/// <summary>
/// A shop's loyalty terms (D-132): after <c>RequiredVisits</c> circles, the reward named by the title. The first visit
/// fills <c>FirstVisitStamps</c> circles (1 when there is no welcome bonus, D-133), every later visit one.
/// </summary>
public sealed record LoyaltyProgramResponse(
    bool IsActive,
    int RequiredVisits,
    int FirstVisitStamps,
    LoyaltyRewardKind RewardKind,
    int? DiscountPercent,
    string RewardTitleAr,
    string? RewardTitleEn,
    int? RewardValidityDays);

/// <summary>How the programme is used at one shop: customers with a stamp, stamps on open cards, and rewards by state.</summary>
public sealed record LoyaltyStatsResponse(int Members, int OpenStamps, int RewardsIssued, int RewardsAvailable, int RewardsRedeemed);

public sealed record LoyaltyShopResponse(Guid Id, string Slug, string NameAr, string NameEn);

/// <summary>A reward as its customer sees it: available (until <c>ExpiresAt</c>) or held for <c>BookingId</c>.</summary>
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
/// The customer's card at one shop: the active programme (null when the shop has none now), the stamps on the open card
/// and the rewards they can still use or have chosen for an upcoming booking.
/// </summary>
public sealed record MyShopLoyaltyResponse(
    LoyaltyShopResponse Shop,
    LoyaltyProgramResponse? Program,
    int Stamps,
    IReadOnlyList<MyLoyaltyRewardResponse> Rewards);

public sealed record MyLoyaltyResponse(IReadOnlyList<MyShopLoyaltyResponse> Items);

internal static class LoyaltyMapping
{
    public static LoyaltyProgramResponse Program(LoyaltyProgram p) =>
        new(p.IsActive, p.RequiredVisits, p.FirstVisitStamps, p.RewardKind, p.DiscountPercent, p.RewardTitleAr, p.RewardTitleEn, p.RewardValidityDays);

    public static LoyaltyShopResponse Shop(ShopSummary s) => new(s.Id.Value, s.Slug, s.NameAr, s.NameEn);

    public static MyLoyaltyRewardResponse Reward(LoyaltyReward r) =>
        new(r.Id.Value, r.Code, r.Kind, r.DiscountPercent, r.TitleAr, r.TitleEn, r.Status, r.IssuedAt, r.ExpiresAt, r.BookingId);
}
