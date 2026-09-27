using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Services.Domain;

namespace Trimme.Modules.Services.Application;

/// <summary>
/// A shop's own service as the shop manages it. Shop-facing contract: no customer data. <c>NameEn</c> may be null;
/// clients show the Arabic name instead (D-070).
/// </summary>
public sealed record ShopServiceResponse(
    Guid Id,
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
    int DisplayOrder,
    ModerationState Moderation,
    string? ModerationReason,
    int AssignedProfessionalCount,
    uint Version);

public sealed record PackageItemResponse(Guid ServiceId, string NameAr, string? NameEn, bool IsAvailable);

/// <summary>A shop's own package. <c>IsBookable</c> is false while any item service is off, archived or hidden.</summary>
public sealed record ShopPackageResponse(
    Guid Id,
    string NameAr,
    string? NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    decimal Price,
    string Currency,
    int DurationMinutes,
    bool IsActive,
    bool IsArchived,
    int DisplayOrder,
    ModerationState Moderation,
    string? ModerationReason,
    IReadOnlyList<PackageItemResponse> Items,
    bool IsBookable,
    uint Version);

public sealed record ServiceCategoryResponse(Guid Id, string NameAr, string NameEn, string Icon, int DisplayOrder, bool IsActive);

/// <summary>The text of a service or package; Arabic required, English optional (D-070).</summary>
public interface ICatalogTextFields
{
    string NameAr { get; }

    string? NameEn { get; }

    string? DescriptionAr { get; }

    string? DescriptionEn { get; }
}

internal sealed class CatalogTextRules<T> : AbstractValidator<T>
    where T : ICatalogTextFields
{
    public CatalogTextRules()
    {
        RuleFor(c => c.NameAr).Must(n => !string.IsNullOrWhiteSpace(n)).WithErrorCode("validation.required")
            .MaximumLength(CatalogRules.MaxNameLength).WithErrorCode("validation.too_long");
        RuleFor(c => c.NameEn).MaximumLength(CatalogRules.MaxNameLength).WithErrorCode("validation.too_long");
        RuleFor(c => c.DescriptionAr).MaximumLength(CatalogRules.MaxDescriptionLength).WithErrorCode("validation.too_long");
        RuleFor(c => c.DescriptionEn).MaximumLength(CatalogRules.MaxDescriptionLength).WithErrorCode("validation.too_long");
    }
}

internal static class CatalogMapping
{
    public static CatalogText Text(ICatalogTextFields fields) =>
        CatalogText.Create(fields.NameAr, fields.NameEn, fields.DescriptionAr, fields.DescriptionEn);

    public static ShopServiceResponse ToResponse(ShopService service, int assignedProfessionals) => new(
        service.Id.Value,
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
        service.DisplayOrder,
        service.Moderation,
        service.ModerationReason,
        assignedProfessionals,
        service.Version);

    public static ShopPackageResponse ToResponse(ServicePackage package, IReadOnlyDictionary<ShopServiceId, ShopService> services)
    {
        var items = package.ExpandItems()
            .Select(id => services.TryGetValue(id, out var service)
                ? new PackageItemResponse(id.Value, service.NameAr, service.NameEn, service.IsPubliclyAvailable)
                : new PackageItemResponse(id.Value, string.Empty, null, false))
            .ToList();
        var bookable = package.IsActive && !package.IsArchived && package.Moderation == ModerationState.Visible && items.All(i => i.IsAvailable);
        return new ShopPackageResponse(
            package.Id.Value,
            package.NameAr,
            package.NameEn,
            package.DescriptionAr,
            package.DescriptionEn,
            package.Price,
            package.Currency,
            package.DurationMinutes,
            package.IsActive,
            package.IsArchived,
            package.DisplayOrder,
            package.Moderation,
            package.ModerationReason,
            items,
            bookable,
            package.Version);
    }

    public static ServiceCategoryResponse ToResponse(ServiceCategory category) =>
        new(category.Id.Value, category.NameAr, category.NameEn, category.Icon, category.DisplayOrder, category.IsActive);

    /// <summary>The services a set of packages uses, loaded in one query (reads through the caller's scope).</summary>
    public static async Task<Dictionary<ShopServiceId, ShopService>> ServicesOfAsync(
        TrimmeDbContext db, IEnumerable<ServicePackage> packages, CancellationToken cancellationToken)
    {
        var ids = packages.SelectMany(p => p.ExpandItems()).Distinct().ToArray();
        return ids.Length == 0
            ? []
            : await db.Set<ShopService>().AsNoTracking().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, cancellationToken);
    }

    /// <summary>A category may be set only if it exists and is active; keeping an existing (now inactive) one is fine.</summary>
    public static async Task<Result<ServiceCategoryId?>> CategoryAsync(
        TrimmeDbContext db, Guid? requested, ServiceCategoryId? current, CancellationToken cancellationToken)
    {
        if (requested is not { } value)
        {
            return (ServiceCategoryId?)null;
        }

        var id = new ServiceCategoryId(value);
        if (id == current)
        {
            return (ServiceCategoryId?)id;
        }

        return await db.Set<ServiceCategory>().AnyAsync(c => c.Id == id && c.IsActive, cancellationToken)
            ? (ServiceCategoryId?)id
            : CatalogErrors.InvalidCategory();
    }
}
