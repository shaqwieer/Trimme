using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Reporting;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Identity.Domain;
using Trimme.Modules.Identity.Infrastructure.Persistence;

namespace Trimme.Modules.Identity.Infrastructure;

/// <summary><see cref="ICustomerDirectory"/>: a customer's display name and locale, never the mobile number.</summary>
internal sealed class CustomerDirectory(TrimmeDbContext db) : ICustomerDirectory
{
    public async Task<CustomerSummary?> FindAsync(Guid customerId, CancellationToken cancellationToken) =>
        await db.Set<ApplicationUser>().AsNoTracking()
            .Where(u => u.Id == customerId && u.UserType == UserType.Customer)
            .Select(u => new CustomerSummary(u.Id, u.DisplayName, u.PreferredLocale, u.DisabledAt == null))
            .SingleOrDefaultAsync(cancellationToken);
}

/// <summary><see cref="ICustomerStatistics"/>: customer registrations (the account's creation time).</summary>
internal sealed class CustomerStatistics(TrimmeDbContext db) : ICustomerStatistics
{
    public Task<int> RegisteredAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        db.Set<ApplicationUser>().AsNoTracking()
            .CountAsync(u => u.UserType == UserType.Customer && u.CreatedAt >= from && u.CreatedAt < to, cancellationToken);
}

/// <summary><see cref="IUserNameLookup"/>: display names and user types only.</summary>
internal sealed class UserNameLookup(TrimmeDbContext db) : IUserNameLookup
{
    public async Task<IReadOnlyDictionary<Guid, UserNameEntry>> FindAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, UserNameEntry>();
        }

        var ids = userIds.Distinct().ToArray();
        var rows = await db.Set<ApplicationUser>().AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.UserType })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.Id, r => new UserNameEntry(r.Id, r.DisplayName, r.UserType.ToString()));
    }
}
