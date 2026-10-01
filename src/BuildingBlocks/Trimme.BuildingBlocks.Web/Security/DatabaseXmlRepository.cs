using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trimme.BuildingBlocks.Infrastructure.Persistence;

namespace Trimme.BuildingBlocks.Web.Security;

/// <summary>
/// Stores the Data Protection key ring in <c>infra.data_protection_keys</c> so every API process shares it and it
/// survives container restarts (otherwise session cookies and password-reset links break on every deploy).
/// Outside Development and Testing each key is wrapped with a certificate that is never stored in the database (D-120,
/// <see cref="KeyEncryptionOptions"/>).
/// </summary>
internal sealed class DatabaseXmlRepository(IServiceScopeFactory scopeFactory) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
        return db.Set<DataProtectionKeyRecord>()
            .AsNoTracking()
            .OrderBy(k => k.Id)
            .Select(k => k.Xml)
            .AsEnumerable()
            .Select(xml => XElement.Parse(xml))
            .ToArray();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        ArgumentNullException.ThrowIfNull(element);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
        db.Set<DataProtectionKeyRecord>().Add(new DataProtectionKeyRecord
        {
            FriendlyName = friendlyName,
            Xml = element.ToString(SaveOptions.DisableFormatting),
        });
        db.SaveChanges();
    }
}
