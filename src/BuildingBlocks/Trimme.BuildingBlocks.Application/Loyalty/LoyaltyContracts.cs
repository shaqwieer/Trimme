using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.BuildingBlocks.Application.Loyalty;

/// <summary>
/// Holds one of the signed-in customer's loyalty rewards for a new online booking (implemented by the Loyalty module,
/// D-132). It runs in the booking's transaction and the customer's own data scope, so only that customer's usable reward
/// of that shop can be held; the hold is saved with the booking. Anything else is 422 <c>loyalty.reward_unavailable</c>.
/// </summary>
public interface ILoyaltyRewardHolder
{
    Task<Result<HeldLoyaltyReward>> HoldAsync(Guid rewardId, ShopId shopId, Guid bookingId, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>The held reward and its title, snapshotted on the booking so the shop sees what to apply at the visit.</summary>
public sealed record HeldLoyaltyReward(Guid RewardId, string TitleAr, string? TitleEn);

/// <summary>
/// A shop's loyalty activity for the owner's reports (implemented by the Loyalty module, D-147), read in the shop's own
/// scope: the circles stamped and the rewards redeemed in a period, each by booking, so the report can say which barber
/// closed the visit that gave or used them; and the terms of rewards used on bookings, to price those visits.
/// </summary>
public interface ILoyaltyReportSource
{
    Task<LoyaltyActivity> ActivityAsync(ShopId shopId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, LoyaltyRewardTerms>> TermsAsync(IReadOnlyCollection<Guid> rewardIds, CancellationToken cancellationToken);
}

/// <param name="ProgrammeActive">The shop's programme is on.</param>
/// <param name="Stamps">One entry per circle stamped in the period (a first visit may stamp several).</param>
/// <param name="Redemptions">Rewards redeemed at a visit in the period.</param>
public sealed record LoyaltyActivity(bool ProgrammeActive, IReadOnlyList<LoyaltyStampEntry> Stamps, IReadOnlyList<LoyaltyRedemptionEntry> Redemptions);

public sealed record LoyaltyStampEntry(Guid BookingId, DateTimeOffset EarnedAt);

public sealed record LoyaltyRedemptionEntry(Guid RewardId, Guid? BookingId, DateTimeOffset RedeemedAt);

/// <summary>A free service makes the visit's expected income zero; a discount takes its percentage off.</summary>
public sealed record LoyaltyRewardTerms(bool FreeService, int? DiscountPercent);
