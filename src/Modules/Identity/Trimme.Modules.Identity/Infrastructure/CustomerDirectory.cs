using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Notifications;
using Trimme.BuildingBlocks.Application.Privacy;
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

/// <summary>
/// <see cref="ICustomerContactReader"/>: the customer's verified mobile for WhatsApp messages (spec §16). Decrypted only
/// here, for the notification jobs (architecture rule); never logged.
/// </summary>
internal sealed class CustomerContactReader(TrimmeDbContext db, IPersonalDataProtector protector) : ICustomerContactReader
{
    public async Task<CustomerContact?> FindAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var row = await db.Set<ApplicationUser>().AsNoTracking()
            .Where(u => u.Id == customerId && u.UserType == UserType.Customer)
            .Select(u => new { u.Id, u.DisplayName, u.ProtectedPhone, u.PreferredLocale, u.DisabledAt })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null
            ? null
            : new CustomerContact(
                row.Id, row.DisplayName, row.ProtectedPhone is null ? null : protector.Unprotect(row.ProtectedPhone, PersonalDataPurposes.MobileNumber),
                row.PreferredLocale, row.DisabledAt == null);
    }
}

/// <summary><see cref="ICustomerNumberCheck"/>: a keyed-hash lookup, so the number is never compared in clear.</summary>
internal sealed class CustomerNumberCheck(TrimmeDbContext db, IPersonalDataProtector protector) : ICustomerNumberCheck
{
    public Task<bool> IsCustomerNumberAsync(string phoneE164, CancellationToken cancellationToken)
    {
        var hash = protector.LookupHash(phoneE164, PersonalDataPurposes.MobileNumber);
        return db.Set<ApplicationUser>().AsNoTracking().AnyAsync(u => u.UserType == UserType.Customer && u.PhoneLookupHash == hash, cancellationToken);
    }
}

/// <summary><see cref="IStaffDirectory"/>: enabled platform admins whose roles grant a permission (ids only).</summary>
internal sealed class StaffDirectory(TrimmeDbContext db) : IStaffDirectory
{
    public async Task<IReadOnlyList<Guid>> ActiveAdminsWithPermissionAsync(string permission, CancellationToken cancellationToken) =>
        await (from userRole in db.Set<Microsoft.AspNetCore.Identity.IdentityUserRole<Guid>>()
               join grant in db.Set<RolePermission>() on userRole.RoleId equals grant.RoleId
               join user in db.Set<ApplicationUser>() on userRole.UserId equals user.Id
               where grant.PermissionCode == permission && user.UserType == UserType.PlatformAdmin && user.DisabledAt == null
               select user.Id)
            .Distinct()
            .ToListAsync(cancellationToken);
}
