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

/// <summary><c>Services</c> (with the barber's own length for each, D-149) wins over the plain <c>ServiceIds</c>.</summary>
internal sealed record SetShopBarberServicesCommand(Guid ProfessionalId, IReadOnlyList<Guid> ServiceIds, IReadOnlyList<BarberServiceItem>? Services = null)
    : ICommand<Result<ProfessionalServicesResponse>>;

/// <summary>A service a barber does, and their own length for it (D-149); <see langword="null"/> is the service's.</summary>
public sealed record BarberServiceItem(Guid ServiceId, int? DurationMinutes);

/// <summary>
/// Replaces the services a barber does (the owner's and the admin's, D-146, D-149): only the barber's own shop's services.
/// With <c>items</c> each barber's own length is set too (one equal to the service's is stored as none); with plain ids
/// the lengths of services they keep are untouched.
/// </summary>
internal static class BarberServices
{
    public static async Task<Result<int>> ReplaceAsync(
        TrimmeDbContext db, ProfessionalSummary professional, IReadOnlyList<Guid>? serviceIds, IReadOnlyList<BarberServiceItem>? items, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var wanted = items is not null
            ? [.. items.GroupBy(i => i.ServiceId).Select(g => g.First())]
            : (serviceIds ?? []).Distinct().Select(id => new BarberServiceItem(id, null)).ToList();
        if (items is not null && wanted.Any(i => i.DurationMinutes is { } minutes && !CatalogRules.IsValidDuration(minutes)))
        {
            return CatalogErrors.InvalidBarberDuration();
        }

        var ids = wanted.Select(w => new ShopServiceId(w.ServiceId)).ToList();
        var services = await db.Set<ShopService>()
            .Where(s => ids.Contains(s.Id) && s.ShopId == professional.ShopId && !s.IsArchived).ToDictionaryAsync(s => s.Id, cancellationToken);
        if (services.Count != ids.Count)
        {
            return CatalogErrors.ItemsNotInShop();
        }

        var current = await db.Set<ProfessionalServiceAssignment>().Where(a => a.ProfessionalId == professional.Id).ToListAsync(cancellationToken);
        db.RemoveRange(current.Where(a => !ids.Contains(a.ServiceId)));
        foreach (var item in wanted)
        {
            var id = new ShopServiceId(item.ServiceId);
            var assignment = current.FirstOrDefault(a => a.ServiceId == id);
            if (assignment is null)
            {
                assignment = new ProfessionalServiceAssignment(professional.ShopId, professional.Id, id, now);
                db.Add(assignment);
            }

            if (items is not null)
            {
                assignment.SetDuration(item.DurationMinutes, services[id].DurationMinutes);
            }
        }

        return ids.Count;
    }
}

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

        var replaced = await BarberServices.ReplaceAsync(db, professional, command.ServiceIds, command.Services, clock.GetUtcNow(), cancellationToken);
        if (replaced.IsFailure)
        {
            return replaced.Error;
        }

        audit.Record(new AuditRecord(
            "professional.services_assigned", "Professional", professionalId.ToString(), professional.ShopId, $"By the shop: {replaced.Value} services assigned",
            SummaryArgs: AuditArgs.Of(("by", "shop"), ("count", replaced.Value))));
        await db.SaveChangesAsync(cancellationToken);
        return await ProfessionalServicesReader.ReadAsync(db, professional, cancellationToken);
    }
}
