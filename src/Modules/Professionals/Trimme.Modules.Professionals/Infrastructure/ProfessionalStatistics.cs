using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Reporting;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Professionals.Domain;

namespace Trimme.Modules.Professionals.Infrastructure;

/// <summary><see cref="IProfessionalStatistics"/>, read through the caller's scope (the admin overview opens the admin scope).</summary>
internal sealed class ProfessionalStatistics(TrimmeDbContext db) : IProfessionalStatistics
{
    public async Task<ProfessionalCounts> CountsAsync(DateTimeOffset addedSince, CancellationToken cancellationToken)
    {
        var professionals = db.Set<Professional>().AsNoTracking();
        return new ProfessionalCounts(
            await professionals.CountAsync(p => p.Status == ProfessionalStatus.Active, cancellationToken),
            await professionals.CountAsync(p => p.Status == ProfessionalStatus.Disabled, cancellationToken),
            await professionals.CountAsync(p => p.CreatedAt >= addedSince, cancellationToken));
    }
}
