namespace Trimme.BuildingBlocks.Application.Privacy;

/// <summary>
/// The signed-in customer's own bookings that are still ahead (D-144). A customer with any cannot delete their account:
/// the shop expects them, and reminders would go to an account that no longer has a number.
/// </summary>
public interface ICustomerUpcomingBookings
{
    Task<int> CountAsync(Guid customerId, CancellationToken cancellationToken);
}

/// <summary>
/// A module's personal data about a customer, removed when the customer deletes their account (D-144). Runs in the
/// customer's own request, in the same unit of work as the account's erasure.
/// </summary>
public interface ICustomerDataEraser
{
    Task EraseAsync(Guid customerId, CancellationToken cancellationToken);
}
