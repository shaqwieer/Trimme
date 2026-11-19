using Trimme.BuildingBlocks.Application.Privacy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.BuildingBlocks.Application.Loyalty;

/// <summary>
/// Holds a loyalty reward for a new booking (implemented by the Loyalty module).
/// <list type="bullet">
/// <item><see cref="HoldAsync"/>: one of the signed-in customer's D-132 coupons, in the booking's transaction and the
/// customer's own data scope; anything else is 422 <c>loyalty.reward_unavailable</c>. Since D-152 it also says what the
/// coupon takes off the booking's <c>price</c> (a free-service coupon: all of it; a percent coupon: its percent).</item>
/// <item><see cref="HoldPointsRewardAsync"/>: «استخدم مكافأتي» (D-149), a ready reward of the member's points at the shop,
/// online or for a walk-in's typed number. It reads the shop's loyalty rows through the caller's scope (the public scope
/// of the shop for a customer, the tenant for a barber), serializes holds of one member at one shop with a transaction
/// lock, and adds the held redemption to the unit of work, saved with the booking or not at all.</item>
/// </list>
/// </summary>
public interface ILoyaltyRewardHolder
{
    Task<Result<HeldLoyaltyReward>> HoldAsync(Guid rewardId, ShopId shopId, Guid bookingId, decimal price, DateTimeOffset now, CancellationToken cancellationToken);

    Task<Result<HeldPointsReward>> HoldPointsRewardAsync(PointsRewardHold request, CancellationToken cancellationToken);
}

/// <summary>
/// The held D-132 coupon and its title, snapshotted on the booking, and what it takes off the price (D-152, the client's
/// 1-3: «الكوبون ما ينزّل السعر»), so the amount due, the barber's screen and the reports all show it.
/// </summary>
public sealed record HeldLoyaltyReward(Guid RewardId, string TitleAr, string? TitleEn, decimal Discount);

/// <summary>
/// A points reward to hold for a booking (D-149). Exactly one of <paramref name="CustomerId"/> (an online booking: the
/// account's mobile) and <paramref name="WalkInPhoneE164"/> (the number typed into a walk-in or phone booking) is set.
/// <paramref name="Services"/> are the booked services with the price each counts for, for a free-service reward.
/// </summary>
public sealed record PointsRewardHold(
    ShopId ShopId,
    Guid BookingId,
    Guid? CustomerId,
    string? WalkInPhoneE164,
    decimal Price,
    IReadOnlyList<BookedServicePrice> Services,
    DateTimeOffset Now)
{
    public override string ToString() => $"{nameof(PointsRewardHold)} {{ ShopId = {ShopId}, BookingId = {BookingId} }}";
}

public sealed record BookedServicePrice(Guid ServiceId, decimal Price);

/// <summary>The held reward (D-149): its redemption id, a title for the booking and the amount it takes off the price.</summary>
public sealed record HeldPointsReward(Guid RedemptionId, string TitleAr, string? TitleEn, decimal Discount);

public static class LoyaltyRewardErrors
{
    /// <summary>400: a reward cannot be used on a booking with a package (D-149).</summary>
    public static Error NotWithPackage() =>
        Error.Validation("loyalty.reward_not_with_package", "A loyalty reward cannot be used on a package.",
            new Dictionary<string, string[]>(StringComparer.Ordinal) { ["useLoyaltyReward"] = ["loyalty.reward_not_with_package"] });

    /// <summary>400: one reward per booking — a D-132 coupon and a points reward are not used together.</summary>
    public static Error OnePerBooking() =>
        Error.Validation("validation.failed", "One reward per booking.",
            new Dictionary<string, string[]>(StringComparer.Ordinal) { ["useLoyaltyReward"] = ["validation.invalid"] });

    /// <summary>400: a walk-in or phone booking redeems with the customer's typed mobile.</summary>
    public static Error PhoneRequired() =>
        Error.Validation("validation.failed", "The customer's mobile is needed to use a reward.",
            new Dictionary<string, string[]>(StringComparer.Ordinal) { ["customerPhone"] = ["validation.required"] });
}

/// <summary>
/// The loyalty member key of a customer account's mobile (implemented by the Identity module, D-149): the keyed hash only,
/// never the number, so request handlers may use it (unlike the contact readers, which are for background jobs).
/// </summary>
public interface ILoyaltyMemberKeys
{
    Task<string?> ForCustomerAsync(Guid customerId, CancellationToken cancellationToken);
}

/// <summary>The loyalty member key of a mobile (D-149): its keyed hash under <see cref="PersonalDataPurposes.LoyaltyMember"/>.</summary>
public static class LoyaltyMembers
{
    public static string KeyOf(IPersonalDataProtector protector, string e164)
    {
        ArgumentNullException.ThrowIfNull(protector);
        return protector.LookupHash(e164, PersonalDataPurposes.LoyaltyMember);
    }
}

/// <summary>
/// Whether a shop's paid loyalty add-on is in force (implemented by the Subscriptions module, D-149), from the platform's
/// coverage read model, so it needs no data scope. The add-on is a period with an end date; it stops by itself.
/// </summary>
public interface ILoyaltyAddOnStatus
{
    Task<bool> IsActiveAsync(ShopId shopId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<ShopId, bool>> ActiveManyAsync(IReadOnlyCollection<ShopId> shopIds, CancellationToken cancellationToken);

    /// <summary>The shops that have had the add-on at any time (where loyalty cards can exist), at most <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<ShopId>> ShopsWithAddOnAsync(int limit, CancellationToken cancellationToken);
}

/// <summary>
/// A shop's loyalty activity for the owner's reports (implemented by the Loyalty module, D-147), read in the shop's own
/// scope: the points earned and the rewards redeemed in a period, each by booking, so the report can say which barber
/// closed the visit that gave or used them; and the terms of D-132 coupons used on bookings, to price those visits.
/// </summary>
public interface ILoyaltyReportSource
{
    Task<LoyaltyActivity> ActivityAsync(ShopId shopId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, LoyaltyRewardTerms>> TermsAsync(IReadOnlyCollection<Guid> rewardIds, CancellationToken cancellationToken);
}

/// <param name="ProgrammeActive">The shop's loyalty add-on is in force (D-149).</param>
/// <param name="Stamps">One entry per point earned in the period (a first visit may earn several).</param>
/// <param name="Redemptions">Rewards redeemed at a visit in the period.</param>
public sealed record LoyaltyActivity(bool ProgrammeActive, IReadOnlyList<LoyaltyStampEntry> Stamps, IReadOnlyList<LoyaltyRedemptionEntry> Redemptions);

public sealed record LoyaltyStampEntry(Guid BookingId, DateTimeOffset EarnedAt);

public sealed record LoyaltyRedemptionEntry(Guid RewardId, Guid? BookingId, DateTimeOffset RedeemedAt);

/// <summary>A D-132 coupon: a free service makes the visit's expected income zero; a discount takes its percentage off.</summary>
public sealed record LoyaltyRewardTerms(bool FreeService, int? DiscountPercent);
