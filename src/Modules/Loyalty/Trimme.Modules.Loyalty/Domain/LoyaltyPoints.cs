using System.Globalization;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.Modules.Loyalty.Domain;

public readonly record struct LoyaltySettingsId(Guid Value) : IEntityId<LoyaltySettingsId>
{
    public static LoyaltySettingsId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct LoyaltyAccountId(Guid Value) : IEntityId<LoyaltyAccountId>
{
    public static LoyaltyAccountId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct LoyaltyEntryId(Guid Value) : IEntityId<LoyaltyEntryId>
{
    public static LoyaltyEntryId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct LoyaltyRedemptionId(Guid Value) : IEntityId<LoyaltyRedemptionId>
{
    public static LoyaltyRedemptionId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

/// <summary>What a reward gives (D-149): one of the shop's services free, or a fixed amount off.</summary>
public enum LoyaltyRewardType
{
    FreeService,
    AmountOff,
}

/// <summary>What happens to points without visits (D-149), counted from the last visit at the shop.</summary>
public enum LoyaltyInactivityRule
{
    /// <summary>Points are kept.</summary>
    None,

    /// <summary>The card is wiped after <c>InactivityDays</c> without a visit.</summary>
    Wipe,

    /// <summary>One point is taken off for each period of <c>InactivityDays</c> without a visit.</summary>
    DecayPerPeriod,
}

/// <summary>The owner's loyalty rules (D-149), as entered.</summary>
public sealed record LoyaltyRules(
    int FirstVisitPoints,
    int VisitPoints,
    int PointsForReward,
    LoyaltyRewardType RewardType,
    Guid? RewardServiceId,
    decimal? RewardAmount,
    LoyaltyInactivityRule InactivityRule,
    int? InactivityDays);

/// <summary>
/// A shop's loyalty rules (D-149, the client's 6-2): points for the first visit and for each later one, the points a
/// reward needs, the reward (a service free, or 5–1,000 SAR off), and what happens without visits. The owner sets them; the
/// admin can too. They apply only while the shop's paid loyalty add-on is in force.
/// </summary>
public sealed class LoyaltySettings : AggregateRoot<LoyaltySettingsId>, IShopOwned, IPublicContent, IConcurrencyVersioned
{
    public const int MaxVisitPoints = 100;
    public const int MinPointsForReward = 2;
    public const int MaxPointsForReward = 1000;
    public const decimal MinRewardAmount = 5m;
    public const decimal MaxRewardAmount = 1000m;
    public const int MinInactivityDays = 7;
    public const int MaxInactivityDays = 730;
    public const int MaxNameLength = 120;

    private LoyaltySettings(LoyaltySettingsId id, ShopId shopId, DateTimeOffset now)
        : base(id)
    {
        ShopId = shopId;
        CreatedAt = now;
    }

    private LoyaltySettings()
    {
    }

    public ShopId ShopId { get; private set; }

    public int FirstVisitPoints { get; private set; }

    public int VisitPoints { get; private set; }

    public int PointsForReward { get; private set; }

    public LoyaltyRewardType RewardType { get; private set; }

    /// <summary>The free service (one of the shop's), for <see cref="LoyaltyRewardType.FreeService"/>.</summary>
    public Guid? RewardServiceId { get; private set; }

    /// <summary>The service's names when the rule was saved, to show the reward.</summary>
    public string? RewardServiceNameAr { get; private set; }

    public string? RewardServiceNameEn { get; private set; }

    /// <summary>The amount off, for <see cref="LoyaltyRewardType.AmountOff"/>.</summary>
    public decimal? RewardAmount { get; private set; }

    public LoyaltyInactivityRule InactivityRule { get; private set; }

    public int? InactivityDays { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    /// <summary>New rules; <paramref name="serviceNames"/> are the free service's names (null for an amount off), checked by the caller to be the shop's.</summary>
    public static Result<LoyaltySettings> Create(LoyaltySettingsId id, ShopId shopId, LoyaltyRules rules, (string Ar, string? En)? serviceNames, DateTimeOffset now)
    {
        var settings = new LoyaltySettings(id, shopId, now);
        var applied = settings.Apply(rules, serviceNames, null);
        return applied.IsFailure ? applied.Error : settings;
    }

    public Result Update(LoyaltyRules rules, (string Ar, string? En)? serviceNames, DateTimeOffset now) => Apply(rules, serviceNames, now);

    /// <summary>
    /// The reward's title, snapshotted on the booking that uses it: the free service's name, or the amount off as the web
    /// app writes prices (D-148: «خصم ٥٠ ⃁», Arabic-Indic digits and the Riyal sign U+20C1; "SAR 50 off" in English). It is
    /// shown only in the app, never in a WhatsApp message.
    /// </summary>
    public (string Ar, string? En) RewardTitle()
    {
        if (RewardType == LoyaltyRewardType.FreeService)
        {
            return (RewardServiceNameAr ?? string.Empty, RewardServiceNameEn);
        }

        var latin = string.Create(CultureInfo.InvariantCulture, $"{RewardAmount:0.##}");
        var arabic = string.Concat(latin.Select(c => c switch
        {
            >= '0' and <= '9' => (char)('٠' + (c - '0')),
            '.' => '٫',
            _ => c,
        }));
        return ($"خصم {arabic} ⃁", $"SAR {latin} off");
    }

    public static Error? Validate(LoyaltyRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (rules.FirstVisitPoints is < 1 or > MaxVisitPoints)
        {
            errors["firstVisitPoints"] = ["validation.out_of_range"];
        }

        if (rules.VisitPoints is < 1 or > MaxVisitPoints)
        {
            errors["visitPoints"] = ["validation.out_of_range"];
        }

        if (rules.PointsForReward is < MinPointsForReward or > MaxPointsForReward)
        {
            errors["pointsForReward"] = ["validation.out_of_range"];
        }

        if (!Enum.IsDefined(rules.RewardType))
        {
            errors["rewardType"] = ["validation.invalid"];
        }
        else if (rules.RewardType == LoyaltyRewardType.FreeService && rules.RewardServiceId is null)
        {
            errors["rewardServiceId"] = ["validation.required"];
        }
        else if (rules.RewardType == LoyaltyRewardType.AmountOff && !IsValidAmount(rules.RewardAmount))
        {
            errors["rewardAmount"] = ["validation.out_of_range"];
        }

        if (!Enum.IsDefined(rules.InactivityRule))
        {
            errors["inactivityRule"] = ["validation.invalid"];
        }
        else if (rules.InactivityRule != LoyaltyInactivityRule.None && rules.InactivityDays is not (>= MinInactivityDays and <= MaxInactivityDays))
        {
            errors["inactivityDays"] = ["validation.out_of_range"];
        }

        return errors.Count == 0 ? null : Error.Validation("validation.failed", "The loyalty rules are invalid.", errors);
    }

    /// <summary>5 to 1,000 SAR, at most two decimals.</summary>
    public static bool IsValidAmount(decimal? amount) =>
        amount is { } value && value >= MinRewardAmount && value <= MaxRewardAmount && decimal.Round(value, 2) == value;

    private Result Apply(LoyaltyRules rules, (string Ar, string? En)? serviceNames, DateTimeOffset? updatedAt)
    {
        if (Validate(rules) is { } invalid)
        {
            return invalid;
        }

        FirstVisitPoints = rules.FirstVisitPoints;
        VisitPoints = rules.VisitPoints;
        PointsForReward = rules.PointsForReward;
        RewardType = rules.RewardType;
        RewardServiceId = rules.RewardType == LoyaltyRewardType.FreeService ? rules.RewardServiceId : null;
        RewardServiceNameAr = rules.RewardType == LoyaltyRewardType.FreeService ? serviceNames?.Ar : null;
        RewardServiceNameEn = rules.RewardType == LoyaltyRewardType.FreeService ? serviceNames?.En : null;
        RewardAmount = rules.RewardType == LoyaltyRewardType.AmountOff ? rules.RewardAmount : null;
        InactivityRule = rules.InactivityRule;
        InactivityDays = rules.InactivityRule == LoyaltyInactivityRule.None ? null : rules.InactivityDays;
        UpdatedAt = updatedAt;
        return Result.Success();
    }
}

/// <summary>
/// A loyalty member's points at one shop (D-149). The member is a mobile number, kept only as its keyed hash
/// (<see cref="MemberKey"/>): an online account's number, or the one typed into a walk-in or phone booking, so two accounts
/// with the same number share the card. <see cref="Points"/> includes the points held for upcoming bookings; they leave
/// when the visit is completed and stay when it is cancelled or a no-show. Written only by the loyalty consumer (completed
/// visits), never by a request.
/// </summary>
public sealed class LoyaltyAccount : Entity<LoyaltyAccountId>, IShopOwned, IConcurrencyVersioned
{
    public const int MemberKeyLength = 64;

    private LoyaltyAccount(LoyaltyAccountId id)
        : base(id)
    {
        MemberKey = string.Empty;
    }

    private LoyaltyAccount()
    {
        MemberKey = string.Empty;
    }

    public ShopId ShopId { get; private set; }

    public string MemberKey { get; private set; }

    public int Points { get; private set; }

    /// <summary>The last completed visit (the inactivity rules count from it); null before the first one.</summary>
    public DateTimeOffset? LastVisitAt { get; private set; }

    /// <summary>The inactivity periods since <see cref="LastVisitAt"/> already taken off.</summary>
    public int DecayPeriodsApplied { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public static LoyaltyAccount Open(ShopId shopId, string memberKey, DateTimeOffset now) =>
        new(LoyaltyAccountId.From(Guid.CreateVersion7(now))) { ShopId = shopId, MemberKey = memberKey, CreatedAt = now };

    /// <summary>
    /// Takes off what the inactivity rule says is lost by <paramref name="now"/>, from the points not held; returns the
    /// entry kind, the points lost (0 when nothing is) and when they were lost: the end of the last inactivity period.
    /// </summary>
    public (LoyaltyEntryKind Kind, int Lost, DateTimeOffset At) Settle(LoyaltySettings? settings, int held, DateTimeOffset now)
    {
        var (lost, periods) = LoyaltyBalance.Decay(this, settings, held, now);
        var at = LastVisitAt is { } last && settings?.InactivityDays is { } days
            ? last.AddDays((settings.InactivityRule == LoyaltyInactivityRule.Wipe ? 1 : Math.Max(periods, 1)) * days)
            : now;
        DecayPeriodsApplied = periods;
        if (lost > 0)
        {
            Points -= lost;
            UpdatedAt = now;
        }

        return (settings?.InactivityRule == LoyaltyInactivityRule.Wipe ? LoyaltyEntryKind.Wiped : LoyaltyEntryKind.Decayed, lost, at < now ? at : now);
    }

    /// <summary>A completed visit: its points, and the inactivity clock starts again.</summary>
    public void Visit(int points, DateTimeOffset now)
    {
        Points += Math.Max(points, 0);
        LastVisitAt = now;
        DecayPeriodsApplied = 0;
        UpdatedAt = now;
    }

    /// <summary>A held reward was used at its visit: its points leave the card.</summary>
    public void Consume(int points, DateTimeOffset now)
    {
        Points = Math.Max(Points - points, 0);
        UpdatedAt = now;
    }
}

public enum LoyaltyEntryKind
{
    /// <summary>Points for a completed visit.</summary>
    Earned,

    /// <summary>A reward used at a completed visit.</summary>
    Redeemed,

    /// <summary>The card was wiped after a stretch without visits.</summary>
    Wiped,

    /// <summary>Points taken off for periods without visits.</summary>
    Decayed,
}

/// <summary>
/// One line of a member's history at a shop (D-149): points earned at a visit (with the barber who closed it), a reward
/// used, or points lost without visits. A booking earns once (unique index).
/// </summary>
public sealed class LoyaltyEntry : Entity<LoyaltyEntryId>, IShopOwned
{
    private LoyaltyEntry(LoyaltyEntryId id)
        : base(id)
    {
        MemberKey = string.Empty;
    }

    private LoyaltyEntry()
    {
        MemberKey = string.Empty;
    }

    public ShopId ShopId { get; private set; }

    public string MemberKey { get; private set; }

    public LoyaltyEntryKind Kind { get; private set; }

    /// <summary>Positive for points earned, negative for points that left the card.</summary>
    public int Points { get; private set; }

    public Guid? BookingId { get; private set; }

    /// <summary>The barber of the visit (the booking's professional).</summary>
    public Guid? ProfessionalId { get; private set; }

    /// <summary>The account that closed the visit («انتهت الخدمة»), and its name then.</summary>
    public Guid? ClosedById { get; private set; }

    public string? ClosedByName { get; private set; }

    public DateTimeOffset At { get; private set; }

    public static LoyaltyEntry Of(
        LoyaltyAccount account, LoyaltyEntryKind kind, int points, Guid? bookingId, Guid? professionalId, Guid? closedById, string? closedByName, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(account);
        return new LoyaltyEntry(LoyaltyEntryId.From(Guid.CreateVersion7(now)))
        {
            ShopId = account.ShopId,
            MemberKey = account.MemberKey,
            Kind = kind,
            Points = points,
            BookingId = bookingId,
            ProfessionalId = professionalId,
            ClosedById = closedById,
            ClosedByName = closedByName is null ? null : closedByName[..Math.Min(closedByName.Length, LoyaltySettings.MaxNameLength)],
            At = now,
        };
    }
}

public enum LoyaltyRedemptionStatus
{
    /// <summary>Chosen for an upcoming booking; its points are set aside.</summary>
    Held,

    /// <summary>Used: the visit was completed.</summary>
    Consumed,

    /// <summary>The booking was cancelled or a no-show: the points are back on the card.</summary>
    Returned,
}

/// <summary>
/// A reward held for one booking (D-149, «استخدم مكافأتي»): the points it takes, the reward and the amount it takes off the
/// booking's price. Held at booking, consumed when the visit is completed, returned on a cancellation or a no-show. One per
/// booking. An online customer's own (they create it with their booking); a walk-in's has no customer.
/// </summary>
public sealed class LoyaltyRedemption : Entity<LoyaltyRedemptionId>, ICustomerOwned
{
    private LoyaltyRedemption(LoyaltyRedemptionId id)
        : base(id)
    {
        MemberKey = TitleAr = string.Empty;
    }

    private LoyaltyRedemption()
    {
        MemberKey = TitleAr = string.Empty;
    }

    public ShopId ShopId { get; private set; }

    public Guid? CustomerId { get; private set; }

    public string MemberKey { get; private set; }

    public Guid BookingId { get; private set; }

    public int Points { get; private set; }

    public LoyaltyRewardType RewardType { get; private set; }

    public Guid? ServiceId { get; private set; }

    public decimal Discount { get; private set; }

    public string TitleAr { get; private set; }

    public string? TitleEn { get; private set; }

    public LoyaltyRedemptionStatus Status { get; private set; }

    public DateTimeOffset HeldAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public static LoyaltyRedemption Hold(
        ShopId shopId, Guid? customerId, string memberKey, Guid bookingId, LoyaltySettings settings, decimal discount, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var (titleAr, titleEn) = settings.RewardTitle();
        return new LoyaltyRedemption(LoyaltyRedemptionId.From(Guid.CreateVersion7(now)))
        {
            ShopId = shopId,
            CustomerId = customerId,
            MemberKey = memberKey,
            BookingId = bookingId,
            Points = settings.PointsForReward,
            RewardType = settings.RewardType,
            ServiceId = settings.RewardServiceId,
            Discount = discount,
            TitleAr = titleAr[..Math.Min(titleAr.Length, LoyaltySettings.MaxNameLength)],
            TitleEn = titleEn is null ? null : titleEn[..Math.Min(titleEn.Length, LoyaltySettings.MaxNameLength)],
            Status = LoyaltyRedemptionStatus.Held,
            HeldAt = now,
        };
    }

    public bool Consume(DateTimeOffset now) => Close(LoyaltyRedemptionStatus.Consumed, now);

    public bool Return(DateTimeOffset now) => Close(LoyaltyRedemptionStatus.Returned, now);

    private bool Close(LoyaltyRedemptionStatus status, DateTimeOffset now)
    {
        if (Status != LoyaltyRedemptionStatus.Held)
        {
            return false;
        }

        Status = status;
        ClosedAt = now;
        return true;
    }
}

/// <summary>
/// The balance rules (D-149). A card shows the points available (not held, minus what is lost without visits), how many
/// rewards are ready and the points on the card being filled: a full card does not stop, so 8 points for a reward of 6 is
/// one reward ready and 2 on the next card («+٢»).
/// </summary>
public static class LoyaltyBalance
{
    /// <summary>
    /// The points lost by <paramref name="now"/> under the rule, from the points not held, and the inactivity periods
    /// counted since the last visit. A wipe takes them all once the period has passed; a decay takes one per period not yet
    /// taken.
    /// </summary>
    public static (int Lost, int Periods) Decay(LoyaltyAccount account, LoyaltySettings? settings, int held, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (account.LastVisitAt is not { } last || settings is null || settings.InactivityRule == LoyaltyInactivityRule.None
            || settings.InactivityDays is not { } days || days <= 0)
        {
            return (0, account.DecayPeriodsApplied);
        }

        var free = Math.Max(account.Points - held, 0);
        var periods = Math.Max((int)Math.Floor((now - last).TotalDays / days), 0);
        return settings.InactivityRule == LoyaltyInactivityRule.Wipe
            ? (periods >= 1 ? free : 0, Math.Max(periods, account.DecayPeriodsApplied))
            : (Math.Min(free, Math.Max(periods - account.DecayPeriodsApplied, 0)), Math.Max(periods, account.DecayPeriodsApplied));
    }

    /// <summary>The points the member can use now: not held, and after what is lost without visits.</summary>
    public static int Available(LoyaltyAccount? account, LoyaltySettings? settings, int held, DateTimeOffset now) =>
        account is null ? 0 : Math.Max(account.Points - held - Decay(account, settings, held, now).Lost, 0);

    public static LoyaltyCardState Card(int available, int pointsForReward) =>
        pointsForReward <= 0
            ? new LoyaltyCardState(available, 0, available, 0)
            : new LoyaltyCardState(available, available / pointsForReward, available % pointsForReward, pointsForReward - (available % pointsForReward));

    /// <summary>
    /// What a reward takes off a booking: the amount (never more than the price), or the free service's price when the
    /// booking includes it; null when it does not.
    /// </summary>
    public static decimal? Discount(LoyaltySettings settings, decimal price, IReadOnlyList<(Guid ServiceId, decimal Price)> services)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(services);
        if (settings.RewardType == LoyaltyRewardType.AmountOff)
        {
            return Math.Min(settings.RewardAmount ?? 0m, Math.Max(price, 0m));
        }

        foreach (var (serviceId, servicePrice) in services)
        {
            if (serviceId == settings.RewardServiceId)
            {
                return Math.Min(servicePrice, Math.Max(price, 0m));
            }
        }

        return null;
    }
}

/// <param name="Points">Available points.</param>
/// <param name="RewardsReady">Full cards, each a reward to use.</param>
/// <param name="CurrentCard">Points on the card being filled.</param>
/// <param name="PointsToNext">Points left for the next reward.</param>
public sealed record LoyaltyCardState(int Points, int RewardsReady, int CurrentCard, int PointsToNext);

public static class LoyaltyPointErrors
{
    public static Error NotRunning() => Error.BusinessRule("loyalty.not_active", "The shop's loyalty programme is not running.");

    public static Error NoRewardReady() => Error.BusinessRule("loyalty.reward_unavailable", "No reward is ready on this card.");

    public static Error ServiceNotBooked() =>
        Error.Validation("loyalty.reward_service_not_booked", "The free service of the reward is not in this booking.",
            new Dictionary<string, string[]>(StringComparer.Ordinal) { ["useLoyaltyReward"] = ["loyalty.reward_service_not_booked"] });

    public static Error InvalidService() =>
        Error.Validation("validation.failed", "The reward service is not one of the shop's services.",
            new Dictionary<string, string[]>(StringComparer.Ordinal) { ["rewardServiceId"] = ["validation.invalid"] });
}
