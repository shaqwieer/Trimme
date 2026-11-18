using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Scheduling;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Availability.Domain;

namespace Trimme.Modules.Availability.Application;

/// <summary>
/// <see cref="IBarberBreakReport"/> (D-147): a barber's own one-off breaks on the dates asked for, through the shop's
/// tenant filter. Each saved window is one break (the barber-day save joins windows that touch, D-134).
/// </summary>
internal sealed class BarberBreakReport(TrimmeDbContext db) : IBarberBreakReport
{
    public async Task<IReadOnlyDictionary<ProfessionalId, BreakTotals>> TotalsAsync(ShopId shopId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var rows = await db.Set<ScheduleBreak>().AsNoTracking()
            .Where(b => b.ShopId == shopId && b.ProfessionalId != null && b.Date != null && b.Date >= from && b.Date <= to)
            .Select(b => new { b.ProfessionalId, b.StartMinute, b.EndMinute })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.ProfessionalId!.Value)
            .ToDictionary(g => g.Key, g => new BreakTotals(g.Count(), g.Sum(r => r.EndMinute - r.StartMinute)));
    }
}
