using System.Security.Cryptography;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.Modules.Loyalty.Domain;

public readonly record struct LoyaltyProgramId(Guid Value) : IEntityId<LoyaltyProgramId>
{
    public static LoyaltyProgramId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct LoyaltyStampId(Guid Value) : IEntityId<LoyaltyStampId>
{
    public static LoyaltyStampId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct LoyaltyRewardId(Guid Value) : IEntityId<LoyaltyRewardId>
{
    public static LoyaltyRewardId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

/// <summary>What a full card gives: a free service, or a percentage off; the title says which (e.g. «حلاقة مجانية»).</summary>
public enum LoyaltyRewardKind
{
    FreeService,
    Discount,
}

public enum LoyaltyRewardStatus
{
    /// <summary>Earned and not used; usable until <c>ExpiresAt</c>, when there is one.</summary>
    Available,

    /// <summary>Chosen for an upcoming booking; released if that booking is cancelled or a no-show.</summary>
    Held,

    /// <summary>Used: the booking it was held for was completed.</summary>
    Redeemed,
}

/// <summary>The terms the platform agreed with the shop (D-132), as an admin enters them.</summary>
public sealed record LoyaltyTerms(
    bool IsActive,
    int RequiredVisits,
    LoyaltyRewardKind RewardKind,
    int? DiscountPercent,
    string? RewardTitleAr,
    string? RewardTitleEn,
    int? RewardValidityDays);

/// <summary>
/// A shop's loyalty programme (D-132): after <see cref="RequiredVisits"/> completed online bookings at the shop, with any
/// of its professionals, the customer earns a reward. One per shop; the platform admin sets it after agreeing the terms
/// with the shop. Switching it off stops new stamps; rewards already earned stay usable.
/// </summary>
public sealed class LoyaltyProgram : AggregateRoot<LoyaltyProgramId>, IShopOwned, IPublicContent, IConcurrencyVersioned
{
    public const int MinVisits = 2;
    public const int MaxVisits = 20;
    public const int MaxTitleLength = 60;
    public const int MinValidityDays = 7;
    public const int MaxValidityDays = 365;

    private LoyaltyProgram(LoyaltyProgramId id, ShopId shopId, DateTimeOffset now)
        : base(id)
    {
        ShopId = shopId;
        RewardTitleAr = string.Empty;
        CreatedAt = now;
    }

    private LoyaltyProgram()
    {
        RewardTitleAr = string.Empty;
    }

    public ShopId ShopId { get; private set; }

    public bool IsActive { get; private set; }

    public int RequiredVisits { get; private set; }

    public LoyaltyRewardKind RewardKind { get; private set; }

    /// <summary>1–100 for a discount; null for a free service.</summary>
    public int? DiscountPercent { get; private set; }

    public string RewardTitleAr { get; private set; }

    public string? RewardTitleEn { get; private set; }

    /// <summary>How long an earned reward stays usable; null means it does not expire.</summary>
    public int? RewardValidityDays { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public static Result<LoyaltyProgram> Create(LoyaltyProgramId id, ShopId shopId, LoyaltyTerms terms, DateTimeOffset now)
    {
        var program = new LoyaltyProgram(id, shopId, now);
        var applied = program.Apply(terms, updatedAt: null);
        return applied.IsFailure ? applied.Error : program;
    }

    public Result Update(LoyaltyTerms terms, DateTimeOffset now) => Apply(terms, now);

    private Result Apply(LoyaltyTerms terms, DateTimeOffset? updatedAt)
    {
        ArgumentNullException.ThrowIfNull(terms);
        if (Validate(terms) is { } invalid)
        {
            return invalid;
        }

        IsActive = terms.IsActive;
        RequiredVisits = terms.RequiredVisits;
        RewardKind = terms.RewardKind;
        DiscountPercent = terms.RewardKind == LoyaltyRewardKind.Discount ? terms.DiscountPercent : null;
        RewardTitleAr = terms.RewardTitleAr!.Trim();
        RewardTitleEn = string.IsNullOrWhiteSpace(terms.RewardTitleEn) ? null : terms.RewardTitleEn.Trim();
        RewardValidityDays = terms.RewardValidityDays;
        UpdatedAt = updatedAt;
        return Result.Success();
    }

    private static Error? Validate(LoyaltyTerms terms)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (terms.RequiredVisits is < MinVisits or > MaxVisits)
        {
            errors["requiredVisits"] = ["validation.out_of_range"];
        }

        if (!Enum.IsDefined(terms.RewardKind))
        {
            errors["rewardKind"] = ["validation.invalid"];
        }
        else if (terms.RewardKind == LoyaltyRewardKind.Discount && terms.DiscountPercent is not (>= 1 and <= 100))
        {
            errors["discountPercent"] = ["validation.out_of_range"];
        }

        if (string.IsNullOrWhiteSpace(terms.RewardTitleAr))
        {
            errors["rewardTitleAr"] = ["validation.required"];
        }
        else if (terms.RewardTitleAr.Trim().Length > MaxTitleLength)
        {
            errors["rewardTitleAr"] = ["validation.too_long"];
        }

        if (terms.RewardTitleEn?.Trim().Length > MaxTitleLength)
        {
            errors["rewardTitleEn"] = ["validation.too_long"];
        }

        if (terms.RewardValidityDays is { } days && days is < MinValidityDays or > MaxValidityDays)
        {
            errors["rewardValidityDays"] = ["validation.out_of_range"];
        }

        return errors.Count == 0 ? null : Error.Validation("validation.failed", "The loyalty programme is invalid.", errors);
    }
}

/// <summary>
/// One circle on a customer's card at a shop: a completed online booking (D-132). A card is the shop's open stamps (no
/// <see cref="RewardId"/>); when there are enough, the oldest ones turn into a reward together. Walk-ins have no customer,
/// so they never earn one; a visit that used a reward earns none either.
/// </summary>
public sealed class LoyaltyStamp : Entity<LoyaltyStampId>, ICustomerOwned
{
    private LoyaltyStamp(LoyaltyStampId id, ShopId shopId, Guid customerId, Guid bookingId, DateTimeOffset earnedAt)
        : base(id)
    {
        ShopId = shopId;
        CustomerId = customerId;
        BookingId = bookingId;
        EarnedAt = earnedAt;
    }

    private LoyaltyStamp()
    {
    }

    public ShopId ShopId { get; private set; }

    public Guid? CustomerId { get; private set; }

    /// <summary>The completed booking; one stamp per booking.</summary>
    public Guid BookingId { get; private set; }

    public DateTimeOffset EarnedAt { get; private set; }

    /// <summary>The reward this stamp was turned into; null while it is on the open card.</summary>
    public LoyaltyRewardId? RewardId { get; private set; }

    public static LoyaltyStamp Earn(LoyaltyStampId id, ShopId shopId, Guid customerId, Guid bookingId, DateTimeOffset now) =>
        new(id, shopId, customerId, bookingId, now);

    public void TurnInto(LoyaltyRewardId rewardId) => RewardId ??= rewardId;
}

/// <summary>
/// A reward a customer earned at a shop (D-132), with the programme's terms snapshotted, so later edits never change it.
/// The customer chooses it when booking (it is then held for that booking); it is redeemed when the shop completes the
/// booking, and released if the booking is cancelled or a no-show. The shop applies it at the visit: prices are unchanged.
/// </summary>
public sealed class LoyaltyReward : AggregateRoot<LoyaltyRewardId>, ICustomerOwned, IConcurrencyVersioned
{
    public const int CodeLength = 8;

    private LoyaltyReward(LoyaltyRewardId id, LoyaltyProgram program, Guid customerId, string code, DateTimeOffset now)
        : base(id)
    {
        ShopId = program.ShopId;
        CustomerId = customerId;
        Code = code;
        Kind = program.RewardKind;
        DiscountPercent = program.DiscountPercent;
        TitleAr = program.RewardTitleAr;
        TitleEn = program.RewardTitleEn;
        Status = LoyaltyRewardStatus.Available;
        IssuedAt = now;
        ExpiresAt = program.RewardValidityDays is { } days ? now.AddDays(days) : null;
    }

    private LoyaltyReward()
    {
        Code = TitleAr = string.Empty;
    }

    public ShopId ShopId { get; private set; }

    public Guid? CustomerId { get; private set; }

    /// <summary>A short code (8 unambiguous characters) the customer can quote at the shop.</summary>
    public string Code { get; private set; }

    public LoyaltyRewardKind Kind { get; private set; }

    public int? DiscountPercent { get; private set; }

    public string TitleAr { get; private set; }

    public string? TitleEn { get; private set; }

    public LoyaltyRewardStatus Status { get; private set; }

    public DateTimeOffset IssuedAt { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <summary>The booking it is held for or was redeemed on.</summary>
    public Guid? BookingId { get; private set; }

    public DateTimeOffset? RedeemedAt { get; private set; }

    public uint Version { get; private set; }

    public static LoyaltyReward Issue(LoyaltyRewardId id, LoyaltyProgram program, Guid customerId, string code, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(program);
        return new LoyaltyReward(id, program, customerId, code, now);
    }

    public bool IsUsable(DateTimeOffset now) => Status == LoyaltyRewardStatus.Available && (ExpiresAt is null || now < ExpiresAt);

    public Result Hold(Guid bookingId, DateTimeOffset now)
    {
        if (!IsUsable(now))
        {
            return LoyaltyErrors.RewardUnavailable();
        }

        Status = LoyaltyRewardStatus.Held;
        BookingId = bookingId;
        return Result.Success();
    }

    /// <summary>The booking it was held for was cancelled or not attended: usable again (if not expired by then).</summary>
    public void Release(Guid bookingId)
    {
        if (Status == LoyaltyRewardStatus.Held && BookingId == bookingId)
        {
            Status = LoyaltyRewardStatus.Available;
            BookingId = null;
        }
    }

    public void Redeem(Guid bookingId, DateTimeOffset now)
    {
        if (Status == LoyaltyRewardStatus.Held && BookingId == bookingId)
        {
            Status = LoyaltyRewardStatus.Redeemed;
            RedeemedAt = now;
        }
    }
}

/// <summary>The card rules (D-132).</summary>
public static class LoyaltyCard
{
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    /// <summary>The stamps that fill a card: the oldest <paramref name="requiredVisits"/> open ones, once there are enough.</summary>
    public static IReadOnlyList<LoyaltyStamp>? Filling(IReadOnlyCollection<LoyaltyStamp> open, int requiredVisits) =>
        open.Count >= requiredVisits ? [.. open.OrderBy(s => s.EarnedAt).ThenBy(s => s.Id.Value).Take(requiredVisits)] : null;

    public static string NewCode() => RandomNumberGenerator.GetString(CodeAlphabet, LoyaltyReward.CodeLength);
}

public static class LoyaltyErrors
{
    public static Error ShopNotFound() => Error.NotFound("shop.not_found", "The shop was not found.");

    public static Error RewardUnavailable() =>
        Error.BusinessRule("loyalty.reward_unavailable", "This reward is not available: it was used, has expired or belongs to another salon.");
}
