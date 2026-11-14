using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Privacy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Customers.Domain;

namespace Trimme.Modules.Customers.Application.Customer;

/// <summary>A deleted account's saved shops and professionals go with it (D-144); the caller saves the unit of work.</summary>
internal sealed class FavoritesEraser(TrimmeDbContext db) : ICustomerDataEraser
{
    public async Task EraseAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var favorites = await db.Set<Favorite>().Where(f => f.CustomerId == customerId).ToListAsync(cancellationToken);
        db.Set<Favorite>().RemoveRange(favorites);
    }
}
