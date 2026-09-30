using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Scheduling;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Bookings.Domain;

namespace Trimme.Modules.Bookings.Application;

public sealed record BookedPackageItemResponse(Guid ServiceId, string NameAr, string? NameEn);

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

public sealed record BookingHistoryResponse(
    BookingEventKind Kind,
    BookingStatus? FromStatus,
    BookingStatus ToStatus,
    DateTimeOffset? PreviousStartsAt,
    ActorType ActorType,
    string? Reason,
    DateTimeOffset OccurredAt);

public enum CustomerBookingAction
{
    Cancel,
    Reschedule,

    /// <summary>Completed, not yet reviewed, and within <c>ReviewWindowDays</c> of completion (D-017, D-097).</summary>
    Review,
}

/// <summary>What a customer response needs besides the booking: the clock, the policy and the customer's own ratings.</summary>
internal sealed record CustomerBookingView(DateTimeOffset Now, int CutoffMinutes, int ReviewWindowDays, IReadOnlyDictionary<Guid, int> Ratings);

/// <summary>
/// A booking as its customer sees it, with what they may still do: cancel or reschedule until the cutoff (D-015), review a
/// completed visit until <c>ReviewDeadline</c>. <c>ReviewRating</c> is the stars they gave, once reviewed.
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
    uint Version);

/// <summary>
/// A booking as the shop sees it: the customer's name, never a phone number (R-NEG-04). <c>AllowedTransitions</c> is
/// what the UI may offer (DV-S08); <c>ViaQr</c> marks an online booking credited to a QR scan (the design's «رمز QR» source);
/// <c>OutsideSchedule</c> flags an active booking that no longer fits the professional's
/// hours, breaks, time off or closures (DV-S22) — nothing is cancelled automatically.
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
    uint Version);

public sealed record BookingNoteResponse(Guid Id, string Text, DateTimeOffset CreatedAt);

/// <summary>How many bookings each status chip would list (every filter but the status applied).</summary>
public sealed record ShopBookingCounts(int All, int Pending, int Confirmed, int Arrived, int Completed, int Cancelled, int NoShow);

/// <summary>A page of the shop's bookings, with the status chips' counts.</summary>
public sealed record ShopBookingListResponse(IReadOnlyList<ShopBookingResponse> Items, int Page, int PageSize, int Total, ShopBookingCounts Counts);

public sealed record ShopBookingDetailResponse(ShopBookingResponse Booking, IReadOnlyList<BookingHistoryResponse> History, IReadOnlyList<BookingNoteResponse> Notes);

/// <summary>A booking in the admin views (the shop's view plus the shop and the customer's id; no contact data).</summary>
public sealed record AdminBookingResponse(BookingShopResponse Shop, Guid? CustomerId, ShopBookingResponse Booking, IReadOnlyList<BookingHistoryResponse> History);

/// <summary>An outbox payload (R-BKG-08): ids, times and statuses only — no names, no phone numbers.</summary>
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
    string ActorType);

internal static class BookingEvents
{
    public const string Created = "booking.created";
    public const string Rescheduled = "booking.rescheduled";
    public const string Cancelled = "booking.cancelled";
    public const string StatusChanged = "booking.status_changed";

    public static void Add(TrimmeDbContext db, string type, Booking booking, DateTimeOffset now)
    {
        var last = booking.History[^1];
        db.Add(OutboxMessage.Create(type, new BookingEvent(
            booking.Id.Value, booking.ShopId.Value, booking.ProfessionalId.Value, booking.CustomerId, booking.StartsAt, booking.EndsAt,
            booking.Status.ToString(), booking.Channel.ToString(), last.PreviousStartsAt, last.ActorType.ToString()), now));
    }

    public static string ForStatus(BookingStatus status) => BookingRules.IsCancelled(status) ? Cancelled : StatusChanged;
}

internal static class BookingMapping
{
    public static BookingItemResponse Item(Booking b) =>
        new(b.ServiceId, b.PackageId, b.ItemNameAr, b.ItemNameEn, b.Price, b.Currency, b.DurationMinutes,
            [.. b.PackageItems.Select(i => new BookedPackageItemResponse(i.ServiceId, i.NameAr, i.NameEn))]);

    public static BookingProfessionalResponse Professional(Booking b) => new(b.ProfessionalId.Value, b.ProfessionalNameAr, b.ProfessionalNameEn);

    public static BookingShopResponse Shop(ShopSummary? shop, ShopId id) =>
        shop is null ? new(id.Value, string.Empty, string.Empty, string.Empty) : new(shop.Id.Value, shop.Slug, shop.NameAr, shop.NameEn);

    public static IReadOnlyList<BookingHistoryResponse> History(Booking b) =>
    [
        .. b.History.OrderBy(h => h.OccurredAt).ThenBy(h => h.Id)
            .Select(h => new BookingHistoryResponse(h.Kind, h.FromStatus, h.ToStatus, h.PreviousStartsAt, h.ActorType, h.Reason, h.OccurredAt)),
    ];

    public static ShopBookingResponse ToShop(Booking b, DateTimeOffset now, bool outsideSchedule) =>
        new(b.Id.Value, b.Reference, b.CustomerName, b.Channel, b.QrLinkId is not null, Professional(b), Item(b), b.StartsAt, b.EndsAt, b.Status, b.CustomerNote,
            b.CancellationReason, b.AllowedShopTransitions(now), outsideSchedule && b.IsActive, b.Version);

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
            b.CancellationReason, b.PaymentStatus, b.AmountDue, actions, view.CutoffMinutes, rating, rating is null ? deadline : null, b.Version);
    }

    public static BookedItem Snapshot(BookableOffer offer) =>
        new(offer.IsPackage ? null : offer.Id, offer.IsPackage ? offer.Id : null, offer.NameAr, offer.NameEn, offer.Price, offer.Currency,
            offer.DurationMinutes, [.. offer.Items.Select(i => BookedPackageItem.Of(i.ServiceId, i.NameAr, i.NameEn))]);
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
