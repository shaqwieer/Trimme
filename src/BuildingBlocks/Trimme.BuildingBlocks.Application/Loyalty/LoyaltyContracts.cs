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
