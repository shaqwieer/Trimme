using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trimme.BuildingBlocks.Application.Qr;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.Modules.Professionals.Domain;

namespace Trimme.Modules.Professionals.Infrastructure.Seeding;

/// <summary>
/// D-155 (the client's 7-2-3): every active barber has their own QR code. New barbers get one when they are added or turned
/// on again; this gives one, at <c>migrate</c>, to the active barbers added before D-155 without one. Idempotent: a barber
/// with a code (even one an admin turned off) is left alone.
/// </summary>
internal sealed class ProfessionalQrBackfill : IReferenceDataSynchronizer
{
    public int Order => 700;

    public string Name => "Barber QR codes";

    public async Task SynchronizeAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<TrimmeDbContext>();
        var codes = services.GetRequiredService<IProfessionalQrCodes>();
        using var _ = services.GetRequiredService<ISystemDataScope>().Begin();
        var active = await db.Set<Professional>().AsNoTracking()
            .Where(p => p.Status == ProfessionalStatus.Active)
            .Select(p => new { p.ShopId, p.Id })
            .ToListAsync(cancellationToken);
        foreach (var professional in active)
        {
            await codes.EnsureAsync(professional.ShopId, professional.Id, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
