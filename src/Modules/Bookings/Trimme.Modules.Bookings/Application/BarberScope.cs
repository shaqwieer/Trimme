using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.Modules.Bookings.Domain;

namespace Trimme.Modules.Bookings.Application;

/// <summary>
/// A barber's login sees and changes only its own professional's bookings (D-145). Every shop handler a barber can reach
/// narrows its booking reads with <see cref="OwnOnly"/> (another barber's booking id answers 404) and fixes the
/// professional with <see cref="Own"/>. The owner (no professional) keeps the whole shop. This is never a query filter:
/// the availability checks must still see every colleague's bookings.
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

    /// <summary>The professional a request may ask for: a barber always gets their own, whatever they asked.</summary>
    public static Guid? Narrow(this ICurrentUser user, Guid? asked) => user.ProfessionalId ?? asked;
}
