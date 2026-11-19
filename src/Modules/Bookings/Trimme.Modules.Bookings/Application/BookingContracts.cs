using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Loyalty;
using Trimme.BuildingBlocks.Application.Scheduling;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Bookings.Domain;

namespace Trimme.Modules.Bookings.Application;

/// <summary>One booked service, with its own price and length when it has them, and whether the barber added it at the visit (D-147).</summary>
public sealed record BookedPackageItemResponse(Guid ServiceId, string NameAr, string? NameEn, decimal? Price = null, int? DurationMinutes = null, bool Added = false);

/// <summary>What was booked, as it was at booking time (R-BKG-01).</summary>
public sealed record BookingItemResponse(
    Guid? ServiceId,
    Guid? PackageId,
    string NameAr,
    string? NameEn,
    decimal Price,
    string Currency,
    int DurationMinutes,
    IReadOnlyList<BookedPackageItemResponse> PackageItems);

public sealed record BookingProfessionalResponse(Guid Id, string NameAr, string NameEn);

public sealed record BookingShopResponse(Guid Id, string Slug, string NameAr, string NameEn);

/// <summary>The loyalty reward chosen for the booking (D-132): the shop applies it at the visit.</summary>
/// <summary>
/// The loyalty reward used on the booking: a D-132 coupon (no <c>Discount</c>: the shop applies it at the visit) or a points
/// reward (D-149), whose <c>Discount</c> is already taken off the amount due.
/// </summary>
public sealed record BookingLoyaltyRewardResponse(Guid Id, string TitleAr, string? TitleEn, decimal? Discount = null);

public sealed record BookingHistoryResponse(
    BookingEventKind Kind,
    BookingStatus? FromStatus,
    BookingStatus ToStatus,
    DateTimeOffset? PreviousStartsAt,
    ActorType ActorType,
    string? Reason,
    DateTimeOffset OccurredAt,
    BookingProfessionalResponse? PreviousProfessional = null,
    string? ActorName = null);

public enum CustomerBookingAction
{
    Cancel,
    Reschedule,

    /// <summary>Completed, not yet reviewed, and within <c>ReviewWindowDays</c> of completion (D-017, D-097).</summary>
    Review,
}

/// <summary>What a customer response needs besides the booking: the clock, the policy and the customer's own ratings.</summary>
internal sealed record CustomerBookingView(
    DateTimeOffset Now, int CutoffMinutes, int ReviewWindowDays, IReadOnlyDictionary<Guid, int> Ratings,
    IReadOnlyDictionary<Guid, PostponementOfferResponse>? Offers = null);

/// <summary>
/// A booking as its customer sees it, with what they may still do: cancel or reschedule until the cutoff (D-015), review a
/// completed visit until <c>ReviewDeadline</c>. <c>ReviewRating</c> is the stars they gave, once reviewed.
/// <c>ProfessionalChangedAt</c>: the shop gave the booking to another barber since the customer last chose it (D-145);
/// until it starts they may cancel or change it whatever the cutoff.
/// </summary>
public sealed record CustomerBookingResponse(
    Guid Id,
    string Reference,
    BookingShopResponse Shop,
    BookingProfessionalResponse Professional,
    BookingItemResponse Item,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    BookingStatus Status,
    BookingChannel Channel,
    string? Note,
    string? CancellationReason,
    PaymentStatus PaymentStatus,
    decimal AmountDue,
    IReadOnlyList<CustomerBookingAction> AllowedActions,
    int CancellationCutoffMinutes,
    int? ReviewRating,
    DateTimeOffset? ReviewDeadline,
    BookingLoyaltyRewardResponse? LoyaltyReward,
    uint Version,
    DateTimeOffset? ProfessionalChangedAt = null,
    PostponementOfferResponse? Postponement = null,
    ShopCancellationReason? CancellationReasonCode = null);

/// <summary>
/// A booking as the shop sees it: the customer's name, never a phone number (R-NEG-04). <c>AllowedTransitions</c> is
/// what the UI may offer (DV-S08); <c>ViaQr</c> marks an online booking credited to a QR scan (the design's «رمز QR» source);
/// <c>OutsideSchedule</c> flags an active booking that no longer fits the professional's
/// hours, breaks, time off or closures (DV-S22) — nothing is cancelled automatically. <c>Visit</c> says whether an
/// online booking's customer is new or returning, for the owner only; it is null for the desk and for walk-ins (D-135).
/// <para>
/// D-147: <c>ActualStartAt</c> (when «وصل» was pressed, or a walk-in started at once) and <c>ActualEndAt</c> (when
/// «انتهت الخدمة» was pressed); <c>Overdue</c> (red: open past its booked end, D-151); <c>CancelledLate</c> («إلغاء متأخر»);
/// <c>ClosedLate</c> («انقفل متأخر»: closed more than a quarter hour after its end) and <c>ClosedBy</c> (the name of who
/// closed it). D-151: <c>CancellationReasonCode</c>, the ready reason the shop chose, beside the typed one.
/// </para>
/// </summary>
public sealed record ShopBookingResponse(
    Guid Id,
    string Reference,
    string CustomerName,
    BookingChannel Channel,
    bool ViaQr,
    BookingProfessionalResponse Professional,
    BookingItemResponse Item,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    BookingStatus Status,
    string? Note,
    string? CancellationReason,
    IReadOnlyList<BookingStatus> AllowedTransitions,
    bool OutsideSchedule,
    BookingLoyaltyRewardResponse? LoyaltyReward,
    CustomerVisit? Visit,
    uint Version,
    DateTimeOffset? ActualStartAt = null,
    DateTimeOffset? ActualEndAt = null,
    bool Overdue = false,
    bool CancelledLate = false,
    bool ClosedLate = false,
    string? ClosedBy = null,
    string? CustomerPhone = null,
    PostponementOfferResponse? Postponement = null,
    decimal? AmountDue = null,
    ShopCancellationReason? CancellationReasonCode = null)
{
    /// <summary>Never print the number (records list every member by default).</summary>
    public override string ToString() => $"{nameof(ShopBookingResponse)} {{ Id = {Id}, Status = {Status} }}";
}

public sealed record BookingNoteResponse(Guid Id, string Text, DateTimeOffset CreatedAt);

/// <summary>
/// The latest postponement offer of a booking (D-147): the new time held for the customer, until when they may answer,
/// its state and who offered it. <c>Live</c>: still open and holding the time.
/// </summary>
public sealed record PostponementOfferResponse(
    Guid Id, DateTimeOffset StartsAt, DateTimeOffset EndsAt, DateTimeOffset ExpiresAt, PostponementStatus Status, string? OfferedBy, bool Live);

/// <summary>How many bookings each status chip would list (every filter but the status applied).</summary>
public sealed record ShopBookingCounts(int All, int Pending, int Confirmed, int Arrived, int Completed, int Cancelled, int NoShow);

/// <summary>A page of the shop's bookings, with the status chips' counts.</summary>
public sealed record ShopBookingListResponse(IReadOnlyList<ShopBookingResponse> Items, int Page, int PageSize, int Total, ShopBookingCounts Counts);

public sealed record ShopBookingDetailResponse(ShopBookingResponse Booking, IReadOnlyList<BookingHistoryResponse> History, IReadOnlyList<BookingNoteResponse> Notes);

/// <summary>A booking in the admin views (the shop's view plus the shop and the customer's id; no contact data).</summary>
public sealed record AdminBookingResponse(BookingShopResponse Shop, Guid? CustomerId, ShopBookingResponse Booking, IReadOnlyList<BookingHistoryResponse> History);

/// <summary>
/// An outbox payload (R-BKG-08): ids, times and statuses only — no names, no phone numbers. <c>ActorId</c> is the account
/// that made the change (null for the system), so a barber is not told about their own action (D-145).
/// </summary>
internal sealed record BookingEvent(
    Guid BookingId,
    Guid ShopId,
    Guid ProfessionalId,
    Guid? CustomerId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Status,
    string Channel,
    DateTimeOffset? PreviousStartsAt,
    string ActorType,
    Guid? PreviousProfessionalId = null,
    Guid? ActorId = null,
    DateTimeOffset? OfferedStartsAt = null,
    string? ActorName = null);

internal static class BookingEvents
{
    public const string Created = "booking.created";
    public const string Rescheduled = "booking.rescheduled";
    public const string Cancelled = "booking.cancelled";
    public const string StatusChanged = "booking.status_changed";

    /// <summary>The booking was given to another barber at the same time (D-145); the payload names the previous one.</summary>
    public const string ProfessionalChanged = "booking.professional_changed";

    /// <summary>
    /// A service was added at the visit or taken off (D-147): the end and the price moved. Only the live board listens;
    /// nobody is messaged.
    /// </summary>
    public const string ServicesChanged = "booking.services_changed";

    /// <summary>The barber offered the customer a new time (D-147); the payload carries it. The customer gets an in-app notice.</summary>
    public const string PostponementOffered = "booking.postponement_offered";

    /// <summary>The customer declined the offered time (D-147); the barber and the owner get an in-app notice.</summary>
    public const string PostponementDeclined = "booking.postponement_declined";

    public static void Add(TrimmeDbContext db, string type, Booking booking, DateTimeOffset now, DateTimeOffset? offeredStartsAt = null)
    {
        var last = booking.History[^1];
        db.Add(OutboxMessage.Create(type, new BookingEvent(
            booking.Id.Value, booking.ShopId.Value, booking.ProfessionalId.Value, booking.CustomerId, booking.StartsAt, booking.EndsAt,
            booking.Status.ToString(), booking.Channel.ToString(), last.PreviousStartsAt, last.ActorType.ToString(), last.PreviousProfessionalId, last.ActorId,
            offeredStartsAt, last.ActorType == ActorType.Customer ? null : last.ActorName), now));
    }

    public static PostponementOfferResponse Offer(PostponementOffer offer, Booking booking, DateTimeOffset now) =>
        new(offer.Id, offer.StartsAt, offer.EndsAt, offer.ExpiresAt, offer.Status, offer.OfferedByName, offer.IsLive(booking, now));

    public static string ForStatus(BookingStatus status) => BookingRules.IsCancelled(status) ? Cancelled : StatusChanged;
}

internal static class BookingMapping
{
    public static BookingItemResponse Item(Booking b) =>
        new(b.ServiceId, b.PackageId, b.ItemNameAr, b.ItemNameEn, b.Price, b.Currency, b.DurationMinutes,
            [.. b.PackageItems.Select(i => new BookedPackageItemResponse(i.ServiceId, i.NameAr, i.NameEn, i.Price, i.DurationMinutes, i.Added))]);

    public static BookingProfessionalResponse Professional(Booking b) => new(b.ProfessionalId.Value, b.ProfessionalNameAr, b.ProfessionalNameEn);

    public static BookingShopResponse Shop(ShopSummary? shop, ShopId id) =>
        shop is null ? new(id.Value, string.Empty, string.Empty, string.Empty) : new(shop.Id.Value, shop.Slug, shop.NameAr, shop.NameEn);

    public static IReadOnlyList<BookingHistoryResponse> History(Booking b) =>
    [
        .. b.History.OrderBy(h => h.OccurredAt).ThenBy(h => h.Id)
            .Select(h => new BookingHistoryResponse(
                h.Kind, h.FromStatus, h.ToStatus, h.PreviousStartsAt, h.ActorType, h.Reason, h.OccurredAt,
                h.PreviousProfessionalId is { } previous
                    ? new BookingProfessionalResponse(previous, h.PreviousProfessionalNameAr ?? string.Empty, h.PreviousProfessionalNameEn ?? string.Empty)
                    : null,
                h.ActorName)),
    ];

    /// <summary>The shop's view; <paramref name="allowed"/> is what the viewer may do (a colleague's booking allows less, D-147).</summary>
    public static ShopBookingResponse ToShop(
        Booking b, DateTimeOffset now, bool outsideSchedule, CustomerVisit? visit = null, IReadOnlyList<BookingStatus>? allowed = null) =>
        new(b.Id.Value, b.Reference, b.CustomerName, b.Channel, b.QrLinkId is not null, Professional(b), Item(b), b.StartsAt, b.EndsAt, b.Status, b.CustomerNote,
            b.CancellationReason, allowed ?? b.AllowedShopTransitions(now), outsideSchedule && b.IsActive, LoyaltyReward(b), visit, b.Version,
            b.ActualStartAt, b.CompletedAt, b.IsOverdue(now), b.CancelledLate, b.ClosedLate, b.ClosingEntry?.ActorName, AmountDue: b.AmountDue,
            CancellationReasonCode: b.CancellationReasonCode);

    public static CustomerBookingResponse ToCustomer(Booking b, ShopSummary? shop, CustomerBookingView view)
    {
        var rating = view.Ratings.TryGetValue(b.Id.Value, out var stars) ? stars : (int?)null;
        var deadline = b.CompletedAt?.AddDays(view.ReviewWindowDays);
        List<CustomerBookingAction> actions = b.CustomerCanChange(view.Now, view.CutoffMinutes)
            ? [CustomerBookingAction.Cancel, CustomerBookingAction.Reschedule]
            : [];
        if (rating is null && deadline is { } until && view.Now <= until && b.CustomerId is not null)
        {
            actions.Add(CustomerBookingAction.Review);
        }

        return new(b.Id.Value, b.Reference, Shop(shop, b.ShopId), Professional(b), Item(b), b.StartsAt, b.EndsAt, b.Status, b.Channel, b.CustomerNote,
            b.CancellationReason, b.PaymentStatus, b.AmountDue, actions, view.CutoffMinutes, rating, rating is null ? deadline : null, LoyaltyReward(b), b.Version,
            b.ProfessionalChangedAt, view.Offers?.GetValueOrDefault(b.Id.Value), b.CancellationReasonCode);
    }

    public static BookingLoyaltyRewardResponse? LoyaltyReward(Booking b) =>
        b.LoyaltyRewardId is { } id
            ? new(id, b.LoyaltyRewardTitleAr ?? string.Empty, b.LoyaltyRewardTitleEn)
            : b.LoyaltyRedemptionId is { } redemption ? new(redemption, b.LoyaltyRewardTitleAr ?? string.Empty, b.LoyaltyRewardTitleEn, b.LoyaltyDiscount) : null;

    /// <summary>The booked services and the price each counts for, for a free-service reward (D-149).</summary>
    public static IReadOnlyList<BookedServicePrice> ServicePrices(BookableOffer offer) =>
        offer.Items.Count == 0
            ? [new BookedServicePrice(offer.Id, offer.Price)]
            : [.. offer.Items.Where(i => i.Price is not null).Select(i => new BookedServicePrice(i.ServiceId, i.Price!.Value))];

    public static BookedItem Snapshot(BookableOffer offer) =>
        new(offer.IsPackage ? null : offer.Id, offer.IsPackage ? offer.Id : null, offer.NameAr, offer.NameEn, offer.Price, offer.Currency,
            offer.DurationMinutes, [.. offer.Items.Select(i => BookedPackageItem.Of(i.ServiceId, i.NameAr, i.NameEn, i.Price, i.DurationMinutes))]);

    /// <summary>
    /// The offer as <paramref name="professional"/> does it, for the booking snapshot (D-149): their own length for the
    /// whole and for each service that carries its own.
    /// </summary>
    public static BookedItem Snapshot(BookableOffer offer, ProfessionalId professional) => Snapshot(offer.For(professional));

    /// <summary>How long <paramref name="professional"/> takes for the booked offer (D-149), for a move to them.</summary>
    public static BookedLength Length(BookableOffer offer, ProfessionalId professional)
    {
        var own = offer.For(professional);
        return new BookedLength(
            own.DurationMinutes,
            own.Items.Where(i => i.DurationMinutes is not null).GroupBy(i => i.ServiceId).ToDictionary(g => g.Key, g => g.First().DurationMinutes!.Value));
    }
}

/// <summary>
/// A shop's booking rules (D-149): its own start interval, minimum notice, horizon and cleaning time over the platform's.
/// Reads the shop through the caller's scope.
/// </summary>
internal sealed class ShopRulesReader(IShopDirectory shops, BuildingBlocks.Application.Platform.IPlatformSettings settings)
{
    public async Task<EffectiveBookingRules> GetAsync(ShopId shopId, CancellationToken cancellationToken) =>
        EffectiveBookingRules.Of(await settings.GetAsync(cancellationToken), (await shops.FindAsync(shopId, cancellationToken))?.Booking);
}

/// <summary>
/// The idempotency flow of create and reschedule (R-BKG-05, D-089). Inside the command's transaction the key is claimed
/// first and saved on its own, so a concurrent request with the same key waits on the primary key, then fails with a
/// duplicate and replays the stored result. A failed command rolls the claim back.
/// </summary>
internal sealed class IdempotencyGate(TrimmeDbContext db, TimeProvider clock)
{
    /// <summary>The booking a completed earlier request produced, a reuse error, or null to run the command.</summary>
    public async Task<Result<Guid>?> ReplayAsync(Guid userId, string scope, string key, string hash, CancellationToken cancellationToken)
    {
        var record = await db.Set<IdempotencyRecord>().AsNoTracking()
            .SingleOrDefaultAsync(r => r.UserId == userId && r.Scope == scope && r.Key == key && r.ExpiresAt > clock.GetUtcNow(), cancellationToken);
        if (record is null)
        {
            return null;
        }

        return record.RequestHash != hash
            ? BookingErrors.IdempotencyKeyReused()
            : record.ResourceId is { } id ? Result<Guid>.Success(id) : BookingErrors.SlotUnavailable();
    }

    /// <summary>Claims the key; false when a concurrent request with the same key already holds it (replay afterwards).</summary>
    public async Task<(bool Claimed, IdempotencyRecord? Record)> ClaimAsync(Guid userId, string scope, string key, string hash, CancellationToken cancellationToken)
    {
        // An expired record with the same key is replaced.
        await db.Set<IdempotencyRecord>().Where(r => r.UserId == userId && r.Scope == scope && r.Key == key && r.ExpiresAt <= clock.GetUtcNow())
            .ExecuteDeleteAsync(cancellationToken);
        var record = new IdempotencyRecord(userId, scope, key, hash, clock.GetUtcNow());
        db.Add(record);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return (true, record);
        }
        catch (Exception exception) when (DatabaseErrors.IsUniqueViolation(exception))
        {
            db.ChangeTracker.Clear();
            return (false, null);
        }
    }
}
