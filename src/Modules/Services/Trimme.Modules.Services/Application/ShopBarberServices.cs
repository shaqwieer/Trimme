using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Auditing;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Services.Application.Admin;
using Trimme.Modules.Services.Domain;

namespace Trimme.Modules.Services.Application;

// The services a barber does, from the barber's page in the owner's dashboard (D-146): the same choice the owner already
// makes per service (D-137), seen from the barber. The owner's own shop only (the tenant filter: another shop's barber
// is 404, another shop's service is refused, and the composite foreign keys refuse it in the database too).

internal sealed record GetShopBarberServicesQuery(Guid ProfessionalId) : IQuery<ProfessionalServicesResponse?>;

internal sealed record SetShopBarberServicesCommand(Guid ProfessionalId, IReadOnlyList<Guid> ServiceIds) : ICommand<Result<ProfessionalServicesResponse>>;

internal sealed class GetShopBarberServicesHandler(TrimmeDbContext db, IProfessionalDirectory professionals)
    : IQueryHandler<GetShopBarberServicesQuery, ProfessionalServicesResponse?>
{
    public async Task<ProfessionalServicesResponse?> Handle(GetShopBarberServicesQuery query, CancellationToken cancellationToken)
    {
        var professional = await professionals.FindAsync(new ProfessionalId(query.ProfessionalId), cancellationToken);
        return professional is null ? null : await ProfessionalServicesReader.ReadAsync(db, professional, cancellationToken);
    }
}

internal sealed class SetShopBarberServicesHandler(TrimmeDbContext db, IProfessionalDirectory professionals, IAuditLog audit, TimeProvider clock)
    : ICommandHandler<SetShopBarberServicesCommand, Result<ProfessionalServicesResponse>>
{
    public async Task<Result<ProfessionalServicesResponse>> Handle(SetShopBarberServicesCommand command, CancellationToken cancellationToken)
    {
        var professionalId = new ProfessionalId(command.ProfessionalId);
        if (await professionals.FindAsync(professionalId, cancellationToken) is not { } professional)
        {
            return Error.NotFound("professional.not_found", "The professional was not found.");
        }

        var requested = (command.ServiceIds ?? []).Distinct().Select(id => new ShopServiceId(id)).ToList();
        var valid = await db.Set<ShopService>()
            .CountAsync(s => requested.Contains(s.Id) && s.ShopId == professional.ShopId && !s.IsArchived, cancellationToken);
        if (valid != requested.Count)
        {
            return CatalogErrors.ItemsNotInShop();
        }

        var current = await db.Set<ProfessionalServiceAssignment>().Where(a => a.ProfessionalId == professionalId).ToListAsync(cancellationToken);
        db.RemoveRange(current.Where(a => !requested.Contains(a.ServiceId)));
        var now = clock.GetUtcNow();
        foreach (var serviceId in requested.Where(id => current.All(a => a.ServiceId != id)))
        {
            db.Add(new ProfessionalServiceAssignment(professional.ShopId, professionalId, serviceId, now));
        }

        audit.Record(new AuditRecord(
            "professional.services_assigned", "Professional", professionalId.ToString(), professional.ShopId, $"By the shop: {requested.Count} services assigned"));
        await db.SaveChangesAsync(cancellationToken);
        return await ProfessionalServicesReader.ReadAsync(db, professional, cancellationToken);
    }
}
