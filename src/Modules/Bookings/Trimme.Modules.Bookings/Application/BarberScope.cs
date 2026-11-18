using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.Modules.Bookings.Domain;

namespace Trimme.Modules.Bookings.Application;

/// <summary>
/// A barber's login sees and changes only its own professional's bookings (D-145). Every shop handler a barber can reach
/// narrows its booking reads with <see cref="OwnOnly"/> (another barber's booking id answers 404) and fixes the
/// professional with <see cref="Own"/>. The owner (no professional) keeps the whole shop. This is never a query filter:
/// the availability checks must still see every colleague's bookings.
/// <para>
/// D-147 adds three reviewed exceptions (listed in <c>BarberScopeTests</c>): any barber of the shop may open, accept or
/// reject a colleague's pending booking, and close a colleague's red booking (<see cref="Reachable"/>, <see cref="AllowedFor"/>);
/// and a barber may record a walk-in or phone booking for a free colleague (the walk-in handler).
/// </para>
/// </summary>
internal static class BarberScope
{
    public static ProfessionalId? Own(this ICurrentUser user) => user.ProfessionalId is { } id ? new ProfessionalId(id) : null;

    public static IQueryable<Booking> OwnOnly(this IQueryable<Booking> bookings, ICurrentUser user)
    {
        if (user.Own() is not { } own)
        {
            return bookings;
        }

        return bookings.Where(b => b.ProfessionalId == own);
    }

    /// <summary>
    /// A barber's own bookings plus the shop's bookings any barber may act on (D-147): pending ones, and red ones (open
    /// <see cref="BookingRules.OverdueAfter"/> past their end), both started within <see cref="BookingRules.OverdueVisibleFor"/>.
    /// The owner keeps the whole shop.
    /// </summary>
    public static IQueryable<Booking> Reachable(this IQueryable<Booking> bookings, ICurrentUser user, DateTimeOffset now)
    {
        if (user.Own() is not { } own)
        {
            return bookings;
        }

        var visibleFrom = now - BookingRules.OverdueVisibleFor;
        var overdueEnd = now - BookingRules.OverdueAfter;
        var open = BookingRules.Active.ToArray();
        return bookings.Where(b => b.ProfessionalId == own
            || (b.StartsAt >= visibleFrom && (b.Status == BookingStatus.Pending || (open.Contains(b.Status) && b.EndsAt < overdueEnd))));
    }

    /// <summary>The shop's pending and red bookings any barber may act on (D-147), whoever's they are.</summary>
    public static IQueryable<Booking> NeedingAttention(this IQueryable<Booking> bookings, DateTimeOffset now)
    {
        var visibleFrom = now - BookingRules.OverdueVisibleFor;
        var overdueEnd = now - BookingRules.OverdueAfter;
        var open = BookingRules.Active.ToArray();
        return bookings.Where(b => b.StartsAt >= visibleFrom && (b.Status == BookingStatus.Pending || (open.Contains(b.Status) && b.EndsAt < overdueEnd)));
    }

    /// <summary>
    /// What this user may do to the booking now: everything the rules allow on their own (or, for the owner's view, on any)
    /// booking; on a colleague's, accept or reject it while pending, and close it once red (D-147).
    /// </summary>
    public static IReadOnlyList<BookingStatus> AllowedFor(this Booking booking, ICurrentUser user, DateTimeOffset now)
    {
        var allowed = booking.AllowedShopTransitions(now);
        if (user.Own() is not { } own || booking.ProfessionalId == own)
        {
            return allowed;
        }

        BookingStatus[] colleague = booking.IsOverdue(now)
            ? [BookingStatus.Completed, BookingStatus.NoShow, BookingStatus.CancelledByShop]
            : booking.Status == BookingStatus.Pending ? [BookingStatus.Confirmed, BookingStatus.CancelledByShop] : [];
        return [.. allowed.Where(colleague.Contains)];
    }

    /// <summary>The professional a request may ask for: a barber always gets their own, whatever they asked.</summary>
    public static Guid? Narrow(this ICurrentUser user, Guid? asked) => user.ProfessionalId ?? asked;
}

/// <summary>
/// The current shop user or admin as a booking actor, with their name at the time (D-147: «كل حركة تنسجل باسم اللي
/// سواها»): a barber by their professional's name, anyone else by their account's display name.
/// </summary>
internal sealed class BookingActors(ICurrentUser user, IUserNameLookup names, IProfessionalDirectory professionals)
{
    public async Task<BookingActor> CurrentAsync(ActorType type, CancellationToken cancellationToken)
    {
        string? name = null;
        if (user.Own() is { } own && await professionals.FindAsync(own, cancellationToken) is { } professional)
        {
            name = professional.NameAr;
        }
        else if (user.UserId is { } id && (await names.FindAsync([id], cancellationToken)).TryGetValue(id, out var entry))
        {
            name = entry.DisplayName;
        }

        return new BookingActor(user.UserId, type, name);
    }
}
