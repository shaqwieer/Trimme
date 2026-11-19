using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Scheduling;
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
/// D-147 adds reviewed exceptions (listed in <c>BarberScopeTests</c>): any barber of the shop may close a colleague's red
/// booking (<see cref="Reachable"/>, <see cref="AllowedFor"/>; D-151 adds «وصل» there, the client's «الأزرار العادية (وصل،
/// ما حضر)، وأي حلاق في المحل يقدر يضغطها»); and a barber may record a walk-in or phone booking for a free colleague (the
/// walk-in handler). D-147's third, accepting or rejecting a colleague's pending booking, went with the Pending status
/// (D-155: every online booking is confirmed at once).
/// </para>
/// <para>
/// D-151 adds a fourth, read-only one: a barber may list a colleague's or the whole shop's bookings
/// (<see cref="ListedFor"/>, «جدولي / الكل / اسم الحلاق») and open any of the shop's bookings. Reading never widens
/// what they may do (<see cref="AllowedFor"/>), and never shows a colleague's customer's mobile
/// (<c>ShopBookingReader.PhonesForBarberAsync</c> reads the barber's own bookings only).
/// </para>
/// <para>
/// D-155 adds a fifth (the client's 1-2, «حجوزات حلاق غير متوفر»): today's open bookings of a barber marked «غير متوفر
/// اليوم» (<see cref="Cover"/>) may be given to a colleague, given a new time or cancelled with a reason by any barber of
/// the shop (<see cref="OwnOrCovered"/>, <see cref="Reachable"/>, <see cref="AllowedFor"/>), and nothing else: no status
/// but the cancellation, no notes, no services, no mobile.
/// </para>
/// </summary>
internal static class BarberScope
{
    public static ProfessionalId? Own(this ICurrentUser user) => user.ProfessionalId is { } id ? new ProfessionalId(id) : null;

    /// <summary>
    /// A barber's own bookings plus today's open bookings of the barbers marked «غير متوفر اليوم» (D-155's exception 5), for
    /// the three emergency changes: another barber, a new time, a cancellation. The owner keeps the whole shop.
    /// </summary>
    public static IQueryable<Booking> OwnOrCovered(this IQueryable<Booking> bookings, ICurrentUser user, Cover cover)
    {
        if (user.Own() is not { } own)
        {
            return bookings;
        }

        if (cover.IsEmpty)
        {
            return bookings.Where(b => b.ProfessionalId == own);
        }

        var away = cover.Barbers.ToArray();
        var open = BookingRules.Active.ToArray();
        var (from, to) = (cover.DayStart, cover.DayEnd);
        return bookings.Where(b => b.ProfessionalId == own
                                   || (away.Contains(b.ProfessionalId) && open.Contains(b.Status) && b.StartsAt >= from && b.StartsAt < to));
    }

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
    /// A barber's own bookings plus the shop's bookings any barber may act on (D-147): red ones (open past their booked end,
    /// D-151) started within <see cref="BookingRules.OverdueVisibleFor"/>. The owner keeps the whole shop.
    /// </summary>
    public static IQueryable<Booking> Reachable(this IQueryable<Booking> bookings, ICurrentUser user, DateTimeOffset now, Cover? cover = null)
    {
        if (user.Own() is not { } own)
        {
            return bookings;
        }

        var visibleFrom = now - BookingRules.OverdueVisibleFor;
        var open = BookingRules.Active.ToArray();
        if (cover is not { IsEmpty: false })
        {
            return bookings.Where(b => b.ProfessionalId == own || (b.StartsAt >= visibleFrom && open.Contains(b.Status) && b.EndsAt < now));
        }

        // D-155: plus today's open bookings of a barber marked «غير متوفر اليوم».
        var away = cover.Barbers.ToArray();
        var (from, to) = (cover.DayStart, cover.DayEnd);
        return bookings.Where(b => b.ProfessionalId == own
                                   || (b.StartsAt >= visibleFrom && open.Contains(b.Status) && b.EndsAt < now)
                                   || (away.Contains(b.ProfessionalId) && open.Contains(b.Status) && b.StartsAt >= from && b.StartsAt < to));
    }

    /// <summary>The shop's red bookings any barber may act on (D-147), whoever's they are.</summary>
    public static IQueryable<Booking> NeedingAttention(this IQueryable<Booking> bookings, DateTimeOffset now)
    {
        var visibleFrom = now - BookingRules.OverdueVisibleFor;
        var open = BookingRules.Active.ToArray();
        return bookings.Where(b => b.StartsAt >= visibleFrom && open.Contains(b.Status) && b.EndsAt < now);
    }

    /// <summary>
    /// What this user may do to the booking now: everything the rules allow on their own (or, for the owner's view, on any)
    /// booking; on a colleague's, once red, mark the arrival («وصل», D-151), close it or cancel it (D-147). Any other
    /// colleague's booking is read-only.
    /// </summary>
    public static IReadOnlyList<BookingStatus> AllowedFor(this Booking booking, ICurrentUser user, DateTimeOffset now, Cover? cover = null)
    {
        var allowed = booking.AllowedShopTransitions(now);
        if (user.Own() is not { } own || booking.ProfessionalId == own)
        {
            return allowed;
        }

        BookingStatus[] colleague = booking.IsOverdue(now)
            ? [BookingStatus.Arrived, BookingStatus.Completed, BookingStatus.NoShow, BookingStatus.CancelledByShop]
            : cover?.Covers(booking) == true
                ? [BookingStatus.CancelledByShop]
                : [];
        return [.. allowed.Where(colleague.Contains)];
    }

    /// <summary>The professional a request may ask for: a barber always gets their own, whatever they asked.</summary>
    public static Guid? Narrow(this ICurrentUser user, Guid? asked) => user.ProfessionalId ?? asked;
}

/// <summary>
/// The barbers marked «غير متوفر اليوم» in the shop and the shop's day (D-155): their bookings that start that day and are
/// still open are the ones any barber may cover.
/// </summary>
internal sealed record Cover(IReadOnlySet<ProfessionalId> Barbers, DateTimeOffset DayStart, DateTimeOffset DayEnd)
{
    public static readonly Cover None = new(new HashSet<ProfessionalId>(), DateTimeOffset.MinValue, DateTimeOffset.MinValue);

    public bool IsEmpty => Barbers.Count == 0;

    public bool Covers(Booking booking) =>
        Barbers.Contains(booking.ProfessionalId) && booking.IsActive && booking.StartsAt >= DayStart && booking.StartsAt < DayEnd;
}

/// <summary>Reads the shop's <see cref="Cover"/> for the signed-in shop user (D-155).</summary>
internal sealed class CoverReader(IUnavailableBarbers unavailable)
{
    public async Task<Cover> TodayAsync(ShopId shopId, CancellationToken cancellationToken)
    {
        var marks = await unavailable.TodayAsync(shopId, cancellationToken);
        return marks.Count == 0
            ? Cover.None
            : new Cover(marks.Select(m => m.ProfessionalId).ToHashSet(), marks[0].DayStart, marks[0].DayEnd);
    }
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
