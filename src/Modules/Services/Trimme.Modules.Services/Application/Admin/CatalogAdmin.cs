using System.Globalization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Auditing;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Paging;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Services.Domain;

namespace Trimme.Modules.Services.Application.Admin;

// Platform admin use cases. Categories are platform-owned; everything else is shop-owned and read inside
// IAdminDataScope. Admins never set a shared price: moderation hides an item, and a support override corrects one
// service of one shop, with a reason, audited (spec §7, §10, DV-S02).

public sealed record AdminCategoryResponse(Guid Id, string NameAr, string NameEn, string Icon, int DisplayOrder, bool IsActive, int ServiceCount);

public enum CatalogStateFilter
{
    Active,
    Inactive,
    Archived,
    Hidden,
}

public enum ModerationAction
{
    Hide,
    Unhide,
}

public sealed record AdminServiceListItem(
    Guid Id,
    Guid ShopId,
    string ShopNameAr,
    string ShopNameEn,
    string NameAr,
    string? NameEn,
    Guid? CategoryId,
    decimal Price,
    string Currency,
    int DurationMinutes,
    bool IsActive,
    bool IsArchived,
    ModerationState Moderation);

public sealed record AdminServiceResponse(
    Guid Id,
    Guid ShopId,
    string ShopNameAr,
    string ShopNameEn,
    string NameAr,
    string? NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    Guid? CategoryId,
    decimal Price,
    string Currency,
    int DurationMinutes,
    bool OnlineBookable,
    bool IsActive,
    bool IsArchived,
    ModerationState Moderation,
    string? ModerationReason,
    int AssignedProfessionalCount,
    uint Version);

public sealed record AdminPackageListItem(
    Guid Id,
    Guid ShopId,
    string ShopNameAr,
    string ShopNameEn,
    string NameAr,
    string? NameEn,
    decimal Price,
    string Currency,
    int DurationMinutes,
    int ItemCount,
    bool IsActive,
    bool IsArchived,
    ModerationState Moderation,
    string? ModerationReason);

public sealed record ProfessionalServiceOption(
    Guid ServiceId,
    string NameAr,
    string? NameEn,
    decimal Price,
    string Currency,
    int DurationMinutes,
    bool IsActive,
    bool Assigned);

/// <summary>The professional's own shop's services (with that shop's prices) and which are assigned (DV-S04).</summary>
public sealed record ProfessionalServicesResponse(Guid ProfessionalId, Guid ShopId, IReadOnlyList<ProfessionalServiceOption> Services);

internal interface ICategoryFields
{
    string NameAr { get; }

    string NameEn { get; }

    string Icon { get; }

    int DisplayOrder { get; }
}

internal sealed record ListCategoriesQuery : IQuery<IReadOnlyList<AdminCategoryResponse>>;

internal sealed record CreateCategoryCommand(string NameAr, string NameEn, string Icon, int DisplayOrder) : ICommand<Result<AdminCategoryResponse>>, ICategoryFields;

internal sealed record UpdateCategoryCommand(Guid CategoryId, string NameAr, string NameEn, string Icon, int DisplayOrder) : ICommand<Result<AdminCategoryResponse>>, ICategoryFields;

internal sealed record SetCategoryActiveCommand(Guid CategoryId, bool Active) : ICommand<Result<AdminCategoryResponse>>;

internal sealed record ListAdminServicesQuery(PageRequest Page, Guid? ShopId, Guid? CategoryId, CatalogStateFilter? State, string? Search)
    : IQuery<PagedResponse<AdminServiceListItem>>;

internal sealed record GetAdminServiceQuery(Guid ServiceId) : IQuery<AdminServiceResponse?>;

internal sealed record ModerateServiceCommand(Guid ServiceId, ModerationAction Action, string? Reason) : ICommand<Result<AdminServiceResponse>>;

internal sealed record OverrideServiceCommand(
    Guid ServiceId,
    string NameAr,
    string? NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    Guid? CategoryId,
    decimal Price,
    int DurationMinutes,
    bool OnlineBookable,
    string Reason,
    uint Version) : ICommand<Result<AdminServiceResponse>>, ICatalogTextFields;

internal sealed record ListAdminPackagesQuery(PageRequest Page, Guid? ShopId, CatalogStateFilter? State, string? Search)
    : IQuery<PagedResponse<AdminPackageListItem>>;

internal sealed record ModeratePackageCommand(Guid PackageId, ModerationAction Action, string? Reason) : ICommand<Result<AdminPackageListItem>>;

internal sealed record GetProfessionalServicesQuery(Guid ProfessionalId) : IQuery<ProfessionalServicesResponse?>;

internal sealed record SetProfessionalServicesCommand(Guid ProfessionalId, IReadOnlyList<Guid> ServiceIds) : ICommand<Result<ProfessionalServicesResponse>>;

internal sealed class CategoryRules<T> : AbstractValidator<T>
    where T : ICategoryFields
{
    public CategoryRules()
    {
        RuleFor(c => c.NameAr).Must(n => !string.IsNullOrWhiteSpace(n)).WithErrorCode("validation.required")
            .MaximumLength(80).WithErrorCode("validation.too_long");
        RuleFor(c => c.NameEn).Must(n => !string.IsNullOrWhiteSpace(n)).WithErrorCode("validation.required")
            .MaximumLength(80).WithErrorCode("validation.too_long");
        RuleFor(c => c.Icon).Must(ServiceCategory.Icons.Contains).WithErrorCode("validation.invalid");
        RuleFor(c => c.DisplayOrder).InclusiveBetween(0, 1000).WithErrorCode("validation.invalid");
    }
}

internal sealed class CreateCategoryValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryValidator() => Include(new CategoryRules<CreateCategoryCommand>());
}

internal sealed class UpdateCategoryValidator : AbstractValidator<UpdateCategoryCommand>
{
    public UpdateCategoryValidator() => Include(new CategoryRules<UpdateCategoryCommand>());
}

internal sealed class ModerateServiceValidator : AbstractValidator<ModerateServiceCommand>
{
    public ModerateServiceValidator() => RuleFor(c => c.Reason).Must((c, r) => c.Action == ModerationAction.Unhide || (r?.Trim().Length ?? 0) >= 5)
        .WithErrorCode("validation.reason_required").MaximumLength(500).WithErrorCode("validation.too_long");
}

internal sealed class ModeratePackageValidator : AbstractValidator<ModeratePackageCommand>
{
    public ModeratePackageValidator() => RuleFor(c => c.Reason).Must((c, r) => c.Action == ModerationAction.Unhide || (r?.Trim().Length ?? 0) >= 5)
        .WithErrorCode("validation.reason_required").MaximumLength(500).WithErrorCode("validation.too_long");
}

internal sealed class OverrideServiceValidator : AbstractValidator<OverrideServiceCommand>
{
    public OverrideServiceValidator()
    {
        Include(new CatalogTextRules<OverrideServiceCommand>());
        RuleFor(c => c.Reason).Must(r => (r?.Trim().Length ?? 0) >= 5).WithErrorCode("validation.reason_required")
            .MaximumLength(500).WithErrorCode("validation.too_long");
    }
}

internal sealed class ListCategoriesHandler(TrimmeDbContext db, IAdminDataScope scope) : IQueryHandler<ListCategoriesQuery, IReadOnlyList<AdminCategoryResponse>>
{
    public async Task<IReadOnlyList<AdminCategoryResponse>> Handle(ListCategoriesQuery query, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        return await AdminCatalogReader.CategoriesAsync(db, null, cancellationToken);
    }
}

internal sealed class CreateCategoryHandler(TrimmeDbContext db, IAdminDataScope scope, IAuditLog audit, TimeProvider clock)
    : ICommandHandler<CreateCategoryCommand, Result<AdminCategoryResponse>>
{
    public async Task<Result<AdminCategoryResponse>> Handle(CreateCategoryCommand command, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var category = ServiceCategory.Create(EntityId.New<ServiceCategoryId>(), command.NameAr, command.NameEn, command.Icon, command.DisplayOrder, clock.GetUtcNow());
        db.Add(category);
        audit.Record(new AuditRecord("service_category.created", nameof(ServiceCategory), category.Id.ToString(), Summary: $"Created ({category.Icon})"));
        await db.SaveChangesAsync(cancellationToken);
        return (await AdminCatalogReader.CategoriesAsync(db, category.Id, cancellationToken)).Single();
    }
}

internal sealed class UpdateCategoryHandler(TrimmeDbContext db, IAdminDataScope scope, IAuditLog audit, TimeProvider clock)
    : ICommandHandler<UpdateCategoryCommand, Result<AdminCategoryResponse>>
{
    public async Task<Result<AdminCategoryResponse>> Handle(UpdateCategoryCommand command, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var id = new ServiceCategoryId(command.CategoryId);
        if (await db.Set<ServiceCategory>().SingleOrDefaultAsync(c => c.Id == id, cancellationToken) is not { } category)
        {
            return CatalogErrors.CategoryNotFound();
        }

        category.Update(command.NameAr, command.NameEn, command.Icon, command.DisplayOrder, clock.GetUtcNow());
        audit.Record(new AuditRecord("service_category.updated", nameof(ServiceCategory), category.Id.ToString(), Summary: "Edited"));
        await db.SaveChangesAsync(cancellationToken);
        return (await AdminCatalogReader.CategoriesAsync(db, category.Id, cancellationToken)).Single();
    }
}

internal sealed class SetCategoryActiveHandler(TrimmeDbContext db, IAdminDataScope scope, IAuditLog audit, TimeProvider clock)
    : ICommandHandler<SetCategoryActiveCommand, Result<AdminCategoryResponse>>
{
    public async Task<Result<AdminCategoryResponse>> Handle(SetCategoryActiveCommand command, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var id = new ServiceCategoryId(command.CategoryId);
        if (await db.Set<ServiceCategory>().SingleOrDefaultAsync(c => c.Id == id, cancellationToken) is not { } category)
        {
            return CatalogErrors.CategoryNotFound();
        }

        category.SetActive(command.Active, clock.GetUtcNow());
        audit.Record(new AuditRecord(
            command.Active ? "service_category.activated" : "service_category.deactivated", nameof(ServiceCategory), category.Id.ToString()));
        await db.SaveChangesAsync(cancellationToken);
        return (await AdminCatalogReader.CategoriesAsync(db, category.Id, cancellationToken)).Single();
    }
}

/// <summary>Shared admin reads; callers hold <see cref="IAdminDataScope"/> for the shop-owned parts.</summary>
internal static class AdminCatalogReader
{
    public static async Task<IReadOnlyList<AdminCategoryResponse>> CategoriesAsync(TrimmeDbContext db, ServiceCategoryId? only, CancellationToken cancellationToken)
    {
        // Categories are platform-owned; the per-category service count spans all shops, so callers hold the admin scope.
        var categories = await db.Set<ServiceCategory>().AsNoTracking()
            .Where(c => only == null || c.Id == only)
            .OrderBy(c => c.DisplayOrder).ThenBy(c => c.NameAr)
            .ToListAsync(cancellationToken);
        var counts = await db.Set<ShopService>()
            .Where(s => s.CategoryId != null && !s.IsArchived)
            .GroupBy(s => s.CategoryId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key!.Value, g => g.Count, cancellationToken);
        return [.. categories.Select(c => new AdminCategoryResponse(c.Id.Value, c.NameAr, c.NameEn, c.Icon, c.DisplayOrder, c.IsActive, counts.GetValueOrDefault(c.Id)))];
    }

    public static async Task<AdminServiceResponse> ServiceAsync(TrimmeDbContext db, IShopDirectory shops, ShopService service, CancellationToken cancellationToken)
    {
        var shop = await shops.FindAsync(service.ShopId, cancellationToken);
        var assigned = await db.Set<ProfessionalServiceAssignment>().CountAsync(a => a.ServiceId == service.Id, cancellationToken);
        return new AdminServiceResponse(
            service.Id.Value,
            service.ShopId.Value,
            shop?.NameAr ?? string.Empty,
            shop?.NameEn ?? string.Empty,
            service.NameAr,
            service.NameEn,
            service.DescriptionAr,
            service.DescriptionEn,
            service.CategoryId?.Value,
            service.Price,
            service.Currency,
            service.DurationMinutes,
            service.OnlineBookable,
            service.IsActive,
            service.IsArchived,
            service.Moderation,
            service.ModerationReason,
            assigned,
            service.Version);
    }

    public static AdminPackageListItem Package(ServicePackage package, ShopSummary? shop) => new(
        package.Id.Value,
        package.ShopId.Value,
        shop?.NameAr ?? string.Empty,
        shop?.NameEn ?? string.Empty,
        package.NameAr,
        package.NameEn,
        package.Price,
        package.Currency,
        package.DurationMinutes,
        package.Items.Count,
        package.IsActive,
        package.IsArchived,
        package.Moderation,
        package.ModerationReason);
}

internal sealed class ListAdminServicesHandler(TrimmeDbContext db, IAdminDataScope scope, IShopDirectory shops)
    : IQueryHandler<ListAdminServicesQuery, PagedResponse<AdminServiceListItem>>
{
    public async Task<PagedResponse<AdminServiceListItem>> Handle(ListAdminServicesQuery query, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var services = db.Set<ShopService>().AsNoTracking();
        if (query.ShopId is { } shop)
        {
            var shopId = new ShopId(shop);
            services = services.Where(s => s.ShopId == shopId);
        }

        if (query.CategoryId is { } category)
        {
            var categoryId = new ServiceCategoryId(category);
            services = services.Where(s => s.CategoryId == categoryId);
        }

        services = query.State switch
        {
            CatalogStateFilter.Active => services.Where(s => s.IsActive && !s.IsArchived),
            CatalogStateFilter.Inactive => services.Where(s => !s.IsActive && !s.IsArchived),
            CatalogStateFilter.Archived => services.Where(s => s.IsArchived),
            CatalogStateFilter.Hidden => services.Where(s => s.Moderation == ModerationState.Hidden),
            _ => services,
        };

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = $"%{query.Search.Trim()}%";
            services = services.Where(s => EF.Functions.ILike(s.NameAr, term) || (s.NameEn != null && EF.Functions.ILike(s.NameEn, term)));
        }

        var total = await services.CountAsync(cancellationToken);
        var page = await services.OrderBy(s => s.ShopId).ThenBy(s => s.DisplayOrder).ThenBy(s => s.Id)
            .Skip(query.Page.Skip).Take(query.Page.PageSize).ToListAsync(cancellationToken);
        var names = await shops.FindManyAsync([.. page.Select(s => s.ShopId)], cancellationToken);
        var items = page.Select(s => new AdminServiceListItem(
            s.Id.Value, s.ShopId.Value, names.GetValueOrDefault(s.ShopId)?.NameAr ?? string.Empty, names.GetValueOrDefault(s.ShopId)?.NameEn ?? string.Empty,
            s.NameAr, s.NameEn, s.CategoryId?.Value, s.Price, s.Currency, s.DurationMinutes, s.IsActive, s.IsArchived, s.Moderation));
        return new PagedResponse<AdminServiceListItem>([.. items], query.Page.Page, query.Page.PageSize, total);
    }
}

internal sealed class GetAdminServiceHandler(TrimmeDbContext db, IAdminDataScope scope, IShopDirectory shops)
    : IQueryHandler<GetAdminServiceQuery, AdminServiceResponse?>
{
    public async Task<AdminServiceResponse?> Handle(GetAdminServiceQuery query, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var id = new ShopServiceId(query.ServiceId);
        var service = await db.Set<ShopService>().AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        return service is null ? null : await AdminCatalogReader.ServiceAsync(db, shops, service, cancellationToken);
    }
}

internal sealed class ModerateServiceHandler(TrimmeDbContext db, IAdminDataScope scope, IShopDirectory shops, IAuditLog audit, TimeProvider clock)
    : ICommandHandler<ModerateServiceCommand, Result<AdminServiceResponse>>
{
    public async Task<Result<AdminServiceResponse>> Handle(ModerateServiceCommand command, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var id = new ShopServiceId(command.ServiceId);
        if (await db.Set<ShopService>().SingleOrDefaultAsync(s => s.Id == id, cancellationToken) is not { } service)
        {
            return CatalogErrors.ServiceNotFound();
        }

        var hide = command.Action == ModerationAction.Hide;
        service.Moderate(hide ? ModerationState.Hidden : ModerationState.Visible, command.Reason?.Trim(), clock.GetUtcNow());
        audit.Record(new AuditRecord(
            hide ? "service.hidden" : "service.unhidden", nameof(ShopService), service.Id.ToString(), service.ShopId,
            hide ? "Hidden from customers by the platform" : "Visible again", command.Reason?.Trim()));
        await db.SaveChangesAsync(cancellationToken);
        return await AdminCatalogReader.ServiceAsync(db, shops, service, cancellationToken);
    }
}

/// <summary>
/// Support override: corrects one service of one shop at the shop's request or for compliance. Explicit permission,
/// a reason, optimistic concurrency and an audit entry with what changed (before → after). Never a bulk edit.
/// </summary>
internal sealed class OverrideServiceHandler(TrimmeDbContext db, IAdminDataScope scope, IShopDirectory shops, IAuditLog audit, TimeProvider clock)
    : ICommandHandler<OverrideServiceCommand, Result<AdminServiceResponse>>
{
    public async Task<Result<AdminServiceResponse>> Handle(OverrideServiceCommand command, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var id = new ShopServiceId(command.ServiceId);
        if (await db.Set<ShopService>().SingleOrDefaultAsync(s => s.Id == id, cancellationToken) is not { } service)
        {
            return CatalogErrors.ServiceNotFound();
        }

        var category = await CatalogMapping.CategoryAsync(db, command.CategoryId, service.CategoryId, cancellationToken);
        if (category.IsFailure)
        {
            return category.Error;
        }

        var before = (service.NameAr, service.NameEn, service.Price, service.DurationMinutes, service.CategoryId, service.OnlineBookable);
        db.Entry(service).Property(s => s.Version).OriginalValue = command.Version;
        var updated = service.Update(CatalogMapping.Text(command), category.Value, command.Price, command.DurationMinutes, command.OnlineBookable, clock.GetUtcNow());
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        var changes = new List<string>();
        if (before.Price != service.Price)
        {
            changes.Add(string.Create(CultureInfo.InvariantCulture, $"Price {before.Price:0.00} → {service.Price:0.00} {service.Currency}"));
        }

        if (before.DurationMinutes != service.DurationMinutes)
        {
            changes.Add(string.Create(CultureInfo.InvariantCulture, $"Duration {before.DurationMinutes} → {service.DurationMinutes} min"));
        }

        if (before.NameAr != service.NameAr || before.NameEn != service.NameEn)
        {
            changes.Add("Name changed");
        }

        if (before.CategoryId != service.CategoryId)
        {
            changes.Add("Category changed");
        }

        if (before.OnlineBookable != service.OnlineBookable)
        {
            changes.Add(service.OnlineBookable ? "Online booking on" : "Online booking off");
        }

        audit.Record(new AuditRecord(
            "service.support_override", nameof(ShopService), service.Id.ToString(), service.ShopId,
            changes.Count == 0 ? "No changes" : string.Join("; ", changes), command.Reason.Trim()));
        await db.SaveChangesAsync(cancellationToken);
        return await AdminCatalogReader.ServiceAsync(db, shops, service, cancellationToken);
    }
}

internal sealed class ListAdminPackagesHandler(TrimmeDbContext db, IAdminDataScope scope, IShopDirectory shops)
    : IQueryHandler<ListAdminPackagesQuery, PagedResponse<AdminPackageListItem>>
{
    public async Task<PagedResponse<AdminPackageListItem>> Handle(ListAdminPackagesQuery query, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var packages = db.Set<ServicePackage>().AsNoTracking().Include(p => p.Items).AsQueryable();
        if (query.ShopId is { } shop)
        {
            var shopId = new ShopId(shop);
            packages = packages.Where(p => p.ShopId == shopId);
        }

        packages = query.State switch
        {
            CatalogStateFilter.Active => packages.Where(p => p.IsActive && !p.IsArchived),
            CatalogStateFilter.Inactive => packages.Where(p => !p.IsActive && !p.IsArchived),
            CatalogStateFilter.Archived => packages.Where(p => p.IsArchived),
            CatalogStateFilter.Hidden => packages.Where(p => p.Moderation == ModerationState.Hidden),
            _ => packages,
        };

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = $"%{query.Search.Trim()}%";
            packages = packages.Where(p => EF.Functions.ILike(p.NameAr, term) || (p.NameEn != null && EF.Functions.ILike(p.NameEn, term)));
        }

        var total = await packages.CountAsync(cancellationToken);
        var page = await packages.OrderBy(p => p.ShopId).ThenBy(p => p.DisplayOrder).ThenBy(p => p.Id)
            .Skip(query.Page.Skip).Take(query.Page.PageSize).ToListAsync(cancellationToken);
        var names = await shops.FindManyAsync([.. page.Select(p => p.ShopId)], cancellationToken);
        return new PagedResponse<AdminPackageListItem>(
            [.. page.Select(p => AdminCatalogReader.Package(p, names.GetValueOrDefault(p.ShopId)))], query.Page.Page, query.Page.PageSize, total);
    }
}

internal sealed class ModeratePackageHandler(TrimmeDbContext db, IAdminDataScope scope, IShopDirectory shops, IAuditLog audit, TimeProvider clock)
    : ICommandHandler<ModeratePackageCommand, Result<AdminPackageListItem>>
{
    public async Task<Result<AdminPackageListItem>> Handle(ModeratePackageCommand command, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var id = new ServicePackageId(command.PackageId);
        if (await db.Set<ServicePackage>().Include(p => p.Items).SingleOrDefaultAsync(p => p.Id == id, cancellationToken) is not { } package)
        {
            return CatalogErrors.PackageNotFound();
        }

        var hide = command.Action == ModerationAction.Hide;
        package.Moderate(hide ? ModerationState.Hidden : ModerationState.Visible, command.Reason?.Trim(), clock.GetUtcNow());
        audit.Record(new AuditRecord(
            hide ? "package.hidden" : "package.unhidden", nameof(ServicePackage), package.Id.ToString(), package.ShopId,
            hide ? "Hidden from customers by the platform" : "Visible again", command.Reason?.Trim()));
        await db.SaveChangesAsync(cancellationToken);
        return AdminCatalogReader.Package(package, await shops.FindAsync(package.ShopId, cancellationToken));
    }
}

internal sealed class GetProfessionalServicesHandler(TrimmeDbContext db, IAdminDataScope scope, IProfessionalDirectory professionals)
    : IQueryHandler<GetProfessionalServicesQuery, ProfessionalServicesResponse?>
{
    public async Task<ProfessionalServicesResponse?> Handle(GetProfessionalServicesQuery query, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var professional = await professionals.FindAsync(new ProfessionalId(query.ProfessionalId), cancellationToken);
        return professional is null ? null : await ProfessionalServicesReader.ReadAsync(db, professional, cancellationToken);
    }
}

/// <summary>
/// Replaces a professional's services with services of the professional's own shop (spec §10: admin assigns).
/// Services of another shop are refused here, and the composite foreign keys refuse them in the database too.
/// </summary>
internal sealed class SetProfessionalServicesHandler(TrimmeDbContext db, IAdminDataScope scope, IProfessionalDirectory professionals, IAuditLog audit, TimeProvider clock)
    : ICommandHandler<SetProfessionalServicesCommand, Result<ProfessionalServicesResponse>>
{
    public async Task<Result<ProfessionalServicesResponse>> Handle(SetProfessionalServicesCommand command, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
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
            "professional.services_assigned", "Professional", professionalId.ToString(), professional.ShopId, $"{requested.Count} services assigned"));
        await db.SaveChangesAsync(cancellationToken);
        return await ProfessionalServicesReader.ReadAsync(db, professional, cancellationToken);
    }
}

internal static class ProfessionalServicesReader
{
    public static async Task<ProfessionalServicesResponse> ReadAsync(TrimmeDbContext db, ProfessionalSummary professional, CancellationToken cancellationToken)
    {
        var assigned = await db.Set<ProfessionalServiceAssignment>().AsNoTracking()
            .Where(a => a.ProfessionalId == professional.Id).Select(a => a.ServiceId).ToListAsync(cancellationToken);
        var services = await db.Set<ShopService>().AsNoTracking()
            .Where(s => s.ShopId == professional.ShopId && !s.IsArchived)
            .OrderBy(s => s.DisplayOrder).ToListAsync(cancellationToken);
        return new ProfessionalServicesResponse(
            professional.Id.Value,
            professional.ShopId.Value,
            [.. services.Select(s => new ProfessionalServiceOption(s.Id.Value, s.NameAr, s.NameEn, s.Price, s.Currency, s.DurationMinutes, s.IsActive, assigned.Contains(s.Id)))]);
    }
}
