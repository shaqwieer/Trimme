using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Bookings.Domain;

namespace Trimme.Modules.Bookings.Application;

/// <summary>
/// Saves a new booking with a reference no other booking has (D-148: «GH-» and 5 characters). A request sees only its own
/// shop's bookings, so the check is the unique index itself, which sees every shop: a repeat is drawn again and saved
/// again. Inside the caller's transaction EF rolls back to the savepoint it took before the failed save, so the retry
/// starts clean and the work saved earlier in the transaction (the idempotency claim) stays.
/// </summary>
internal static class BookingReferences
{
    /// <summary>A repeat is about one in 28 million per existing booking: five draws are plenty.</summary>
    private const int Attempts = 5;

    public static async Task SaveNewAsync(TrimmeDbContext db, Booking booking, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException exception) when (attempt < Attempts && DatabaseErrors.IsUniqueViolationOn(exception, "reference"))
            {
                booking.RenewReference();
            }
        }
    }
}
