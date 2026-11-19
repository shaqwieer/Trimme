using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Auditing;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Services.Domain;

namespace Trimme.Modules.Services.Application;

// A shop's own sections of its services (D-149, the client's «أقسام للخدمات: مثل الشعر، الدقن، والعناية، ويحدد ترتيب
// ظهورها»). The owner adds, renames, orders and deletes them (Shop.Services.Manage), and the admin does the same from the
// shop's page; the shop page and the booking show the services in tabs in this order, «أخرى» for services without one,
// packages last. Deleting a section leaves its services without one. They replace the platform's categories on the
// shop's pages; the categories stay for the platform (search filter, reports) until they are retired.

/// <summary>One of the shop's sections, with how many of its (non-archived) services sit in it.</summary>
public sealed record ServiceSectionResponse(Guid Id, string NameAr, string? NameEn, int DisplayOrder, int ServiceCount);

/// <summary>A section's names; the English one is optional (the Arabic one is shown instead).</summary>
public sealed record ServiceSectionRequest(string NameAr, string? NameEn);

/// <summary>A section as customers see it on the shop page and in the booking (D-149).</summary>
public sealed record PublicServiceSectionResponse(Guid Id, string NameAr, string? NameEn);

internal interface IServiceSectionFields
{
    string NameAr { get; }

    string? NameEn { get; }
}

internal sealed record ListOwnServiceSectionsQuery : IQuery<IReadOnlyList<ServiceSectionResponse>?>;

internal sealed record CreateOwnServiceSectionCommand(string NameAr, string? NameEn) : ICommand<Result<ServiceSectionResponse>>, IServiceSectionFields;

internal sealed record RenameOwnServiceSectionCommand(Guid SectionId, string NameAr, string? NameEn) : ICommand<Result<ServiceSectionResponse>>, IServiceSectionFields;

internal sealed record DeleteOwnServiceSectionCommand(Guid SectionId) : ICommand<Result>;

internal sealed record OrderOwnServiceSectionsCommand(IReadOnlyList<Guid> OrderedIds) : ICommand<Result<IReadOnlyList<ServiceSectionResponse>>>;

internal sealed class ServiceSectionRules<T> : AbstractValidator<T>
    where T : IServiceSectionFields
{
    public ServiceSectionRules()
    {
        RuleFor(s => s.NameAr).Must(n => !string.IsNullOrWhiteSpace(n)).WithErrorCode("validation.required")
            .MaximumLength(ServiceSection.MaxNameLength).WithErrorCode("validation.too_long");
        RuleFor(s => s.NameEn).MaximumLength(ServiceSection.MaxNameLength).WithErrorCode("validation.too_long");
    }
}

internal sealed class CreateOwnServiceSectionValidator : AbstractValidator<CreateOwnServiceSectionCommand>
{
    public CreateOwnServiceSectionValidator() => Include(new ServiceSectionRules<CreateOwnServiceSectionCommand>());
}

internal sealed class RenameOwnServiceSectionValidator : AbstractValidator<RenameOwnServiceSectionCommand>
{
    public RenameOwnServiceSectionValidator() => Include(new ServiceSectionRules<RenameOwnServiceSectionCommand>());
}

/// <summary>The section work for one shop, through the caller's scope (the owner's tenant, or the admin's scope).</summary>
internal sealed class ServiceSectionStore(TrimmeDbContext db, IAuditLog audit, TimeProvider clock)
{
    public async Task<IReadOnlyList<ServiceSectionResponse>> ListAsync(ShopId shopId, CancellationToken cancellationToken)
    {
        var sections = await db.Set<ServiceSection>().AsNoTracking()
            .Where(s => s.ShopId == shopId).OrderBy(s => s.DisplayOrder).ThenBy(s => s.CreatedAt).ToListAsync(cancellationToken);
        var counts = await db.Set<ShopService>().AsNoTracking()
            .Where(s => s.ShopId == shopId && !s.IsArchived && s.SectionId != null)
            .GroupBy(s => s.SectionId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key!.Value, g => g.Count, cancellationToken);
        return [.. sections.Select(s => new ServiceSectionResponse(s.Id.Value, s.NameAr, s.NameEn, s.DisplayOrder, counts.GetValueOrDefault(s.Id)))];
    }

    public async Task<Result<ServiceSectionResponse>> CreateAsync(ShopId shopId, IServiceSectionFields fields, CancellationToken cancellationToken)
    {
        var existing = await db.Set<ServiceSection>().Where(s => s.ShopId == shopId).Select(s => s.DisplayOrder).ToListAsync(cancellationToken);
        if (existing.Count >= ServiceSection.MaxSections)
        {
            return CatalogErrors.TooManySections();
        }

        var section = ServiceSection.Create(
            EntityId.New<ServiceSectionId>(), shopId, fields.NameAr, fields.NameEn, existing.Count == 0 ? 1 : existing.Max() + 1, clock.GetUtcNow());
        db.Add(section);
        audit.Record(new AuditRecord("service_section.created", nameof(ServiceSection), section.Id.ToString(), shopId, section.NameAr,
            SummaryArgs: AuditArgs.Of(("nameAr", section.NameAr), ("nameEn", section.NameEn))));
        await db.SaveChangesAsync(cancellationToken);
        return new ServiceSectionResponse(section.Id.Value, section.NameAr, section.NameEn, section.DisplayOrder, 0);
    }

    public async Task<Result<ServiceSectionResponse>> RenameAsync(ShopId shopId, Guid sectionId, IServiceSectionFields fields, CancellationToken cancellationToken)
    {
        var id = new ServiceSectionId(sectionId);
        if (await db.Set<ServiceSection>().SingleOrDefaultAsync(s => s.Id == id && s.ShopId == shopId, cancellationToken) is not { } section)
        {
            return CatalogErrors.SectionNotFound();
        }

        section.Rename(fields.NameAr, fields.NameEn, clock.GetUtcNow());
        audit.Record(new AuditRecord("service_section.renamed", nameof(ServiceSection), section.Id.ToString(), shopId, section.NameAr,
            SummaryArgs: AuditArgs.Of(("nameAr", section.NameAr), ("nameEn", section.NameEn))));
        await db.SaveChangesAsync(cancellationToken);
        var count = await db.Set<ShopService>().CountAsync(s => s.ShopId == shopId && s.SectionId == id && !s.IsArchived, cancellationToken);
        return new ServiceSectionResponse(section.Id.Value, section.NameAr, section.NameEn, section.DisplayOrder, count);
    }

    /// <summary>Deletes the section; its services stay, without a section («أخرى»).</summary>
    public async Task<Result> DeleteAsync(ShopId shopId, Guid sectionId, CancellationToken cancellationToken)
    {
        var id = new ServiceSectionId(sectionId);
        if (await db.Set<ServiceSection>().SingleOrDefaultAsync(s => s.Id == id && s.ShopId == shopId, cancellationToken) is not { } section)
        {
            return CatalogErrors.SectionNotFound();
        }

        var now = clock.GetUtcNow();
        foreach (var service in await db.Set<ShopService>().Where(s => s.ShopId == shopId && s.SectionId == id).ToListAsync(cancellationToken))
        {
            service.PlaceIn(null, now);
        }

        db.Remove(section);
        audit.Record(new AuditRecord("service_section.deleted", nameof(ServiceSection), section.Id.ToString(), shopId, section.NameAr,
            SummaryArgs: AuditArgs.Of(("nameAr", section.NameAr), ("nameEn", section.NameEn))));
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>The new order, from the full list of the shop's sections.</summary>
    public async Task<Result<IReadOnlyList<ServiceSectionResponse>>> OrderAsync(ShopId shopId, IReadOnlyList<Guid> orderedIds, CancellationToken cancellationToken)
    {
        var sections = await db.Set<ServiceSection>().Where(s => s.ShopId == shopId).ToListAsync(cancellationToken);
        if (orderedIds.Count != sections.Count || orderedIds.Distinct().Count() != orderedIds.Count
            || !sections.All(s => orderedIds.Contains(s.Id.Value)))
        {
            return CatalogErrors.OrderMismatch();
        }

        for (var i = 0; i < orderedIds.Count; i++)
        {
            sections.Single(s => s.Id.Value == orderedIds[i]).MoveTo(i + 1);
        }

        audit.Record(new AuditRecord("service_section.ordered", nameof(ServiceSection), shopId.ToString(), shopId, $"{sections.Count} sections",
            SummaryArgs: AuditArgs.Of(("count", sections.Count))));
        await db.SaveChangesAsync(cancellationToken);
        return Result<IReadOnlyList<ServiceSectionResponse>>.Success(await ListAsync(shopId, cancellationToken));
    }
}

/// <summary>A service's section, which must be one of its own shop's (D-149); <see langword="null"/> is none.</summary>
internal static class ServiceSections
{
    public static async Task<Result<ServiceSectionId?>> ResolveAsync(TrimmeDbContext db, ShopId shopId, Guid? requested, CancellationToken cancellationToken)
    {
        if (requested is not { } value)
        {
            return (ServiceSectionId?)null;
        }

        var id = new ServiceSectionId(value);
        return await db.Set<ServiceSection>().AnyAsync(s => s.Id == id && s.ShopId == shopId, cancellationToken)
            ? (ServiceSectionId?)id
            : CatalogErrors.InvalidSection();
    }
}

internal sealed class ListOwnServiceSectionsHandler(ICurrentTenant tenant, ServiceSectionStore store)
    : IQueryHandler<ListOwnServiceSectionsQuery, IReadOnlyList<ServiceSectionResponse>?>
{
    public async Task<IReadOnlyList<ServiceSectionResponse>?> Handle(ListOwnServiceSectionsQuery query, CancellationToken cancellationToken) =>
        tenant.ShopId is { } shopId ? await store.ListAsync(shopId, cancellationToken) : null;
}

internal sealed class CreateOwnServiceSectionHandler(ICurrentTenant tenant, ServiceSectionStore store)
    : ICommandHandler<CreateOwnServiceSectionCommand, Result<ServiceSectionResponse>>
{
    public async Task<Result<ServiceSectionResponse>> Handle(CreateOwnServiceSectionCommand command, CancellationToken cancellationToken) =>
        tenant.ShopId is { } shopId ? await store.CreateAsync(shopId, command, cancellationToken) : CatalogErrors.SectionNotFound();
}

internal sealed class RenameOwnServiceSectionHandler(ICurrentTenant tenant, ServiceSectionStore store)
    : ICommandHandler<RenameOwnServiceSectionCommand, Result<ServiceSectionResponse>>
{
    public async Task<Result<ServiceSectionResponse>> Handle(RenameOwnServiceSectionCommand command, CancellationToken cancellationToken) =>
        tenant.ShopId is { } shopId ? await store.RenameAsync(shopId, command.SectionId, command, cancellationToken) : CatalogErrors.SectionNotFound();
}

internal sealed class DeleteOwnServiceSectionHandler(ICurrentTenant tenant, ServiceSectionStore store) : ICommandHandler<DeleteOwnServiceSectionCommand, Result>
{
    public async Task<Result> Handle(DeleteOwnServiceSectionCommand command, CancellationToken cancellationToken) =>
        tenant.ShopId is { } shopId ? await store.DeleteAsync(shopId, command.SectionId, cancellationToken) : CatalogErrors.SectionNotFound();
}

internal sealed class OrderOwnServiceSectionsHandler(ICurrentTenant tenant, ServiceSectionStore store)
    : ICommandHandler<OrderOwnServiceSectionsCommand, Result<IReadOnlyList<ServiceSectionResponse>>>
{
    public async Task<Result<IReadOnlyList<ServiceSectionResponse>>> Handle(OrderOwnServiceSectionsCommand command, CancellationToken cancellationToken) =>
        tenant.ShopId is { } shopId ? await store.OrderAsync(shopId, command.OrderedIds, cancellationToken) : CatalogErrors.SectionNotFound();
}
