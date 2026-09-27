using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Services.Domain;

namespace Trimme.Modules.Services.Application;

// Shop self-service over its own catalogue (spec §10, §13). Every handler resolves the shop from ICurrentTenant first:
// no tenant (a suspended shop) answers 404 before anything is read or stamped. The tenant query filter scopes every
// read, so another shop's id is simply "not found".

internal sealed record ListShopServicesQuery(bool IncludeArchived) : IQuery<IReadOnlyList<ShopServiceResponse>?>;

internal sealed record GetShopServiceQuery(Guid ServiceId) : IQuery<ShopServiceResponse?>;

internal sealed record CreateShopServiceCommand(
    string NameAr,
    string? NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    Guid? CategoryId,
    decimal Price,
    int DurationMinutes,
    bool OnlineBookable) : ICommand<Result<ShopServiceResponse>>, ICatalogTextFields;

internal sealed record UpdateShopServiceCommand(
    Guid ServiceId,
    string NameAr,
    string? NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    Guid? CategoryId,
    decimal Price,
    int DurationMinutes,
    bool OnlineBookable,
    uint Version) : ICommand<Result<ShopServiceResponse>>, ICatalogTextFields;

internal enum CatalogStateChange
{
    Activate,
    Deactivate,
    Archive,
}

internal sealed record ChangeShopServiceStateCommand(Guid ServiceId, CatalogStateChange Change) : ICommand<Result<ShopServiceResponse>>;

internal sealed record DeleteShopServiceCommand(Guid ServiceId) : ICommand<Result>;

internal sealed record ReorderShopServicesCommand(IReadOnlyList<Guid> OrderedIds) : ICommand<Result<IReadOnlyList<ShopServiceResponse>>>;

internal sealed record ListShopPackagesQuery(bool IncludeArchived) : IQuery<IReadOnlyList<ShopPackageResponse>?>;

internal sealed record GetShopPackageQuery(Guid PackageId) : IQuery<ShopPackageResponse?>;

internal sealed record CreateShopPackageCommand(
    string NameAr,
    string? NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    decimal Price,
    int DurationMinutes,
    IReadOnlyList<Guid> ServiceIds) : ICommand<Result<ShopPackageResponse>>, ICatalogTextFields;

internal sealed record UpdateShopPackageCommand(
    Guid PackageId,
    string NameAr,
    string? NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    decimal Price,
    int DurationMinutes,
    IReadOnlyList<Guid> ServiceIds,
    uint Version) : ICommand<Result<ShopPackageResponse>>, ICatalogTextFields;

internal sealed record ChangeShopPackageStateCommand(Guid PackageId, CatalogStateChange Change) : ICommand<Result<ShopPackageResponse>>;

internal sealed record ReorderShopPackagesCommand(IReadOnlyList<Guid> OrderedIds) : ICommand<Result<IReadOnlyList<ShopPackageResponse>>>;

internal sealed class CreateShopServiceValidator : AbstractValidator<CreateShopServiceCommand>
{
    public CreateShopServiceValidator() => Include(new CatalogTextRules<CreateShopServiceCommand>());
}

internal sealed class UpdateShopServiceValidator : AbstractValidator<UpdateShopServiceCommand>
{
    public UpdateShopServiceValidator() => Include(new CatalogTextRules<UpdateShopServiceCommand>());
}

internal sealed class CreateShopPackageValidator : AbstractValidator<CreateShopPackageCommand>
{
    public CreateShopPackageValidator() => Include(new CatalogTextRules<CreateShopPackageCommand>());
}

internal sealed class UpdateShopPackageValidator : AbstractValidator<UpdateShopPackageCommand>
{
    public UpdateShopPackageValidator() => Include(new CatalogTextRules<UpdateShopPackageCommand>());
}

/// <summary>Shared queries of the shop's own catalogue (always inside the tenant filter).</summary>
internal sealed class ShopCatalogReader(TrimmeDbContext db)
{
    public async Task<ShopServiceResponse> ServiceAsync(ShopService service, CancellationToken cancellationToken)
    {
        var assigned = await db.Set<ProfessionalServiceAssignment>().CountAsync(a => a.ServiceId == service.Id, cancellationToken);
        return CatalogMapping.ToResponse(service, assigned);
    }

    public async Task<IReadOnlyList<ShopServiceResponse>> ServicesAsync(bool includeArchived, CancellationToken cancellationToken)
    {
        var services = await db.Set<ShopService>().AsNoTracking()
            .Where(s => includeArchived || !s.IsArchived)
            .OrderBy(s => s.IsArchived).ThenBy(s => s.DisplayOrder).ThenBy(s => s.CreatedAt)
            .ToListAsync(cancellationToken);
        var counts = await db.Set<ProfessionalServiceAssignment>()
            .GroupBy(a => a.ServiceId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        return [.. services.Select(s => CatalogMapping.ToResponse(s, counts.GetValueOrDefault(s.Id)))];
    }

    public async Task<ShopPackageResponse> PackageAsync(ServicePackage package, CancellationToken cancellationToken) =>
        CatalogMapping.ToResponse(package, await CatalogMapping.ServicesOfAsync(db, [package], cancellationToken));

    public async Task<IReadOnlyList<ShopPackageResponse>> PackagesAsync(bool includeArchived, CancellationToken cancellationToken)
    {
        var packages = await db.Set<ServicePackage>().AsNoTracking().Include(p => p.Items)
            .Where(p => includeArchived || !p.IsArchived)
            .OrderBy(p => p.IsArchived).ThenBy(p => p.DisplayOrder).ThenBy(p => p.CreatedAt)
            .ToListAsync(cancellationToken);
        var services = await CatalogMapping.ServicesOfAsync(db, packages, cancellationToken);
        return [.. packages.Select(p => CatalogMapping.ToResponse(p, services))];
    }

    /// <summary>The shop's own non-archived services with these ids, or an error if any is missing, foreign or archived.</summary>
    public async Task<Result<IReadOnlyList<ShopServiceId>>> ItemServicesAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        var requested = ids.Select(id => new ShopServiceId(id)).ToList();
        var found = await db.Set<ShopService>().CountAsync(s => requested.Contains(s.Id) && !s.IsArchived, cancellationToken);
        return found == requested.Distinct().Count() ? requested : CatalogErrors.ItemsNotInShop();
    }

    public async Task<int> NextServiceOrderAsync(CancellationToken cancellationToken) =>
        (await db.Set<ShopService>().MaxAsync(s => (int?)s.DisplayOrder, cancellationToken) ?? 0) + 1;

    public async Task<int> NextPackageOrderAsync(CancellationToken cancellationToken) =>
        (await db.Set<ServicePackage>().MaxAsync(p => (int?)p.DisplayOrder, cancellationToken) ?? 0) + 1;
}

internal sealed class ListShopServicesHandler(ICurrentTenant tenant, ShopCatalogReader reader)
    : IQueryHandler<ListShopServicesQuery, IReadOnlyList<ShopServiceResponse>?>
{
    public async Task<IReadOnlyList<ShopServiceResponse>?> Handle(ListShopServicesQuery query, CancellationToken cancellationToken) =>
        tenant.ShopId is null ? null : await reader.ServicesAsync(query.IncludeArchived, cancellationToken);
}

internal sealed class GetShopServiceHandler(TrimmeDbContext db, ICurrentTenant tenant, ShopCatalogReader reader)
    : IQueryHandler<GetShopServiceQuery, ShopServiceResponse?>
{
    public async Task<ShopServiceResponse?> Handle(GetShopServiceQuery query, CancellationToken cancellationToken)
    {
        if (tenant.ShopId is null)
        {
            return null;
        }

        var id = new ShopServiceId(query.ServiceId);
        var service = await db.Set<ShopService>().AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        return service is null ? null : await reader.ServiceAsync(service, cancellationToken);
    }
}

internal sealed class CreateShopServiceHandler(TrimmeDbContext db, ICurrentTenant tenant, ShopCatalogReader reader, TimeProvider clock)
    : ICommandHandler<CreateShopServiceCommand, Result<ShopServiceResponse>>
{
    public async Task<Result<ShopServiceResponse>> Handle(CreateShopServiceCommand command, CancellationToken cancellationToken)
    {
        if (tenant.ShopId is not { } shopId)
        {
            return CatalogErrors.ServiceNotFound();
        }

        var category = await CatalogMapping.CategoryAsync(db, command.CategoryId, null, cancellationToken);
        if (category.IsFailure)
        {
            return category.Error;
        }

        var created = ShopService.Create(
            EntityId.New<ShopServiceId>(), shopId, CatalogMapping.Text(command), category.Value, command.Price, command.DurationMinutes,
            command.OnlineBookable, await reader.NextServiceOrderAsync(cancellationToken), clock.GetUtcNow());
        if (created.IsFailure)
        {
            return created.Error;
        }

        db.Add(created.Value);
        await db.SaveChangesAsync(cancellationToken);
        return await reader.ServiceAsync(created.Value, cancellationToken);
    }
}

internal sealed class UpdateShopServiceHandler(TrimmeDbContext db, ICurrentTenant tenant, ShopCatalogReader reader, TimeProvider clock)
    : ICommandHandler<UpdateShopServiceCommand, Result<ShopServiceResponse>>
{
    public async Task<Result<ShopServiceResponse>> Handle(UpdateShopServiceCommand command, CancellationToken cancellationToken)
    {
        var id = new ShopServiceId(command.ServiceId);
        var service = tenant.ShopId is null ? null : await db.Set<ShopService>().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (service is null)
        {
            return CatalogErrors.ServiceNotFound();
        }

        var category = await CatalogMapping.CategoryAsync(db, command.CategoryId, service.CategoryId, cancellationToken);
        if (category.IsFailure)
        {
            return category.Error;
        }

        // Optimistic concurrency: a stale version makes the save fail with 409.
        db.Entry(service).Property(s => s.Version).OriginalValue = command.Version;
        var updated = service.Update(CatalogMapping.Text(command), category.Value, command.Price, command.DurationMinutes, command.OnlineBookable, clock.GetUtcNow());
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await reader.ServiceAsync(service, cancellationToken);
    }
}

internal sealed class ChangeShopServiceStateHandler(TrimmeDbContext db, ICurrentTenant tenant, ShopCatalogReader reader, TimeProvider clock)
    : ICommandHandler<ChangeShopServiceStateCommand, Result<ShopServiceResponse>>
{
    public async Task<Result<ShopServiceResponse>> Handle(ChangeShopServiceStateCommand command, CancellationToken cancellationToken)
    {
        var id = new ShopServiceId(command.ServiceId);
        var service = tenant.ShopId is null ? null : await db.Set<ShopService>().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (service is null)
        {
            return CatalogErrors.ServiceNotFound();
        }

        var now = clock.GetUtcNow();
        var changed = command.Change switch
        {
            CatalogStateChange.Activate => service.SetActive(true, now),
            CatalogStateChange.Deactivate => service.SetActive(false, now),
            _ => service.Archive(now),
        };
        if (changed.IsFailure)
        {
            return changed.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await reader.ServiceAsync(service, cancellationToken);
    }
}

/// <summary>Deletes a service only if nothing uses it (packages now, bookings from Phase 10); otherwise archive (R-SVC-02).</summary>
internal sealed class DeleteShopServiceHandler(TrimmeDbContext db, ICurrentTenant tenant, IEnumerable<IShopServiceUsage> usages)
    : ICommandHandler<DeleteShopServiceCommand, Result>
{
    public async Task<Result> Handle(DeleteShopServiceCommand command, CancellationToken cancellationToken)
    {
        var id = new ShopServiceId(command.ServiceId);
        if (tenant.ShopId is not { } shopId || await db.Set<ShopService>().SingleOrDefaultAsync(s => s.Id == id, cancellationToken) is not { } service)
        {
            return CatalogErrors.ServiceNotFound();
        }

        foreach (var usage in usages)
        {
            if (await usage.IsInUseAsync(shopId, command.ServiceId, cancellationToken))
            {
                return CatalogErrors.InUse();
            }
        }

        db.Remove(service);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>
/// Sets the display order from the full list of the shop's non-archived services. Anything else (a missing, foreign,
/// archived or repeated id) is refused and nothing changes. Last write wins between two concurrent reorders (D-071).
/// </summary>
internal sealed class ReorderShopServicesHandler(TrimmeDbContext db, ICurrentTenant tenant, ShopCatalogReader reader)
    : ICommandHandler<ReorderShopServicesCommand, Result<IReadOnlyList<ShopServiceResponse>>>
{
    public async Task<Result<IReadOnlyList<ShopServiceResponse>>> Handle(ReorderShopServicesCommand command, CancellationToken cancellationToken)
    {
        if (tenant.ShopId is null)
        {
            return CatalogErrors.ServiceNotFound();
        }

        var services = await db.Set<ShopService>().Where(s => !s.IsArchived).ToListAsync(cancellationToken);
        var byId = services.ToDictionary(s => s.Id.Value);
        if (command.OrderedIds.Count != services.Count || command.OrderedIds.Distinct().Count() != services.Count || !command.OrderedIds.All(byId.ContainsKey))
        {
            return CatalogErrors.OrderMismatch();
        }

        for (var position = 0; position < command.OrderedIds.Count; position++)
        {
            byId[command.OrderedIds[position]].MoveTo(position + 1);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result<IReadOnlyList<ShopServiceResponse>>.Success(await reader.ServicesAsync(includeArchived: false, cancellationToken));
    }
}

internal sealed class ListShopPackagesHandler(ICurrentTenant tenant, ShopCatalogReader reader)
    : IQueryHandler<ListShopPackagesQuery, IReadOnlyList<ShopPackageResponse>?>
{
    public async Task<IReadOnlyList<ShopPackageResponse>?> Handle(ListShopPackagesQuery query, CancellationToken cancellationToken) =>
        tenant.ShopId is null ? null : await reader.PackagesAsync(query.IncludeArchived, cancellationToken);
}

internal sealed class GetShopPackageHandler(TrimmeDbContext db, ICurrentTenant tenant, ShopCatalogReader reader)
    : IQueryHandler<GetShopPackageQuery, ShopPackageResponse?>
{
    public async Task<ShopPackageResponse?> Handle(GetShopPackageQuery query, CancellationToken cancellationToken)
    {
        if (tenant.ShopId is null)
        {
            return null;
        }

        var id = new ServicePackageId(query.PackageId);
        var package = await db.Set<ServicePackage>().AsNoTracking().Include(p => p.Items).SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        return package is null ? null : await reader.PackageAsync(package, cancellationToken);
    }
}

internal sealed class CreateShopPackageHandler(TrimmeDbContext db, ICurrentTenant tenant, ShopCatalogReader reader, TimeProvider clock)
    : ICommandHandler<CreateShopPackageCommand, Result<ShopPackageResponse>>
{
    public async Task<Result<ShopPackageResponse>> Handle(CreateShopPackageCommand command, CancellationToken cancellationToken)
    {
        if (tenant.ShopId is not { } shopId)
        {
            return CatalogErrors.PackageNotFound();
        }

        var items = await reader.ItemServicesAsync(command.ServiceIds ?? [], cancellationToken);
        if (items.IsFailure)
        {
            return items.Error;
        }

        var created = ServicePackage.Create(
            EntityId.New<ServicePackageId>(), shopId, CatalogMapping.Text(command), command.Price, command.DurationMinutes, items.Value,
            await reader.NextPackageOrderAsync(cancellationToken), clock.GetUtcNow());
        if (created.IsFailure)
        {
            return created.Error;
        }

        db.Add(created.Value);
        await db.SaveChangesAsync(cancellationToken);
        return await reader.PackageAsync(created.Value, cancellationToken);
    }
}

internal sealed class UpdateShopPackageHandler(TrimmeDbContext db, ICurrentTenant tenant, ShopCatalogReader reader, TimeProvider clock)
    : ICommandHandler<UpdateShopPackageCommand, Result<ShopPackageResponse>>
{
    public async Task<Result<ShopPackageResponse>> Handle(UpdateShopPackageCommand command, CancellationToken cancellationToken)
    {
        var id = new ServicePackageId(command.PackageId);
        var package = tenant.ShopId is null ? null : await db.Set<ServicePackage>().Include(p => p.Items).SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (package is null)
        {
            return CatalogErrors.PackageNotFound();
        }

        var items = await reader.ItemServicesAsync(command.ServiceIds ?? [], cancellationToken);
        if (items.IsFailure)
        {
            return items.Error;
        }

        // The package row is always updated (UpdatedAt), so this check also covers items-only edits.
        db.Entry(package).Property(p => p.Version).OriginalValue = command.Version;
        var updated = package.Update(CatalogMapping.Text(command), command.Price, command.DurationMinutes, items.Value, clock.GetUtcNow());
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await reader.PackageAsync(package, cancellationToken);
    }
}

internal sealed class ChangeShopPackageStateHandler(TrimmeDbContext db, ICurrentTenant tenant, ShopCatalogReader reader, TimeProvider clock)
    : ICommandHandler<ChangeShopPackageStateCommand, Result<ShopPackageResponse>>
{
    public async Task<Result<ShopPackageResponse>> Handle(ChangeShopPackageStateCommand command, CancellationToken cancellationToken)
    {
        var id = new ServicePackageId(command.PackageId);
        var package = tenant.ShopId is null ? null : await db.Set<ServicePackage>().Include(p => p.Items).SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (package is null)
        {
            return CatalogErrors.PackageNotFound();
        }

        var now = clock.GetUtcNow();
        var changed = command.Change switch
        {
            CatalogStateChange.Activate => package.SetActive(true, now),
            CatalogStateChange.Deactivate => package.SetActive(false, now),
            _ => package.Archive(now),
        };
        if (changed.IsFailure)
        {
            return changed.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await reader.PackageAsync(package, cancellationToken);
    }
}

internal sealed class ReorderShopPackagesHandler(TrimmeDbContext db, ICurrentTenant tenant, ShopCatalogReader reader)
    : ICommandHandler<ReorderShopPackagesCommand, Result<IReadOnlyList<ShopPackageResponse>>>
{
    public async Task<Result<IReadOnlyList<ShopPackageResponse>>> Handle(ReorderShopPackagesCommand command, CancellationToken cancellationToken)
    {
        if (tenant.ShopId is null)
        {
            return CatalogErrors.PackageNotFound();
        }

        var packages = await db.Set<ServicePackage>().Where(p => !p.IsArchived).ToListAsync(cancellationToken);
        var byId = packages.ToDictionary(p => p.Id.Value);
        if (command.OrderedIds.Count != packages.Count || command.OrderedIds.Distinct().Count() != packages.Count || !command.OrderedIds.All(byId.ContainsKey))
        {
            return CatalogErrors.OrderMismatch();
        }

        for (var position = 0; position < command.OrderedIds.Count; position++)
        {
            byId[command.OrderedIds[position]].MoveTo(position + 1);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result<IReadOnlyList<ShopPackageResponse>>.Success(await reader.PackagesAsync(includeArchived: false, cancellationToken));
    }
}
