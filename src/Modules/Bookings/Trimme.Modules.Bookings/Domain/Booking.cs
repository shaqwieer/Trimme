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
    WalkIn,
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
}

/// <summary>Who did something to a booking. The id is the user's (null for the system).</summary>
public sealed record BookingActor(Guid? Id, ActorType Type)
{
    public static readonly BookingActor System = new(null, ActorType.System);
}

/// <summary>What was booked, as it was at booking time (R-BKG-01): later edits to the service never change it.</summary>
public sealed record BookedItem(Guid? ServiceId, Guid? PackageId, string NameAr, string? NameEn, decimal Price, string Currency, int DurationMinutes, IReadOnlyList<BookedPackageItem> PackageItems);

/// <summary>One service of a booked package (reporting, D-072).</summary>
public sealed class BookedPackageItem
{
    public Guid ServiceId { get; set; }

    public string NameAr { get; set; } = string.Empty;

    public string? NameEn { get; set; }

    public static BookedPackageItem Of(Guid serviceId, string nameAr, string? nameEn) => new() { ServiceId = serviceId, NameAr = nameAr, NameEn = nameEn };
}

/// <summary>The professional as named at booking time (and after a reschedule to another professional).</summary>
public sealed record BookedProfessional(ProfessionalId Id, string NameAr, string NameEn);

/// <summary>One entry of the booking's history (R-BKG-02): every status change and reschedule, with actor and time.</summary>
public sealed class BookingHistoryEntry
{
    internal BookingHistoryEntry(BookingEventKind kind, BookingStatus? from, BookingStatus to, DateTimeOffset? previousStartsAt, BookingActor actor, string? reason, DateTimeOffset at)
    {
        Kind = kind;
        FromStatus = from;
        ToStatus = to;
        PreviousStartsAt = previousStartsAt;
        ActorId = actor.Id;
        ActorType = actor.Type;
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

    public Guid? ActorId { get; private set; }

    public ActorType ActorType { get; private set; }

    public string? Reason { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
}

/// <summary>Booking rules that are not the state machine itself (D-087).</summary>
public static class BookingRules
{
    public const int MaxNameLength = 120;
    public const int MaxNoteLength = 500;
    public const int MaxReasonLength = 300;
    public const int ReferenceLength = 8;

    /// <summary>A booked professional may be marked arrived from this long before the start.</summary>
    public static readonly TimeSpan ArrivalWindow = TimeSpan.FromMinutes(60);

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

    public static string NewReference() =>
        string.Create(ReferenceLength, 0, (chars, _) =>
        {
            for (var i = 0; i < chars.Length; i++)
            {
                chars[i] = ReferenceAlphabet[RandomNumberGenerator.GetInt32(ReferenceAlphabet.Length)];
            }
        });
}

/// <summary>The allowed transitions (D-016). Anything else is refused with 409 <c>booking.invalid_transition</c>.</summary>
public static class BookingStateMachine
{
    private static readonly Dictionary<BookingStatus, BookingStatus[]> Allowed = new()
    {
        [BookingStatus.Pending] = [BookingStatus.Confirmed, BookingStatus.CancelledByCustomer, BookingStatus.CancelledByShop],
        [BookingStatus.Confirmed] = [BookingStatus.Arrived, BookingStatus.NoShow, BookingStatus.CancelledByCustomer, BookingStatus.CancelledByShop],
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
        DateTimeOffset startsAt, BookingStatus status, BookingChannel channel, string? customerNote, DateTimeOffset now)
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
        EndsAt = StartsAt.AddMinutes(item.DurationMinutes);
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

    public BookingStatus Status { get; private set; }

    public BookingChannel Channel { get; private set; }

    /// <summary>The customer's own note to the shop (or the shop's note on a walk-in).</summary>
    public string? CustomerNote { get; private set; }

    public string? CancellationReason { get; private set; }

    /// <summary>Payment seam (R-BKG-10): no online payment in v1.</summary>
    public PaymentStatus PaymentStatus { get; private set; }

    /// <summary>What the customer owes at the shop: the booked price, snapshotted.</summary>
    public decimal AmountDue { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public IReadOnlyList<BookingHistoryEntry> History => _history;

    public static Booking CreateOnline(
        BookingId id, ShopId shopId, Guid customerId, string customerName, BookedProfessional professional, BookedItem item,
        DateTimeOffset startsAt, bool requireManualConfirmation, string? customerNote, DateTimeOffset now)
    {
        var status = requireManualConfirmation ? BookingStatus.Pending : BookingStatus.Confirmed;
        var booking = new Booking(id, shopId, customerId, customerName.Trim(), professional, item, startsAt, status, BookingChannel.Online, Clean(customerNote), now);
        booking._history.Add(new BookingHistoryEntry(BookingEventKind.Created, null, status, null, new BookingActor(customerId, ActorType.Customer), null, now));
        return booking;
    }

    /// <summary>D-035: a walk-in that starts now is Arrived; a later one is Confirmed.</summary>
    public static Booking CreateWalkIn(
        BookingId id, ShopId shopId, string customerName, BookedProfessional professional, BookedItem item,
        DateTimeOffset startsAt, bool startsNow, string? note, BookingActor actor, DateTimeOffset now)
    {
        var status = startsNow ? BookingStatus.Arrived : BookingStatus.Confirmed;
        var booking = new Booking(id, shopId, null, customerName.Trim(), professional, item, startsAt, status, BookingChannel.WalkIn, Clean(note), now);
        booking._history.Add(new BookingHistoryEntry(BookingEventKind.Created, null, status, null, actor, null, now));
        return booking;
    }

    /// <summary>
    /// Restores a booking with a given status and history, for the development seed only (past appointments cannot be
    /// created through the online rules).
    /// </summary>
    public static Booking Seeded(
        BookingId id, ShopId shopId, Guid? customerId, string customerName, BookedProfessional professional, BookedItem item,
        DateTimeOffset startsAt, BookingChannel channel, IReadOnlyList<BookingStatus> path, string? reason, DateTimeOffset createdAt)
    {
        var booking = new Booking(id, shopId, customerId, customerName, professional, item, startsAt, path[0], channel, null, createdAt);
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

    public bool IsActive => BookingRules.IsActive(Status);

    /// <summary>
    /// A shop's transition (D-016, D-087): confirm, mark arrived (from an hour before the start), complete, mark a no-show
    /// (once the start has passed) or cancel (with a reason). Admins cancel through the same rule with their own actor.
    /// </summary>
    public Result ApplyShopTransition(BookingStatus to, BookingActor actor, string? reason, DateTimeOffset now)
    {
        if (!BookingStateMachine.CanTransition(Status, to) || to == BookingStatus.CancelledByCustomer)
        {
            return BookingErrors.InvalidTransition(Status, to);
        }

        switch (to)
        {
            case BookingStatus.Arrived when now < StartsAt - BookingRules.ArrivalWindow:
            case BookingStatus.NoShow when now < StartsAt:
                return BookingErrors.TooEarly(to);
            case BookingStatus.CancelledByShop when string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 3:
                return BookingErrors.ReasonRequired();
        }

        if (BookingRules.TooLong(reason, BookingRules.MaxReasonLength, "reason") is { } tooLong)
        {
            return tooLong;
        }

        Move(to, actor, Clean(reason), now);
        return Result.Success();
    }

    /// <summary>A customer cancels their own booking until the cutoff before the start (D-015).</summary>
    public Result CancelByCustomer(Guid customerId, string? reason, DateTimeOffset now, int cutoffMinutes)
    {
        if (!BookingStateMachine.CanTransition(Status, BookingStatus.CancelledByCustomer))
        {
            return BookingErrors.InvalidTransition(Status, BookingStatus.CancelledByCustomer);
        }

        if (now > StartsAt.AddMinutes(-cutoffMinutes))
        {
            return BookingErrors.CutoffPassed(cutoffMinutes);
        }

        if (BookingRules.TooLong(reason, BookingRules.MaxReasonLength, "reason") is { } tooLong)
        {
            return tooLong;
        }

        Move(BookingStatus.CancelledByCustomer, new BookingActor(customerId, ActorType.Customer), Clean(reason), now);
        return Result.Success();
    }

    /// <summary>
    /// Moves a Pending or Confirmed booking to a new start (and optionally another professional) with the same snapshot
    /// and status (D-087). The caller has rechecked availability; the customer's cutoff applies to the current start.
    /// </summary>
    public Result Reschedule(DateTimeOffset newStart, BookedProfessional professional, BookingActor actor, DateTimeOffset now, int? cutoffMinutes)
    {
        if (Status is not (BookingStatus.Pending or BookingStatus.Confirmed))
        {
            return BookingErrors.NotReschedulable(Status);
        }

        if (cutoffMinutes is { } cutoff && now > StartsAt.AddMinutes(-cutoff))
        {
            return BookingErrors.CutoffPassed(cutoff);
        }

        var previous = StartsAt;
        StartsAt = newStart.ToUniversalTime();
        EndsAt = StartsAt.AddMinutes(DurationMinutes);
        SetProfessional(professional);
        UpdatedAt = now;
        _history.Add(new BookingHistoryEntry(BookingEventKind.Rescheduled, Status, Status, previous, actor, null, now));
        return Result.Success();
    }

    /// <summary>What the given actor may do next (the UI renders only these, DV-S08).</summary>
    public IReadOnlyList<BookingStatus> AllowedShopTransitions(DateTimeOffset now) =>
    [
        .. BookingStateMachine.Next(Status).Where(to => to switch
        {
            BookingStatus.CancelledByCustomer => false,
            BookingStatus.Arrived => now >= StartsAt - BookingRules.ArrivalWindow,
            BookingStatus.NoShow => now >= StartsAt,
            _ => true,
        }),
    ];

    public bool CustomerCanChange(DateTimeOffset now, int cutoffMinutes) =>
        Status is BookingStatus.Pending or BookingStatus.Confirmed && now <= StartsAt.AddMinutes(-cutoffMinutes);

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

public static class BookingErrors
{
    public static Error NotFound() => Error.NotFound("booking.not_found", "The booking was not found.");

    public static Error InvalidTransition(BookingStatus from, BookingStatus to) =>
        Error.Conflict("booking.invalid_transition", $"A {from} booking cannot become {to}.").WithDetail("status", from.ToString());

    public static Error TooEarly(BookingStatus to) => Error.BusinessRule("booking.too_early", $"It is too early to mark the booking {to}.");

    public static Error ReasonRequired() =>
        Error.Validation("validation.failed", "A reason is required.", new Dictionary<string, string[]>(StringComparer.Ordinal) { ["reason"] = ["validation.reason_required"] });

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
