using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.Modules.Bookings.Domain;

public enum PostponementStatus
{
    /// <summary>Waiting for the customer; the new time is held for them until <see cref="PostponementOffer.ExpiresAt"/>.</summary>
    Open,
    Accepted,
    Declined,

    /// <summary>Replaced by a newer offer for the same booking.</summary>
    Withdrawn,
}

/// <summary>
/// An emergency postponement (D-147, «يؤجّل بموافقة الزبون، والعرض يظل محجوز لين قبل الموعد بساعة»): the barber offers a
/// customer's online booking a new time with them. The time is held for the customer — no one else can book it — until
/// one hour before the original appointment; the customer accepts it (the booking moves) or declines it (the booking
/// stays). An offer only holds while its booking is still Pending or Confirmed at the original time with the same barber:
/// once the booking is cancelled, moved or given another time, the offer is dead without being touched. The hold is
/// enforced by the database (triggers under a per-barber lock, migration <c>BarberOperations</c>), not only here.
/// </summary>
public sealed class PostponementOffer : ICustomerOwned
{
    /// <summary>The customer may answer until this long before the original start (the document's «قبل الموعد بساعة»).</summary>
    public static readonly TimeSpan AnswerBefore = TimeSpan.FromHours(1);

    /// <summary>
    /// An offer of <paramref name="startsAt"/> for the booking. <paramref name="bufferMinutes"/> is the shop's cleaning time
    /// now, the one the offered time was checked with (D-149); without it, the booking's own.
    /// </summary>
    public PostponementOffer(Guid id, Booking booking, DateTimeOffset startsAt, BookingActor actor, DateTimeOffset now, int? bufferMinutes = null)
    {
        Id = id;
        BookingId = booking.Id;
        ShopId = booking.ShopId;
        CustomerId = booking.CustomerId;
        ProfessionalId = booking.ProfessionalId;
        OriginalStartsAt = booking.StartsAt;
        StartsAt = startsAt.ToUniversalTime();
        EndsAt = StartsAt.AddMinutes(booking.DurationMinutes);
        BlockedUntil = EndsAt.AddMinutes(Math.Max(bufferMinutes ?? booking.BufferMinutes, 0));
        ExpiresAt = booking.StartsAt - AnswerBefore;
        Status = PostponementStatus.Open;
        OfferedBy = actor.Id;
        OfferedByName = actor.Name;
        CreatedAt = now.ToUniversalTime();
    }

    private PostponementOffer()
    {
    }

    public Guid Id { get; private set; }

    public BookingId BookingId { get; private set; }

    public ShopId ShopId { get; private set; }

    public Guid? CustomerId { get; private set; }

    public ProfessionalId ProfessionalId { get; private set; }

    /// <summary>The booking's start when the offer was made; the offer holds only while the booking still starts then.</summary>
    public DateTimeOffset OriginalStartsAt { get; private set; }

    public DateTimeOffset StartsAt { get; private set; }

    public DateTimeOffset EndsAt { get; private set; }

    /// <summary>The end plus the shop's cleaning time (D-149): what the offer holds of the barber's time.</summary>
    public DateTimeOffset BlockedUntil { get; private set; }

    /// <summary>The cleaning time the offer holds after its end; the booking takes it when the customer accepts.</summary>
    public int BufferMinutes => (int)Math.Round((BlockedUntil - EndsAt).TotalMinutes);

    public DateTimeOffset ExpiresAt { get; private set; }

    public PostponementStatus Status { get; private set; }

    public Guid? OfferedBy { get; private set; }

    public string? OfferedByName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? AnsweredAt { get; private set; }

    /// <summary>
    /// The offer still holds its time and can be answered: open, not past its deadline, and its booking still waiting at
    /// the original time with the same barber.
    /// </summary>
    public bool IsLive(Booking booking, DateTimeOffset now) =>
        Status == PostponementStatus.Open && now < ExpiresAt && booking.Id == BookingId
        && booking.Status is BookingStatus.Pending or BookingStatus.Confirmed
        && booking.StartsAt == OriginalStartsAt && booking.ProfessionalId == ProfessionalId;

    internal void Close(PostponementStatus status, DateTimeOffset now)
    {
        Status = status;
        AnsweredAt = now.ToUniversalTime();
    }

    public void Withdraw(DateTimeOffset now)
    {
        if (Status == PostponementStatus.Open)
        {
            Close(PostponementStatus.Withdrawn, now);
        }
    }
}

public static class PostponementErrors
{
    public static Error OnlineOnly() =>
        Error.BusinessRule("booking.postpone_online_only", "A walk-in or phone booking gets its new time directly, once the customer agrees.");

    public static Error ConsentRequired() =>
        Error.BusinessRule("booking.consent_required", "A customer's online booking gets a new time only with their consent: offer it instead.");

    public static Error TooLate() =>
        Error.BusinessRule("booking.postpone_too_late", "It is too close to the appointment to wait for the customer: move it to a colleague or cancel it.");

    public static Error Gone() => Error.Conflict("booking.postponement_gone", "The offer is no longer open.");

    public static Error SameTime() => Error.Validation("booking.postpone_same_time", "The new time is the booking's time.");
}
