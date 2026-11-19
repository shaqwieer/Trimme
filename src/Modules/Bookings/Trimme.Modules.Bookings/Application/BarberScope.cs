using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.Modules.Bookings.Domain;

namespace Trimme.Modules.Bookings.Application;

/// <summary>
/// A barber's login changes only its own professional's bookings (D-145). Every shop handler that changes a booking
/// narrows its reads with <see cref="OwnOnly"/> (another barber's booking id answers 404) and fixes the professional with
/// <see cref="Own"/>. The owner (no professional) keeps the whole shop. This is never a query filter: the availability
/// checks must still see every colleague's bookings.
/// <para>
/// D-147 adds three reviewed exceptions (listed in <c>BarberScopeTests</c>): any barber of the shop may open, accept or
/// reject a colleague's pending booking, and close a colleague's red booking (<see cref="Reachable"/>, <see cref="AllowedFor"/>;
/// D-151 adds «وصل» there, the client's «الأزرار العادية (وصل، ما حضر)، وأي حلاق في المحل يقدر يضغطها»); and a barber may
/// record a walk-in or phone booking for a free colleague (the walk-in handler).
/// </para>
/// <para>
/// D-151 adds a fourth, read-only one: a barber may list a colleague's or the whole shop's bookings
/// (<see cref="ListedFor"/>, «جدولي / الكل / اسم الحلاق») and open any of the shop's bookings. Reading never widens
/// what they may do (<see cref="AllowedFor"/>), and never shows a colleague's customer's mobile
/// (<c>ShopBookingReader.PhonesForBarberAsync</c> reads the barber's own bookings only).
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
    /// The shop's list for this user (D-151): the owner reads the whole shop, as before. A barber reads their own bookings
    /// unless they ask for the whole shop (<paramref name="all"/>) or name a colleague (the list's professional filter, which
    /// then applies as for the owner). Read-only: the rows carry what the barber may do on each (<see cref="AllowedFor"/>).
    /// </summary>
    public static IQueryable<Booking> ListedFor(this IQueryable<Booking> bookings, ICurrentUser user, bool all, Guid? professionalId) =>
        all || professionalId is not null ? bookings : bookings.OwnOnly(user);

    /// <summary>
    /// A barber's own bookings plus the shop's bookings any barber may act on (D-147): pending ones, and red ones (open past
    /// their booked end, D-151), both started within <see cref="BookingRules.OverdueVisibleFor"/>. The owner keeps the whole shop.
    /// </summary>
    public static IQueryable<Booking> Reachable(this IQueryable<Booking> bookings, ICurrentUser user, DateTimeOffset now)
    {
        if (user.Own() is not { } own)
        {
            return bookings;
        }

        var visibleFrom = now - BookingRules.OverdueVisibleFor;
        var open = BookingRules.Active.ToArray();
        return bookings.Where(b => b.ProfessionalId == own
            || (b.StartsAt >= visibleFrom && (b.Status == BookingStatus.Pending || (open.Contains(b.Status) && b.EndsAt < now))));
    }

    /// <summary>The shop's pending and red bookings any barber may act on (D-147), whoever's they are.</summary>
    public static IQueryable<Booking> NeedingAttention(this IQueryable<Booking> bookings, DateTimeOffset now)
    {
        var visibleFrom = now - BookingRules.OverdueVisibleFor;
        var open = BookingRules.Active.ToArray();
        return bookings.Where(b => b.StartsAt >= visibleFrom && (b.Status == BookingStatus.Pending || (open.Contains(b.Status) && b.EndsAt < now)));
    }

    /// <summary>
    /// What this user may do to the booking now: everything the rules allow on their own (or, for the owner's view, on any)
    /// booking; on a colleague's, accept or reject it while pending, and once red mark the arrival («وصل», D-151), close it
    /// or reject it (D-147). Any other colleague's booking is read-only.
    /// </summary>
    public static IReadOnlyList<BookingStatus> AllowedFor(this Booking booking, ICurrentUser user, DateTimeOffset now)
    {
        var allowed = booking.AllowedShopTransitions(now);
        if (user.Own() is not { } own || booking.ProfessionalId == own)
        {
            return allowed;
        }

        BookingStatus[] colleague = booking.IsOverdue(now)
            ? [BookingStatus.Arrived, BookingStatus.Completed, BookingStatus.NoShow, BookingStatus.CancelledByShop]
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
