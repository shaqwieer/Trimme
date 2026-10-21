using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Bookings.Domain;

namespace Trimme.Modules.Bookings.Application;

/// <summary>Whether an online booking's customer had a completed visit at the shop before it (D-135).</summary>
public enum CustomerVisit
{
    New,
    Returning,
}

/// <summary>
/// The owner's figures for the last <paramref name="Days"/> days: completed visits by customers coming for the first time
/// (each one a new customer) and by customers who had been before.
/// </summary>
public sealed record ShopCustomerMix(int Days, int NewCustomers, int ReturningVisits);

/// <summary>
/// New or returning customers, for the shop's owner only (D-135, <c>Shop.Insights.View</c>; the desk does not get it). A
/// customer is returning at a booking when they had a completed visit at this shop that started before it. Walk-ins have
/// no customer account, so they get no label. Reads go through the shop's tenant filter: only this shop's own bookings,
/// which the shop already sees, are counted.
/// </summary>
internal sealed class CustomerVisits(TrimmeDbContext db, ICurrentUser user, IPermissionResolver permissions)
{
    /// <summary>Identity owns the permission catalogue; the endpoint matrix test fails if a code is not in it.</summary>
    public const string Permission = "Shop.Insights.View";

    public const int MixDays = 30;

    private bool? allowed;

    public async Task<bool> AllowedAsync(CancellationToken cancellationToken)
    {
        allowed ??= user.UserId is { } id && (await permissions.GetPermissionsAsync(id, cancellationToken)).Contains(Permission);
        return allowed.Value;
    }

    /// <summary>A label per online booking, or none at all without the permission.</summary>
    public async Task<IReadOnlyDictionary<BookingId, CustomerVisit>> LabelsAsync(IReadOnlyList<Booking> bookings, CancellationToken cancellationToken)
    {
        Guid[] customers = [.. bookings.Where(b => b.CustomerId is not null).Select(b => b.CustomerId!.Value).Distinct()];
        if (customers.Length == 0 || !await AllowedAsync(cancellationToken))
        {
            return new Dictionary<BookingId, CustomerVisit>();
        }

        var first = await FirstVisitsAsync(customers, cancellationToken);
        return bookings.Where(b => b.CustomerId is not null).ToDictionary(
            b => b.Id,
            b => first.TryGetValue(b.CustomerId!.Value, out var at) && at < b.StartsAt ? CustomerVisit.Returning : CustomerVisit.New);
    }

    /// <summary>Completed visits that started in [<paramref name="from"/>, <paramref name="to"/>), first visits apart; null without the permission.</summary>
    public async Task<ShopCustomerMix?> MixAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (!await AllowedAsync(cancellationToken))
        {
            return null;
        }

        var visits = await db.Set<Booking>().AsNoTracking()
            .Where(b => b.Status == BookingStatus.Completed && b.CustomerId != null && b.StartsAt >= from && b.StartsAt < to)
            .Select(b => new { CustomerId = b.CustomerId!.Value, b.StartsAt })
            .ToListAsync(cancellationToken);
        var first = await FirstVisitsAsync([.. visits.Select(v => v.CustomerId).Distinct()], cancellationToken);
        var firstVisits = visits.Count(v => first[v.CustomerId] == v.StartsAt);
        return new ShopCustomerMix(MixDays, firstVisits, visits.Count - firstVisits);
    }

    /// <summary>Each customer's first completed visit at this shop.</summary>
    private async Task<Dictionary<Guid, DateTimeOffset>> FirstVisitsAsync(Guid[] customers, CancellationToken cancellationToken) =>
        customers.Length == 0
            ? []
            : await db.Set<Booking>().AsNoTracking()
                .Where(b => b.Status == BookingStatus.Completed && b.CustomerId != null && customers.Contains(b.CustomerId.Value))
                .GroupBy(b => b.CustomerId!.Value)
                .Select(g => new { CustomerId = g.Key, First = g.Min(b => b.StartsAt) })
                .ToDictionaryAsync(x => x.CustomerId, x => x.First, cancellationToken);
}
