using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.Modules.Bookings.Domain;

public readonly record struct BookingId(Guid Value) : IEntityId<BookingId>
{
    public static BookingId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

/// <summary>The booking lifecycle (D-016).</summary>
public enum BookingStatus
{
    Pending,
    Confirmed,
    Arrived,
    Completed,
    CancelledByCustomer,
    CancelledByShop,
    NoShow,
}

public enum BookingChannel
{
    Online,

    /// <summary>The customer is in the shop: a barber records it and the time is taken at once (or later that day).</summary>
    WalkIn,

    /// <summary>The customer called: a barber records an appointment at another time (D-147). Like a walk-in, «سجّله المحل».</summary>
    Phone,
}

/// <summary>
/// The ready reasons a barber taps to cancel a booking (D-151, the client's 2-7), so the owner's reports can count them.
/// <see cref="Other"/> («سبب ثاني») needs the reason typed; the others may come without text.
/// </summary>
public enum ShopCancellationReason
{
    /// <summary>«الموظف تعبان».</summary>
    StaffUnwell,

    /// <summary>«ظرف طارئ».</summary>
    Emergency,

    /// <summary>«المحل مسكّر (كهرباء أو غيره)».</summary>
    ShopClosed,

    /// <summary>«سبب ثاني»: the text says what.</summary>
    Other,
}

public enum ActorType
{
    Customer,
    ShopUser,
    PlatformAdmin,
    System,
}

/// <summary>Payment seam (R-BKG-10): v1 takes no payments, so every booking is <see cref="NotApplicable"/>.</summary>
public enum PaymentStatus
{
    NotApplicable,
}

public enum BookingEventKind
{
    Created,
    StatusChanged,
    Rescheduled,

    /// <summary>The booking was given to another barber of the same shop, at the same time (D-145).</summary>
    ProfessionalChanged,

    /// <summary>A service added at the visit, after «وصل» (D-147); the reason holds its name.</summary>
    ServiceAdded,

    /// <summary>A service taken off before «انتهت الخدمة» (D-147); the reason holds its name.</summary>
    ServiceRemoved,

    /// <summary>The barber offered the customer a new time (D-147); the offer holds it until an hour before the start.</summary>
    PostponementOffered,

    /// <summary>The customer took the offered time: the booking moved (the previous start is kept).</summary>
    PostponementAccepted,

    /// <summary>The customer declined the offered time: the booking stays.</summary>
    PostponementDeclined,
}

/// <summary>
/// Who did something to a booking. The id is the user's (null for the system); the name is a snapshot of their display
/// name at the time (D-147), so the log still names a barber who was later renamed or removed.
/// </summary>
public sealed record BookingActor(Guid? Id, ActorType Type, string? Name = null)
{
    public static readonly BookingActor System = new(null, ActorType.System);
}

/// <summary>What was booked, as it was at booking time (R-BKG-01): later edits to the service never change it.</summary>
public sealed record BookedItem(Guid? ServiceId, Guid? PackageId, string NameAr, string? NameEn, decimal Price, string Currency, int DurationMinutes, IReadOnlyList<BookedPackageItem> PackageItems);

/// <summary>
/// One service of a booked package (reporting, D-072), of several services booked together (D-125), or added at the visit
/// (D-147). Services booked together and added ones carry their own price and length, so they can be taken off again;
/// a package's own services and items booked before D-147 have none.
/// </summary>
public sealed class BookedPackageItem
{
    public Guid ServiceId { get; set; }

    public string NameAr { get; set; } = string.Empty;

    public string? NameEn { get; set; }

    public decimal? Price { get; set; }

    public int? DurationMinutes { get; set; }

    /// <summary>Added by the barber after «وصل» (D-147).</summary>
    public bool Added { get; set; }

    public static BookedPackageItem Of(Guid serviceId, string nameAr, string? nameEn, decimal? price = null, int? durationMinutes = null) =>
        new() { ServiceId = serviceId, NameAr = nameAr, NameEn = nameEn, Price = price, DurationMinutes = durationMinutes };
}

/// <summary>The professional as named at booking time (and after a reschedule to another professional).</summary>
public sealed record BookedProfessional(ProfessionalId Id, string NameAr, string NameEn);

/// <summary>
/// How long a booking takes with a given barber (D-149: barbers have their own length per service): the whole, and each
/// booked service that carries its own length.
/// </summary>
public sealed record BookedLength(int DurationMinutes, IReadOnlyDictionary<Guid, int> ItemDurations);

/// <summary>One entry of the booking's history (R-BKG-02): every status change and reschedule, with actor and time.</summary>
public sealed class BookingHistoryEntry
{
    internal BookingHistoryEntry(
        BookingEventKind kind, BookingStatus? from, BookingStatus to, DateTimeOffset? previousStartsAt, BookingActor actor, string? reason, DateTimeOffset at,
        BookedProfessional? previousProfessional = null)
    {
        Kind = kind;
        FromStatus = from;
        ToStatus = to;
        PreviousStartsAt = previousStartsAt;
        PreviousProfessionalId = previousProfessional?.Id.Value;
        PreviousProfessionalNameAr = previousProfessional?.NameAr;
        PreviousProfessionalNameEn = previousProfessional?.NameEn;
        ActorId = actor.Id;
        ActorType = actor.Type;
        ActorName = string.IsNullOrWhiteSpace(actor.Name) ? null : actor.Name.Trim()[..Math.Min(actor.Name.Trim().Length, BookingRules.MaxNameLength)];
        Reason = reason;
        OccurredAt = at.ToUniversalTime();
    }

    private BookingHistoryEntry()
    {
    }

    public int Id { get; private set; }

    public BookingEventKind Kind { get; private set; }

    public BookingStatus? FromStatus { get; private set; }

    public BookingStatus ToStatus { get; private set; }

    /// <summary>For a reschedule: the start it moved from.</summary>
    public DateTimeOffset? PreviousStartsAt { get; private set; }

    /// <summary>When the booking changed barber (D-145): who had it before, as they were named then.</summary>
    public Guid? PreviousProfessionalId { get; private set; }

    public string? PreviousProfessionalNameAr { get; private set; }

    public string? PreviousProfessionalNameEn { get; private set; }

    public Guid? ActorId { get; private set; }

    public ActorType ActorType { get; private set; }

    /// <summary>The actor's display name when it happened (D-147); null for the system and for entries made before D-147.</summary>
    public string? ActorName { get; private set; }

    public string? Reason { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
}

/// <summary>Booking rules that are not the state machine itself (D-087).</summary>
public static class BookingRules
{
    public const int MaxNameLength = 120;
    public const int MaxNoteLength = 500;
    public const int MaxReasonLength = 300;

    /// <summary>The column's length: «GH-» and 5 characters today (D-148); the 8-character references made before stay valid.</summary>
    public const int MaxReferenceLength = 12;

    /// <summary>A short reference read out over the phone (D-148: «GH-4K7QM»).</summary>
    public const string ReferencePrefix = "GH-";

    public const int ReferenceCodeLength = 5;

    /// <summary>A booked professional may be marked arrived from this long before the start.</summary>
    public static readonly TimeSpan ArrivalWindow = TimeSpan.FromMinutes(60);

    /// <summary>«ما حضر» is offered once the customer is this late (D-147: «تأخر أكثر من ربع ساعة»).</summary>
    public static readonly TimeSpan NoShowAfter = TimeSpan.FromMinutes(15);

    /// <summary>A cancellation this close to the start (or after it) is a late cancellation, «إلغاء متأخر» (D-147).</summary>
    public static readonly TimeSpan LateCancellationWindow = TimeSpan.FromMinutes(15);

    /// <summary>
    /// A booking closed this long after its booked end is «انقفل متأخر» (D-147) and leaves the speed figures. Red starts
    /// at the end itself (D-151, the client's «حجز فات وقته ٦:٠٠، والساعة ٦:٠٣ شكله عادي»); this quarter hour only keeps a
    /// visit that runs a little over out of the late figures.
    /// </summary>
    public static readonly TimeSpan ClosedLateAfter = TimeSpan.FromMinutes(15);

    /// <summary>Red bookings stay in every barber's list for this long after their start; then only the owner's reports show them.</summary>
    public static readonly TimeSpan OverdueVisibleFor = TimeSpan.FromDays(3);

    /// <summary>Unambiguous characters (no 0/O, 1/I/L) for references read out over the phone.</summary>
    private const string ReferenceAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    /// <summary>The statuses that hold the professional's time (the exclusion constraint uses the same list).</summary>
    public static readonly IReadOnlyList<BookingStatus> Active = [BookingStatus.Pending, BookingStatus.Confirmed, BookingStatus.Arrived];

    public static bool IsActive(BookingStatus status) => Active.Contains(status);

    /// <summary>A free-text field over its limit (after trimming) is a field error, never a database error.</summary>
    public static Error? TooLong(string? value, int max, string field) =>
        value?.Trim().Length > max
            ? Error.Validation("validation.failed", "The text is too long.", new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = ["validation.too_long"] })
            : null;

    public static bool IsCancelled(BookingStatus status) => status is BookingStatus.CancelledByCustomer or BookingStatus.CancelledByShop;

    /// <summary>
    /// «GH-» and 5 unambiguous characters, about 28 million codes (D-148). The unique index refuses a repeat; the creation
    /// paths then draw again (<see cref="Booking.RenewReference"/>).
    /// </summary>
    public static string NewReference() =>
        string.Create(ReferencePrefix.Length + ReferenceCodeLength, 0, (chars, _) =>
        {
            ReferencePrefix.AsSpan().CopyTo(chars);
            for (var i = ReferencePrefix.Length; i < chars.Length; i++)
            {
                chars[i] = ReferenceAlphabet[RandomNumberGenerator.GetInt32(ReferenceAlphabet.Length)];
            }
        });
}

/// <summary>
/// The allowed transitions (D-016). Anything else is refused with 409 <c>booking.invalid_transition</c>. Confirmed →
/// Completed is used only to close a red booking late (D-147); otherwise «وصل» comes first, so the visit has an actual
/// start (this replaces D-135's one-tap finish).
/// </summary>
public static class BookingStateMachine
{
    private static readonly Dictionary<BookingStatus, BookingStatus[]> Allowed = new()
    {
        [BookingStatus.Pending] = [BookingStatus.Confirmed, BookingStatus.CancelledByCustomer, BookingStatus.CancelledByShop],
        [BookingStatus.Confirmed] =
            [BookingStatus.Arrived, BookingStatus.Completed, BookingStatus.NoShow, BookingStatus.CancelledByCustomer, BookingStatus.CancelledByShop],
        [BookingStatus.Arrived] = [BookingStatus.Completed],
    };

    public static bool CanTransition(BookingStatus from, BookingStatus to) => Allowed.TryGetValue(from, out var next) && next.Contains(to);

    public static IReadOnlyList<BookingStatus> Next(BookingStatus from) => Allowed.TryGetValue(from, out var next) ? next : [];

    public static bool IsTerminal(BookingStatus status) => !Allowed.ContainsKey(status);
}

/// <summary>
/// An appointment with one professional of one shop (spec §11). It is shop-owned and, for online bookings,
/// customer-owned (D-085): the shop and the customer can read it; nobody else. It keeps a snapshot of what was booked,
/// the professional's name and the customer's name, so history never changes when the shop edits its catalogue.
/// The database refuses two active bookings of one professional that overlap (exclusion constraint, R-BKG-03).
/// </summary>
public sealed class Booking : AggregateRoot<BookingId>, ICustomerOwned, IConcurrencyVersioned
{
    private readonly List<BookingHistoryEntry> _history = [];

    private Booking(
        BookingId id, ShopId shopId, Guid? customerId, string customerName, BookedProfessional professional, BookedItem item,
        DateTimeOffset startsAt, BookingStatus status, BookingChannel channel, string? customerNote, DateTimeOffset now, int bufferMinutes)
        : base(id)
    {
        ShopId = shopId;
        CustomerId = customerId;
        CustomerName = customerName;
        Reference = BookingRules.NewReference();
        SetProfessional(professional);
        ServiceId = item.ServiceId;
        PackageId = item.PackageId;
        ItemNameAr = item.NameAr;
        ItemNameEn = item.NameEn;
        Price = item.Price;
        Currency = item.Currency;
        DurationMinutes = item.DurationMinutes;
        PackageItems = [.. item.PackageItems];
        // Instants are stored in UTC (timestamptz); clients may send any offset.
        StartsAt = startsAt.ToUniversalTime();
        BufferMinutes = Math.Max(bufferMinutes, 0);
        SetEnd(StartsAt.AddMinutes(item.DurationMinutes));
        Status = status;
        Channel = channel;
        CustomerNote = customerNote;
        PaymentStatus = PaymentStatus.NotApplicable;
        AmountDue = item.Price;
        CreatedAt = now.ToUniversalTime();
    }

    private Booking()
    {
        CustomerName = Reference = ProfessionalNameAr = ProfessionalNameEn = ItemNameAr = Currency = string.Empty;
    }

    public ShopId ShopId { get; private set; }

    /// <summary>The customer's user id; null for a walk-in.</summary>
    public Guid? CustomerId { get; private set; }

    /// <summary>The customer's name at booking time (a walk-in's name as the shop typed it). Never a phone number.</summary>
    public string CustomerName { get; private set; }

    /// <summary>Short code the customer and the shop use to find the booking (8 unambiguous characters).</summary>
    public string Reference { get; private set; }

    public ProfessionalId ProfessionalId { get; private set; }

    public string ProfessionalNameAr { get; private set; }

    public string ProfessionalNameEn { get; private set; }

    public Guid? ServiceId { get; private set; }

    public Guid? PackageId { get; private set; }

    public string ItemNameAr { get; private set; }

    public string? ItemNameEn { get; private set; }

    public decimal Price { get; private set; }

    public string Currency { get; private set; }

    public int DurationMinutes { get; private set; }

    public List<BookedPackageItem> PackageItems { get; private set; } = [];

    public DateTimeOffset StartsAt { get; private set; }

    public DateTimeOffset EndsAt { get; private set; }

    /// <summary>The shop's cleaning time after this booking, as set when it was made (D-149).</summary>
    public int BufferMinutes { get; private set; }

    /// <summary>
    /// When the barber is free again: the end plus <see cref="BufferMinutes"/>. The exclusion constraint and the
    /// postponement holds compare <c>[StartsAt, BlockedUntil)</c>, so the cleaning time cannot be double-booked.
    /// </summary>
    public DateTimeOffset BlockedUntil { get; private set; }

    public BookingStatus Status { get; private set; }

    public BookingChannel Channel { get; private set; }

    /// <summary>The customer's own note to the shop (or the shop's note on a walk-in).</summary>
    public string? CustomerNote { get; private set; }

    public string? CancellationReason { get; private set; }

    /// <summary>The ready reason the shop chose when it cancelled (D-151); null for older cancellations, rejections and the customer's.</summary>
    public ShopCancellationReason? CancellationReasonCode { get; private set; }

    /// <summary>Payment seam (R-BKG-10): no online payment in v1.</summary>
    public PaymentStatus PaymentStatus { get; private set; }

    /// <summary>What the customer owes at the shop: the booked price, snapshotted.</summary>
    public decimal AmountDue { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// The QR code whose scan led to this online booking (R-QR-02, D-114), when the visitor booked within the attribution
    /// window; a code of the same shop (composite key). Set once, when the booking is made; never for walk-ins.
    /// </summary>
    public QrCodeLinkId? QrLinkId { get; private set; }

    /// <summary>The scan (QR visit) the booking is credited to; it counts that scan as converted.</summary>
    public Guid? QrVisitId { get; private set; }

    /// <summary>
    /// The loyalty reward the customer chose for this online booking (D-132), with its title snapshotted: the shop applies
    /// it at the visit (the price and amount due are unchanged). Set once, when the booking is made.
    /// </summary>
    public Guid? LoyaltyRewardId { get; private set; }

    public string? LoyaltyRewardTitleAr { get; private set; }

    public string? LoyaltyRewardTitleEn { get; private set; }

    /// <summary>
    /// The points reward used on this booking (D-149, «استخدم مكافأتي»): its redemption, held at booking, used when the visit
    /// is completed and given back on a cancellation or a no-show. Set once, when the booking is made.
    /// </summary>
    public Guid? LoyaltyRedemptionId { get; private set; }

    /// <summary>What the points reward takes off the price (the amount, or the free service's price); 0 without one.</summary>
    public decimal LoyaltyDiscount { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public IReadOnlyList<BookingHistoryEntry> History => _history;

    /// <summary>
    /// When the shop last gave the booking to another barber (D-145); cleared when the customer reschedules it. While
    /// set, the customer may cancel or change the booking until it starts, whatever the cancellation window.
    /// </summary>
    public DateTimeOffset? ProfessionalChangedAt { get; private set; }

    public static Booking CreateOnline(
        BookingId id, ShopId shopId, Guid customerId, string customerName, BookedProfessional professional, BookedItem item,
        DateTimeOffset startsAt, bool requireManualConfirmation, string? customerNote, DateTimeOffset now, (QrCodeLinkId LinkId, Guid VisitId)? qrScan = null,
        int bufferMinutes = 0)
    {
        var status = requireManualConfirmation ? BookingStatus.Pending : BookingStatus.Confirmed;
        var booking = new Booking(id, shopId, customerId, customerName.Trim(), professional, item, startsAt, status, BookingChannel.Online, Clean(customerNote), now, bufferMinutes);
        booking.QrLinkId = qrScan?.LinkId;
        booking.QrVisitId = qrScan?.VisitId;
        booking._history.Add(new BookingHistoryEntry(BookingEventKind.Created, null, status, null, new BookingActor(customerId, ActorType.Customer, customerName), null, now));
        return booking;
    }

    /// <summary>
    /// D-035: a walk-in that starts now is Arrived (its actual start is now); a later one, and every phone booking (D-147),
    /// is Confirmed.
    /// </summary>
    public static Booking CreateWalkIn(
        BookingId id, ShopId shopId, string customerName, BookedProfessional professional, BookedItem item,
        DateTimeOffset startsAt, bool startsNow, string? note, BookingActor actor, DateTimeOffset now, BookingChannel channel = BookingChannel.WalkIn,
        int bufferMinutes = 0)
    {
        if (channel == BookingChannel.Online)
        {
            throw new ArgumentOutOfRangeException(nameof(channel), "A shop-recorded booking is a walk-in or a phone booking.");
        }

        var status = startsNow && channel == BookingChannel.WalkIn ? BookingStatus.Arrived : BookingStatus.Confirmed;
        var booking = new Booking(id, shopId, null, customerName.Trim(), professional, item, startsAt, status, channel, Clean(note), now, bufferMinutes);
        booking._history.Add(new BookingHistoryEntry(BookingEventKind.Created, null, status, null, actor, null, now));
        return booking;
    }

    /// <summary>
    /// Restores a booking with a given status and history, for the development seed only (past appointments cannot be
    /// created through the online rules).
    /// </summary>
    public static Booking Seeded(
        BookingId id, ShopId shopId, Guid? customerId, string customerName, BookedProfessional professional, BookedItem item,
        DateTimeOffset startsAt, BookingChannel channel, IReadOnlyList<BookingStatus> path, string? reason, DateTimeOffset createdAt,
        (QrCodeLinkId LinkId, Guid VisitId)? qrScan = null, int bufferMinutes = 0)
    {
        var booking = new Booking(id, shopId, customerId, customerName, professional, item, startsAt, path[0], channel, null, createdAt, bufferMinutes);
        booking.QrLinkId = qrScan?.LinkId;
        booking.QrVisitId = qrScan?.VisitId;
        booking._history.Add(new BookingHistoryEntry(BookingEventKind.Created, null, path[0], null, BookingActor.System, null, createdAt));
        foreach (var next in path.Skip(1))
        {
            // Visit steps happen when the visit does: arrival at the start, completion or no-show at the end.
            var at = next switch
            {
                BookingStatus.Arrived => booking.StartsAt,
                BookingStatus.Completed or BookingStatus.NoShow => booking.EndsAt,
                _ => createdAt,
            };
            booking._history.Add(new BookingHistoryEntry(BookingEventKind.StatusChanged, booking.Status, next, null, BookingActor.System, reason, at));
            booking.Status = next;
        }

        booking.CancellationReason = BookingRules.IsCancelled(booking.Status) ? reason : null;
        return booking;
    }

    public void UseLoyaltyReward(Guid rewardId, string titleAr, string? titleEn)
    {
        if (LoyaltyRewardId is not null || Channel != BookingChannel.Online)
        {
            throw new InvalidOperationException("A loyalty reward is chosen once, when an online booking is made.");
        }

        LoyaltyRewardId = rewardId;
        LoyaltyRewardTitleAr = titleAr;
        LoyaltyRewardTitleEn = titleEn;
    }

    /// <summary>
    /// A points reward (D-149): one per booking, never with a package. The amount due drops by its discount; the price stays
    /// the booked one.
    /// </summary>
    public void UsePointsReward(Guid redemptionId, string titleAr, string? titleEn, decimal discount)
    {
        if (LoyaltyRewardId is not null || LoyaltyRedemptionId is not null || PackageId is not null)
        {
            throw new InvalidOperationException("One reward per booking, and none on a package.");
        }

        LoyaltyRedemptionId = redemptionId;
        LoyaltyRewardTitleAr = titleAr;
        LoyaltyRewardTitleEn = titleEn;
        LoyaltyDiscount = Math.Clamp(discount, 0m, Price);
        AmountDue = Price - LoyaltyDiscount;
    }

    public bool IsActive => BookingRules.IsActive(Status);

    /// <summary>
    /// For a package booking, the services booked with it or added at the visit (D-148, D-147): the items that carry their
    /// own price. Empty for any other booking.
    /// </summary>
    public IReadOnlyList<Guid> PackageExtras =>
        PackageId is null ? [] : [.. PackageItems.Where(i => i.Price is not null && i.DurationMinutes is not null).Select(i => i.ServiceId)];

    /// <summary>Draws a new reference before the booking is first saved, when the drawn one is taken (D-148).</summary>
    internal void RenewReference() => Reference = BookingRules.NewReference();

    /// <summary>Recorded by the shop: a walk-in or a phone booking (D-147), as opposed to the customer's own online booking.</summary>
    public bool IsShopRecorded => Channel is BookingChannel.WalkIn or BookingChannel.Phone;

    /// <summary>When the shop marked the visit Completed (from the history); null while it is not Completed.</summary>
    public DateTimeOffset? CompletedAt =>
        Status == BookingStatus.Completed
            ? _history.Where(h => h.ToStatus == BookingStatus.Completed).Select(h => (DateTimeOffset?)h.OccurredAt).LastOrDefault()
            : null;

    /// <summary>
    /// The actual start (D-147): when «وصل» was pressed, or when a walk-in that started at once was recorded. Null when the
    /// arrival was never marked (a red booking closed late from Confirmed).
    /// </summary>
    public DateTimeOffset? ActualStartAt =>
        _history.Where(h => h.ToStatus == BookingStatus.Arrived && h.Kind is BookingEventKind.StatusChanged or BookingEventKind.Created)
            .Select(h => (DateTimeOffset?)h.OccurredAt).FirstOrDefault();

    /// <summary>The entry that closed the booking (completed, no-show or cancelled); null while it is open.</summary>
    public BookingHistoryEntry? ClosingEntry =>
        BookingStateMachine.IsTerminal(Status) ? _history.LastOrDefault(h => h.Kind == BookingEventKind.StatusChanged && h.ToStatus == Status) : null;

    /// <summary>Red (D-147; D-151: from the booked end itself): still open after its booked end.</summary>
    public bool IsOverdue(DateTimeOffset now) => IsActive && now > EndsAt;

    /// <summary>
    /// «انقفل متأخر» (D-147): closed more than <see cref="BookingRules.ClosedLateAfter"/> after its booked end; its times are
    /// not counted in the speed figures.
    /// </summary>
    public bool ClosedLate => ClosingEntry is { } closing && closing.OccurredAt > EndsAt + BookingRules.ClosedLateAfter;

    /// <summary>Cancelled less than <see cref="BookingRules.LateCancellationWindow"/> before the start, or after it: «إلغاء متأخر» (D-147).</summary>
    public bool CancelledLate =>
        BookingRules.IsCancelled(Status) && ClosingEntry is { } closing && closing.OccurredAt > StartsAt - BookingRules.LateCancellationWindow;

    /// <summary>
    /// A shop's transition (D-016, D-087, D-147): confirm, mark arrived (from an hour before the start), complete (after
    /// «وصل»; from Confirmed only to close a red booking late), mark a no-show (15 minutes after the start) or cancel (with
    /// a reason; never once arrived). Admins cancel through the same rule with their own actor. A visit finished before its
    /// booked end ends then (<see cref="FinishAt"/>). A cancellation may carry a ready reason (D-151): one other than
    /// <see cref="ShopCancellationReason.Other"/> needs no text; without one, or with «سبب ثاني», the text is required.
    /// </summary>
    public Result ApplyShopTransition(BookingStatus to, BookingActor actor, string? reason, DateTimeOffset now, ShopCancellationReason? reasonCode = null)
    {
        if (reasonCode is not null && to != BookingStatus.CancelledByShop)
        {
            return BookingErrors.ReasonCodeNotAllowed();
        }

        if (reasonCode is { } code && !Enum.IsDefined(code))
        {
            return BookingErrors.ReasonCodeNotAllowed();
        }

        if (!BookingStateMachine.CanTransition(Status, to) || to == BookingStatus.CancelledByCustomer)
        {
            return BookingErrors.InvalidTransition(Status, to);
        }

        var readyReason = reasonCode is { } chosen && chosen != ShopCancellationReason.Other;

        switch (to)
        {
            case BookingStatus.Arrived when now < StartsAt - BookingRules.ArrivalWindow:
            case BookingStatus.NoShow when now < StartsAt + BookingRules.NoShowAfter:
                return BookingErrors.TooEarly(to);
            case BookingStatus.Completed when Status == BookingStatus.Confirmed && !IsOverdue(now):
                return BookingErrors.ArrivalRequired();
            case BookingStatus.CancelledByShop when !readyReason && (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 3):
                return BookingErrors.ReasonRequired();
        }

        if (BookingRules.TooLong(reason, BookingRules.MaxReasonLength, "reason") is { } tooLong)
        {
            return tooLong;
        }

        Move(to, actor, Clean(reason), now);
        if (to == BookingStatus.CancelledByShop)
        {
            CancellationReasonCode = reasonCode;
        }

        if (to == BookingStatus.Completed)
        {
            FinishAt(now);
        }

        return Result.Success();
    }

    /// <summary>A customer cancels their own booking until the cutoff before the start (D-015).</summary>
    public Result CancelByCustomer(Guid customerId, string? reason, DateTimeOffset now, int cutoffMinutes)
    {
        if (!BookingStateMachine.CanTransition(Status, BookingStatus.CancelledByCustomer))
        {
            return BookingErrors.InvalidTransition(Status, BookingStatus.CancelledByCustomer);
        }

        if (CutoffApplies(now, cutoffMinutes))
        {
            return BookingErrors.CutoffPassed(cutoffMinutes);
        }

        if (BookingRules.TooLong(reason, BookingRules.MaxReasonLength, "reason") is { } tooLong)
        {
            return tooLong;
        }

        Move(BookingStatus.CancelledByCustomer, new BookingActor(customerId, ActorType.Customer, CustomerName), Clean(reason), now);
        return Result.Success();
    }

    /// <summary>
    /// Moves a Pending or Confirmed booking to a new start (and optionally another professional) with the same snapshot
    /// and status (D-087). The caller has rechecked availability; the customer's cutoff applies to the current start.
    /// <paramref name="bufferMinutes"/> is the shop's cleaning time now, the one the new place was checked with (D-149).
    /// </summary>
    public Result Reschedule(
        DateTimeOffset newStart, BookedProfessional professional, BookingActor actor, DateTimeOffset now, int? cutoffMinutes, string? reason = null,
        BookedLength? length = null, int? bufferMinutes = null)
    {
        if (Status is not (BookingStatus.Pending or BookingStatus.Confirmed))
        {
            return BookingErrors.NotReschedulable(Status);
        }

        if (cutoffMinutes is { } cutoff && CutoffApplies(now, cutoff))
        {
            return BookingErrors.CutoffPassed(cutoff);
        }

        var previous = StartsAt;
        var previousProfessional = CurrentProfessional;
        StartsAt = newStart.ToUniversalTime();

        // Another barber takes the booking for their own length (D-149); the same barber keeps the booked one.
        if (length is not null)
        {
            ApplyLength(length);
        }

        UseBuffer(bufferMinutes);
        SetEnd(StartsAt.AddMinutes(DurationMinutes));
        SetProfessional(professional);
        UpdatedAt = now;

        // The customer chose this time and barber: a free change offered after a barber change is used up (D-145).
        if (actor.Type == ActorType.Customer)
        {
            ProfessionalChangedAt = null;
        }
        else if (professional.Id != previousProfessional.Id)
        {
            ProfessionalChangedAt = now;
        }

        _history.Add(new BookingHistoryEntry(
            BookingEventKind.Rescheduled, Status, Status, previous, actor, Clean(reason), now,
            professional.Id != previousProfessional.Id ? previousProfessional : null));
        return Result.Success();
    }

    /// <summary>
    /// Gives a Pending or Confirmed booking that has not started to another barber of the same shop, at the same time
    /// (D-145), for that barber's own length when given (D-149). The caller has checked that the barber does the booked
    /// item and is free for it. Until the start, the customer may then cancel or change it even inside the cancellation window.
    /// </summary>
    public Result ChangeProfessional(
        BookedProfessional professional, BookingActor actor, DateTimeOffset now, BookedLength? length = null, int? bufferMinutes = null)
    {
        if (Status is not (BookingStatus.Pending or BookingStatus.Confirmed))
        {
            return BookingErrors.NotReschedulable(Status);
        }

        if (now >= StartsAt)
        {
            return BookingErrors.StartInPast();
        }

        if (professional.Id == ProfessionalId)
        {
            return BookingErrors.SameProfessional();
        }

        var previous = CurrentProfessional;
        SetProfessional(professional);
        if (length is not null)
        {
            ApplyLength(length);
        }

        UseBuffer(bufferMinutes);
        SetEnd(StartsAt.AddMinutes(DurationMinutes));
        ProfessionalChangedAt = now;
        UpdatedAt = now;
        _history.Add(new BookingHistoryEntry(BookingEventKind.ProfessionalChanged, Status, Status, null, actor, null, now, previous));
        return Result.Success();
    }

    /// <summary>
    /// Adds a service at the visit (D-147): only once the customer has arrived («إضافة خدمة مسموحة بعد وصل بس»). The booking
    /// gets longer and dearer by the service's own length and price; the caller has checked that the barber does it and is
    /// free for the extra time (the exclusion constraint has the last word).
    /// </summary>
    public Result AddService(BookedPackageItem service, BookingActor actor, DateTimeOffset now)
    {
        if (Status != BookingStatus.Arrived)
        {
            return BookingErrors.AddAfterArrival();
        }

        if (service.Price is not { } price || service.DurationMinutes is not { } minutes || minutes <= 0)
        {
            throw new ArgumentException("An added service carries its price and length.", nameof(service));
        }

        if (ServiceId == service.ServiceId || PackageItems.Any(i => i.ServiceId == service.ServiceId))
        {
            return BookingErrors.ServiceAlreadyBooked();
        }

        // A single service becomes the first of a list, with its own price and length.
        List<BookedPackageItem> items = PackageItems.Count == 0 && ServiceId is { } first
            ? [BookedPackageItem.Of(first, ItemNameAr, ItemNameEn, Price, DurationMinutes)]
            : [.. PackageItems];
        items.Add(new BookedPackageItem
        {
            ServiceId = service.ServiceId, NameAr = service.NameAr, NameEn = service.NameEn, Price = price, DurationMinutes = minutes, Added = true,
        });
        PackageItems = items;
        Price += price;
        AmountDue = Math.Max(Price - LoyaltyDiscount, 0m);
        DurationMinutes += minutes;
        SetEnd(EndsAt.AddMinutes(minutes));
        Rename(PackageId is null ? null : service, null);
        UpdatedAt = now;
        _history.Add(new BookingHistoryEntry(BookingEventKind.ServiceAdded, Status, Status, null, actor, Truncated(service.NameAr), now));
        return Result.Success();
    }

    /// <summary>
    /// Takes a service off an open booking until «انتهت الخدمة» (D-147). At least one service stays; a package's own
    /// services stay with it (only services added to it can go); items booked before D-147 have no price of their own and
    /// stay too.
    /// </summary>
    public Result RemoveService(Guid serviceId, BookingActor actor, DateTimeOffset now)
    {
        if (!IsActive)
        {
            return BookingErrors.ServicesClosed(Status);
        }

        if (PackageItems.Count == 0)
        {
            return ServiceId == serviceId ? BookingErrors.LastService() : BookingErrors.ServiceNotBooked();
        }

        if (PackageItems.FirstOrDefault(i => i.ServiceId == serviceId) is not { } item)
        {
            return BookingErrors.ServiceNotBooked();
        }

        // A package's own services stay with it; services booked with it (D-148) or added at the visit carry their own price.
        if (PackageId is not null && item.Price is null)
        {
            return BookingErrors.PackageServiceFixed();
        }

        if (PackageId is null && PackageItems.Count == 1)
        {
            return BookingErrors.LastService();
        }

        if (item.Price is not { } price || item.DurationMinutes is not { } minutes)
        {
            return BookingErrors.PackageServiceFixed();
        }

        List<BookedPackageItem> items = [.. PackageItems.Where(i => i != item)];
        Price -= price;
        AmountDue = Math.Max(Price - LoyaltyDiscount, 0m);
        DurationMinutes -= minutes;
        SetEnd(EndsAt.AddMinutes(-minutes));
        if (PackageId is null)
        {
            ServiceId = items[0].ServiceId;
            if (items.Count == 1)
            {
                // Back to one service: the plain single-service form (D-125).
                ItemNameAr = items[0].NameAr;
                ItemNameEn = items[0].NameEn;
                items = [];
            }
        }

        PackageItems = items;
        if (PackageId is not null || items.Count > 0)
        {
            Rename(null, PackageId is null ? null : item);
        }

        UpdatedAt = now;
        _history.Add(new BookingHistoryEntry(BookingEventKind.ServiceRemoved, Status, Status, null, actor, Truncated(item.NameAr), now));
        return Result.Success();
    }

    /// <summary>
    /// The barber offers a customer's online booking a new time with them (D-147). Only a Pending or Confirmed online
    /// booking more than an hour before its start; the offer itself is a <see cref="PostponementOffer"/> the caller saves.
    /// </summary>
    public Result OfferPostponement(DateTimeOffset newStart, BookingActor actor, DateTimeOffset now)
    {
        if (Status is not (BookingStatus.Pending or BookingStatus.Confirmed))
        {
            return BookingErrors.NotReschedulable(Status);
        }

        if (Channel != BookingChannel.Online || CustomerId is null)
        {
            return PostponementErrors.OnlineOnly();
        }

        if (now >= StartsAt - PostponementOffer.AnswerBefore)
        {
            return PostponementErrors.TooLate();
        }

        if (newStart.ToUniversalTime() == StartsAt)
        {
            return PostponementErrors.SameTime();
        }

        UpdatedAt = now;
        _history.Add(new BookingHistoryEntry(BookingEventKind.PostponementOffered, Status, Status, null, actor, null, now));
        return Result.Success();
    }

    /// <summary>The customer takes the offered time (D-147): the booking moves there, whatever their own change window.</summary>
    public Result AcceptPostponement(PostponementOffer offer, Guid customerId, DateTimeOffset now)
    {
        if (CustomerId != customerId || !offer.IsLive(this, now))
        {
            return PostponementErrors.Gone();
        }

        var previous = StartsAt;
        StartsAt = offer.StartsAt;
        BufferMinutes = offer.BufferMinutes;
        SetEnd(offer.EndsAt);
        UpdatedAt = now;
        offer.Close(PostponementStatus.Accepted, now);
        _history.Add(new BookingHistoryEntry(
            BookingEventKind.PostponementAccepted, Status, Status, previous, new BookingActor(customerId, ActorType.Customer, CustomerName), null, now));
        return Result.Success();
    }

    /// <summary>The customer declines the offered time (D-147): the booking stays as it was, and the time is free again.</summary>
    public Result DeclinePostponement(PostponementOffer offer, Guid customerId, DateTimeOffset now)
    {
        if (CustomerId != customerId || !offer.IsLive(this, now))
        {
            return PostponementErrors.Gone();
        }

        UpdatedAt = now;
        offer.Close(PostponementStatus.Declined, now);
        _history.Add(new BookingHistoryEntry(
            BookingEventKind.PostponementDeclined, Status, Status, null, new BookingActor(customerId, ActorType.Customer, CustomerName), null, now));
        return Result.Success();
    }

    /// <summary>The barber as the booking names them now.</summary>
    public BookedProfessional CurrentProfessional => new(ProfessionalId, ProfessionalNameAr, ProfessionalNameEn);

    /// <summary>
    /// The customer's cancellation window (D-015) is closed, unless the shop gave the booking to another barber since
    /// the customer last chose it (D-145): then the customer may cancel or change it until it starts.
    /// </summary>
    public bool CutoffApplies(DateTimeOffset now, int cutoffMinutes) =>
        now > StartsAt.AddMinutes(-cutoffMinutes) && !(ProfessionalChangedAt is not null && now < StartsAt);

    /// <summary>What the shop may do next (the UI renders only these, DV-S08), by the time rules of <see cref="ApplyShopTransition"/>.</summary>
    public IReadOnlyList<BookingStatus> AllowedShopTransitions(DateTimeOffset now) =>
    [
        .. BookingStateMachine.Next(Status).Where(to => to switch
        {
            BookingStatus.CancelledByCustomer => false,
            BookingStatus.Arrived => now >= StartsAt - BookingRules.ArrivalWindow,
            BookingStatus.NoShow => now >= StartsAt + BookingRules.NoShowAfter,
            BookingStatus.Completed => Status == BookingStatus.Arrived || IsOverdue(now),
            _ => true,
        }),
    ];

    public bool CustomerCanChange(DateTimeOffset now, int cutoffMinutes) =>
        Status is BookingStatus.Pending or BookingStatus.Confirmed && !CutoffApplies(now, cutoffMinutes);

    /// <summary>
    /// A visit marked finished before its booked end ends at that minute (D-135): the calendar, the history and the
    /// customer's booking show when it really ended. The rest of the time is free either way, because only active bookings
    /// hold a professional's time. A visit finished before its start (arrived early) keeps its times.
    /// </summary>
    private void FinishAt(DateTimeOffset now)
    {
        var minute = now.ToUniversalTime().AddTicks(-(now.Ticks % TimeSpan.TicksPerMinute));
        if (minute > StartsAt && minute < EndsAt)
        {
            SetEnd(minute);
        }
    }

    /// <summary>A booking placed again takes the cleaning time its new place was checked with (D-149); none keeps its own.</summary>
    private void UseBuffer(int? bufferMinutes)
    {
        if (bufferMinutes is { } minutes)
        {
            BufferMinutes = Math.Max(minutes, 0);
        }
    }

    /// <summary>Sets the end, and with it when the barber is free again (the cleaning time after it, D-149).</summary>
    private void SetEnd(DateTimeOffset end)
    {
        EndsAt = end;
        BlockedUntil = end.AddMinutes(BufferMinutes);
    }

    /// <summary>Takes another barber's length (D-149): the whole, and each booked service that carries its own.</summary>
    private void ApplyLength(BookedLength length)
    {
        DurationMinutes = length.DurationMinutes;
        if (PackageItems.Any(i => i.DurationMinutes is not null && length.ItemDurations.ContainsKey(i.ServiceId)))
        {
            PackageItems =
            [
                .. PackageItems.Select(i => i.DurationMinutes is not null && length.ItemDurations.TryGetValue(i.ServiceId, out var minutes)
                    ? new BookedPackageItem { ServiceId = i.ServiceId, NameAr = i.NameAr, NameEn = i.NameEn, Price = i.Price, DurationMinutes = minutes, Added = i.Added }
                    : i),
            ];
        }
    }

    private void Move(BookingStatus to, BookingActor actor, string? reason, DateTimeOffset now)
    {
        _history.Add(new BookingHistoryEntry(BookingEventKind.StatusChanged, Status, to, null, actor, reason, now));
        Status = to;
        CancellationReason = BookingRules.IsCancelled(to) ? reason : CancellationReason;
        UpdatedAt = now;
    }

    [MemberNotNull(nameof(ProfessionalNameAr), nameof(ProfessionalNameEn))]
    private void SetProfessional(BookedProfessional professional)
    {
        ProfessionalId = professional.Id;
        ProfessionalNameAr = professional.NameAr;
        ProfessionalNameEn = professional.NameEn;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Truncated(string value) => value.Length <= BookingRules.MaxReasonLength ? value : value[..BookingRules.MaxReasonLength];

    private static string Joined(IEnumerable<string> names)
    {
        var joined = string.Join(" + ", names);
        return joined.Length <= BookingRules.MaxNameLength ? joined : joined[..BookingRules.MaxNameLength];
    }

    /// <summary>
    /// The booked item's name after a change (D-125's « + »): services booked together are named from their items; a
    /// package keeps its own name, with the added service appended or the removed one taken off its end.
    /// </summary>
    private void Rename(BookedPackageItem? appended, BookedPackageItem? removed)
    {
        if (PackageId is null)
        {
            ItemNameAr = Joined(PackageItems.Select(i => i.NameAr));
            ItemNameEn = PackageItems.All(i => !string.IsNullOrWhiteSpace(i.NameEn)) ? Joined(PackageItems.Select(i => i.NameEn!)) : null;
            return;
        }

        if (appended is not null)
        {
            ItemNameAr = Joined([ItemNameAr, appended.NameAr]);
            ItemNameEn = ItemNameEn is not null && appended.NameEn is not null ? Joined([ItemNameEn, appended.NameEn]) : ItemNameEn;
        }
        else if (removed is not null)
        {
            ItemNameAr = Without(ItemNameAr, removed.NameAr)!;
            ItemNameEn = Without(ItemNameEn, removed.NameEn);
        }

        static string? Without(string? name, string? part)
        {
            if (name is null || part is null)
            {
                return name;
            }

            var suffix = $" + {part}";
            var at = name.LastIndexOf(suffix, StringComparison.Ordinal);
            return at > 0 ? name.Remove(at, suffix.Length) : name;
        }
    }
}

/// <summary>A shop-internal note on a booking (never shown to the customer).</summary>
public sealed class BookingNote : IShopOwned
{
    public BookingNote(Guid id, BookingId bookingId, ShopId shopId, string text, Guid authorId, DateTimeOffset now)
    {
        Id = id;
        BookingId = bookingId;
        ShopId = shopId;
        Text = text.Trim();
        AuthorId = authorId;
        CreatedAt = now;
    }

    private BookingNote()
    {
        Text = string.Empty;
    }

    public Guid Id { get; private set; }

    public BookingId BookingId { get; private set; }

    public ShopId ShopId { get; private set; }

    public string Text { get; private set; }

    public Guid AuthorId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}

/// <summary>
/// The mobile number the desk took for a walk-in (D-134): written once with the booking, stored encrypted, and read
/// only by the platform team, masked or through an audited reveal. No shop response, event or notification carries it.
/// </summary>
public sealed class WalkInContact : IShopOwned
{
    public WalkInContact(BookingId bookingId, ShopId shopId, string protectedPhone, string phoneLookupHash, string phoneMasked, DateTimeOffset now)
    {
        BookingId = bookingId;
        ShopId = shopId;
        ProtectedPhone = protectedPhone;
        PhoneLookupHash = phoneLookupHash;
        PhoneMasked = phoneMasked;
        CreatedAt = now;
    }

    private WalkInContact()
    {
        ProtectedPhone = PhoneLookupHash = PhoneMasked = string.Empty;
    }

    public BookingId BookingId { get; private set; }

    public ShopId ShopId { get; private set; }

    /// <summary>Data Protection ciphertext of the E.164 number (purpose <c>trimme.walk-in-phone</c>).</summary>
    public string ProtectedPhone { get; private set; }

    /// <summary>Keyed hash of the E.164 number, to find a customer's walk-ins later without decrypting.</summary>
    public string PhoneLookupHash { get; private set; }

    /// <summary>For the admin booking page: <c>+966 5•• ••• •30</c>.</summary>
    public string PhoneMasked { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}

public static class BookingErrors
{
    public static Error NotFound() => Error.NotFound("booking.not_found", "The booking was not found.");

    public static Error InvalidTransition(BookingStatus from, BookingStatus to) =>
        Error.Conflict("booking.invalid_transition", $"A {from} booking cannot become {to}.").WithDetail("status", from.ToString());

    public static Error TooEarly(BookingStatus to) => Error.BusinessRule("booking.too_early", $"It is too early to mark the booking {to}.");

    public static Error ArrivalRequired() => Error.BusinessRule("booking.arrival_required", "Mark the customer arrived before finishing the visit.");

    public static Error AddAfterArrival() => Error.BusinessRule("booking.add_after_arrival", "A service is added once the customer has arrived.");

    public static Error ServiceAlreadyBooked() => Error.Conflict("booking.service_already_booked", "The service is already on the booking.");

    public static Error ServiceNotBooked() => Error.NotFound("booking.service_not_booked", "The service is not on the booking.");

    public static Error LastService() => Error.BusinessRule("booking.last_service", "A booking keeps at least one service.");

    public static Error PackageServiceFixed() =>
        Error.BusinessRule("booking.service_fixed", "This service stays with the booking (a package's own service, or booked before services could be removed).");

    public static Error ServicesClosed(BookingStatus status) =>
        Error.Conflict("booking.services_closed", $"A {status} booking's services cannot change.").WithDetail("status", status.ToString());

    public static Error ReasonCodeNotAllowed() =>
        Error.Validation("validation.failed", "A ready reason is only for the shop's cancellation.",
            new Dictionary<string, string[]>(StringComparer.Ordinal) { ["reasonCode"] = ["validation.invalid"] });

    public static Error ReasonRequired() =>
        Error.Validation("validation.failed", "A reason is required.", new Dictionary<string, string[]>(StringComparer.Ordinal) { ["reason"] = ["validation.reason_required"] });

    public static Error ProfessionalBusy() =>
        Error.Conflict("booking.professional_busy", "The barber is not free at this time.");

    public static Error SameProfessional() =>
        Error.Validation("booking.same_professional", "The booking is already with this barber.");

    public static Error CutoffPassed(int cutoffMinutes) =>
        Error.BusinessRule("booking.cancellation_cutoff_passed", "It is too late to change this booking online.").WithDetail("cutoffMinutes", cutoffMinutes);

    public static Error NotReschedulable(BookingStatus status) =>
        Error.Conflict("booking.not_reschedulable", $"A {status} booking cannot be rescheduled.").WithDetail("status", status.ToString());

    public static Error SlotUnavailable() => Error.Conflict("booking.slot_unavailable", "The time is no longer available.");

    public static Error ShopNotAccepting(string? reason) =>
        Error.BusinessRule("booking.shop_not_accepting", "The shop is not taking online bookings right now.").WithDetail("reason", reason ?? "shop.not_active");

    public static Error OfferNotFound() => Error.NotFound("booking.offer_not_found", "The service or package is not offered.");

    public static Error OfferNotOnlineBookable() => Error.BusinessRule("booking.offer_not_online_bookable", "The service or package cannot be booked online.");

    public static Error ProfessionalNotEligible() => Error.BusinessRule("booking.professional_not_eligible", "The professional does not provide this service.");

    public static Error ProfileIncomplete() => Error.BusinessRule("booking.profile_incomplete", "Add your name to your profile before booking.");

    public static Error StartInPast() =>
        Error.Validation("validation.failed", "The start is in the past.", new Dictionary<string, string[]>(StringComparer.Ordinal) { ["startsAt"] = ["validation.date_in_past"] });

    public static Error IdempotencyKeyRequired() => Error.Validation("idempotency.key_required", "An Idempotency-Key header is required.");

    public static Error IdempotencyKeyReused() => Error.BusinessRule("idempotency.key_reused", "This Idempotency-Key was used with a different request.");
}
