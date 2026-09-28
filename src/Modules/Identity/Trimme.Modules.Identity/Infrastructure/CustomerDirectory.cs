using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Directories;
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
