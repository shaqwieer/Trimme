using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.Modules.Services.Domain;

public readonly record struct ServiceCategoryId(Guid Value) : IEntityId<ServiceCategoryId>
{
    public static ServiceCategoryId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct ShopServiceId(Guid Value) : IEntityId<ShopServiceId>
{
    public static ShopServiceId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct ServicePackageId(Guid Value) : IEntityId<ServicePackageId>
{
    public static ServicePackageId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

/// <summary>Platform moderation of a shop's service or package (DV-S02). A hidden item is never published.</summary>
public enum ModerationState
{
    Visible,
    Hidden,
}

/// <summary>Shared limits for prices and durations (D-071).</summary>
public static class CatalogRules
{
    public const string Currency = "SAR";
    public const decimal MaxPrice = 100_000m;
    public const int MinDuration = 5;
    public const int MaxDuration = 480;
    public const int DurationStep = 5;
    public const int MaxNameLength = 120;
    public const int MaxDescriptionLength = 500;
    public const int MinPackageItems = 2;
    public const int MaxPackageItems = 10;

    /// <summary>0 to 100,000 SAR with at most two decimal places; never rounded.</summary>
    public static bool IsValidPrice(decimal price) => price is >= 0 and <= MaxPrice && decimal.Round(price, 2) == price;

    /// <summary>A multiple of 5 minutes between 5 minutes and 8 hours (supports 8:05, 8:10… slot steps).</summary>
    public static bool IsValidDuration(int minutes) => minutes is >= MinDuration and <= MaxDuration && minutes % DurationStep == 0;
}

/// <summary>
/// A platform-level category (hair, beard, care…) that shops file their services under. Platform-owned: it carries no
/// price or duration (spec §10). Both languages are required, as admins manage a short list (D-070).
/// </summary>
public sealed class ServiceCategory : AggregateRoot<ServiceCategoryId>, IPublicContent
{
    /// <summary>Design icon keys a category may use (icons.tsx).</summary>
    public static readonly IReadOnlyList<string> Icons = ["scissors", "user", "users", "star", "heart", "layers", "tag", "coffee"];

    private ServiceCategory(ServiceCategoryId id, string nameAr, string nameEn, string icon, int displayOrder, DateTimeOffset now)
        : base(id)
    {
        NameAr = nameAr;
        NameEn = nameEn;
        Icon = icon;
        DisplayOrder = displayOrder;
        IsActive = true;
        CreatedAt = now;
    }

    private ServiceCategory()
    {
        NameAr = NameEn = Icon = string.Empty;
    }

    public string NameAr { get; private set; }

    public string NameEn { get; private set; }

    public string Icon { get; private set; }

    public int DisplayOrder { get; private set; }

    /// <summary>Inactive categories cannot be chosen for new or changed services; existing services keep them.</summary>
    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public static ServiceCategory Create(ServiceCategoryId id, string nameAr, string nameEn, string icon, int displayOrder, DateTimeOffset now) =>
        new(id, nameAr.Trim(), nameEn.Trim(), icon, displayOrder, now);

    public void Update(string nameAr, string nameEn, string icon, int displayOrder, DateTimeOffset now)
    {
        NameAr = nameAr.Trim();
        NameEn = nameEn.Trim();
        Icon = icon;
        DisplayOrder = displayOrder;
        UpdatedAt = now;
    }

    public void SetActive(bool active, DateTimeOffset now)
    {
        IsActive = active;
        UpdatedAt = now;
    }
}

/// <summary>The editable details of a service or package, normalized (trimmed; empty optional text → null).</summary>
public sealed record CatalogText(string NameAr, string? NameEn, string? DescriptionAr, string? DescriptionEn)
{
    public static CatalogText Create(string nameAr, string? nameEn, string? descriptionAr, string? descriptionEn) =>
        new(nameAr.Trim(), Clean(nameEn), Clean(descriptionAr), Clean(descriptionEn));

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// A service one shop offers (spec §10): the shop sets its own names, price and duration; there is no global price
/// (DV-S02/S03). It is never physically deleted once used, only archived (R-SVC-02). Admins can hide it (moderation)
/// or correct it through an audited support override; neither changes who owns it.
/// </summary>
public sealed class ShopService : AggregateRoot<ShopServiceId>, IShopOwned, IConcurrencyVersioned, IPublicContent
{
    private ShopService(ShopServiceId id, ShopId shopId, CatalogText text, ServiceCategoryId? categoryId, decimal price, int durationMinutes, bool onlineBookable, int displayOrder, DateTimeOffset now)
        : base(id)
    {
        ShopId = shopId;
        Apply(text, categoryId, price, durationMinutes, onlineBookable);
        NameAr = text.NameAr;
        Currency = CatalogRules.Currency;
        DisplayOrder = displayOrder;
        IsActive = true;
        Moderation = ModerationState.Visible;
        CreatedAt = now;
    }

    private ShopService()
    {
        NameAr = Currency = string.Empty;
    }

    /// <summary>Stamped from the signed-in shop on insert (or set explicitly by the dev seed).</summary>
    public ShopId ShopId { get; private set; }

    public string NameAr { get; private set; }

    /// <summary>Optional; clients fall back to <see cref="NameAr"/> (D-070).</summary>
    public string? NameEn { get; private set; }

    public string? DescriptionAr { get; private set; }

    public string? DescriptionEn { get; private set; }

    public ServiceCategoryId? CategoryId { get; private set; }

    public decimal Price { get; private set; }

    /// <summary>Stored with the price so bookings can snapshot both (Phase 10).</summary>
    public string Currency { get; private set; }

    public int DurationMinutes { get; private set; }

    /// <summary>Booking rule: customers can book it online (walk-ins are always possible).</summary>
    public bool OnlineBookable { get; private set; }

    public bool IsActive { get; private set; }

    public bool IsArchived { get; private set; }

    public int DisplayOrder { get; private set; }

    public ModerationState Moderation { get; private set; }

    public string? ModerationReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    public uint Version { get; private set; }

    /// <summary>Offered to customers: active, not archived and not hidden by the platform.</summary>
    public bool IsPubliclyAvailable => IsActive && !IsArchived && Moderation == ModerationState.Visible;

    public static Result<ShopService> Create(
        ShopServiceId id, ShopId shopId, CatalogText text, ServiceCategoryId? categoryId, decimal price, int durationMinutes, bool onlineBookable, int displayOrder, DateTimeOffset now)
    {
        var invalid = CatalogErrors.Validate(price, durationMinutes);
        return invalid is not null
            ? invalid
            : new ShopService(id, shopId, text, categoryId, price, durationMinutes, onlineBookable, displayOrder, now);
    }

    public Result Update(CatalogText text, ServiceCategoryId? categoryId, decimal price, int durationMinutes, bool onlineBookable, DateTimeOffset now)
    {
        if (IsArchived)
        {
            return CatalogErrors.Archived();
        }

        if (CatalogErrors.Validate(price, durationMinutes) is { } invalid)
        {
            return invalid;
        }

        Apply(text, categoryId, price, durationMinutes, onlineBookable);
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>Turning a service on or off is idempotent; an archived service stays off.</summary>
    public Result SetActive(bool active, DateTimeOffset now)
    {
        if (IsArchived)
        {
            return CatalogErrors.Archived();
        }

        IsActive = active;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>Retires the service for good: it stays in history and reports, never offered again.</summary>
    public Result Archive(DateTimeOffset now)
    {
        if (IsArchived)
        {
            return CatalogErrors.Archived();
        }

        IsArchived = true;
        IsActive = false;
        ArchivedAt = now;
        UpdatedAt = now;
        return Result.Success();
    }

    public void MoveTo(int displayOrder) => DisplayOrder = displayOrder;

    public void Moderate(ModerationState state, string? reason, DateTimeOffset now)
    {
        Moderation = state;
        ModerationReason = state == ModerationState.Hidden ? reason : null;
        UpdatedAt = now;
    }

    private void Apply(CatalogText text, ServiceCategoryId? categoryId, decimal price, int durationMinutes, bool onlineBookable)
    {
        NameAr = text.NameAr;
        NameEn = text.NameEn;
        DescriptionAr = text.DescriptionAr;
        DescriptionEn = text.DescriptionEn;
        CategoryId = categoryId;
        Price = price;
        DurationMinutes = durationMinutes;
        OnlineBookable = onlineBookable;
    }
}

/// <summary>
/// A package (D-020): a shop-owned bundle of its own services with an explicit price and total duration, booked as one
/// contiguous appointment. Items reference services of the same shop through composite keys, so the database rejects a
/// package built from another shop's services.
/// </summary>
public sealed class ServicePackage : AggregateRoot<ServicePackageId>, IShopOwned, IConcurrencyVersioned, IPublicContent
{
    private readonly List<ServicePackageItem> _items = [];

    private ServicePackage(ServicePackageId id, ShopId shopId, CatalogText text, decimal price, int durationMinutes, int displayOrder, DateTimeOffset now)
        : base(id)
    {
        ShopId = shopId;
        NameAr = text.NameAr;
        SetText(text);
        Price = price;
        Currency = CatalogRules.Currency;
        DurationMinutes = durationMinutes;
        DisplayOrder = displayOrder;
        IsActive = true;
        Moderation = ModerationState.Visible;
        CreatedAt = now;
    }

    private ServicePackage()
    {
        NameAr = Currency = string.Empty;
    }

    public ShopId ShopId { get; private set; }

    public string NameAr { get; private set; }

    public string? NameEn { get; private set; }

    public string? DescriptionAr { get; private set; }

    public string? DescriptionEn { get; private set; }

    public decimal Price { get; private set; }

    public string Currency { get; private set; }

    /// <summary>Explicit total duration, set by the shop (not the sum of its items).</summary>
    public int DurationMinutes { get; private set; }

    public bool IsActive { get; private set; }

    public bool IsArchived { get; private set; }

    public int DisplayOrder { get; private set; }

    public ModerationState Moderation { get; private set; }

    public string? ModerationReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    public uint Version { get; private set; }

    public IReadOnlyList<ServicePackageItem> Items => _items;

    public static Result<ServicePackage> Create(
        ServicePackageId id, ShopId shopId, CatalogText text, decimal price, int durationMinutes, IReadOnlyList<ShopServiceId> serviceIds, int displayOrder, DateTimeOffset now)
    {
        if (CatalogErrors.Validate(price, durationMinutes) is { } invalid)
        {
            return invalid;
        }

        var package = new ServicePackage(id, shopId, text, price, durationMinutes, displayOrder, now);
        var items = package.ReplaceItems(serviceIds, now);
        return items.IsFailure ? items.Error : package;
    }

    public Result Update(CatalogText text, decimal price, int durationMinutes, IReadOnlyList<ShopServiceId> serviceIds, DateTimeOffset now)
    {
        if (IsArchived)
        {
            return CatalogErrors.Archived();
        }

        if (CatalogErrors.Validate(price, durationMinutes) is { } invalid)
        {
            return invalid;
        }

        var items = ReplaceItems(serviceIds, now);
        if (items.IsFailure)
        {
            return items;
        }

        SetText(text);
        Price = price;
        DurationMinutes = durationMinutes;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Sets the item services in order (2–10, distinct). Always touches <see cref="UpdatedAt"/>, so an items-only edit
    /// still updates the package row and its optimistic-concurrency check runs.
    /// </summary>
    public Result ReplaceItems(IReadOnlyList<ShopServiceId> serviceIds, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(serviceIds);
        if (serviceIds.Count is < CatalogRules.MinPackageItems or > CatalogRules.MaxPackageItems || serviceIds.Distinct().Count() != serviceIds.Count)
        {
            return CatalogErrors.InvalidItems();
        }

        // Keep the rows of services that stay (their key cannot be deleted and re-added in one unit of work).
        _items.RemoveAll(item => !serviceIds.Contains(item.ServiceId));
        for (var position = 0; position < serviceIds.Count; position++)
        {
            var existing = _items.FirstOrDefault(item => item.ServiceId == serviceIds[position]);
            if (existing is null)
            {
                _items.Add(new ServicePackageItem(Id, ShopId, serviceIds[position], position));
            }
            else
            {
                existing.MoveTo(position);
            }
        }

        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>The package's services in order, for reporting and availability (R-SVC-05).</summary>
    public IReadOnlyList<ShopServiceId> ExpandItems() => [.. _items.OrderBy(i => i.Position).Select(i => i.ServiceId)];

    public Result SetActive(bool active, DateTimeOffset now)
    {
        if (IsArchived)
        {
            return CatalogErrors.Archived();
        }

        IsActive = active;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result Archive(DateTimeOffset now)
    {
        if (IsArchived)
        {
            return CatalogErrors.Archived();
        }

        IsArchived = true;
        IsActive = false;
        ArchivedAt = now;
        UpdatedAt = now;
        return Result.Success();
    }

    public void MoveTo(int displayOrder) => DisplayOrder = displayOrder;

    public void Moderate(ModerationState state, string? reason, DateTimeOffset now)
    {
        Moderation = state;
        ModerationReason = state == ModerationState.Hidden ? reason : null;
        UpdatedAt = now;
    }

    private void SetText(CatalogText text)
    {
        NameAr = text.NameAr;
        NameEn = text.NameEn;
        DescriptionAr = text.DescriptionAr;
        DescriptionEn = text.DescriptionEn;
    }
}

/// <summary>One service in a package, in order. Same shop as the package and the service (composite keys).</summary>
public sealed class ServicePackageItem : IShopOwned
{
    internal ServicePackageItem(ServicePackageId packageId, ShopId shopId, ShopServiceId serviceId, int position)
    {
        PackageId = packageId;
        ShopId = shopId;
        ServiceId = serviceId;
        Position = position;
    }

    private ServicePackageItem()
    {
    }

    public ServicePackageId PackageId { get; private set; }

    public ShopId ShopId { get; private set; }

    public ShopServiceId ServiceId { get; private set; }

    public int Position { get; private set; }

    internal void MoveTo(int position) => Position = position;
}

/// <summary>
/// A professional assigned to one of their own shop's services (spec §10: the platform admin assigns). Both
/// references include the shop id, so the database rejects any cross-shop pairing (R-NEG-06, D-073).
/// </summary>
public sealed class ProfessionalServiceAssignment : IShopOwned, IPublicContent
{
    public ProfessionalServiceAssignment(ShopId shopId, ProfessionalId professionalId, ShopServiceId serviceId, DateTimeOffset assignedAt)
    {
        ShopId = shopId;
        ProfessionalId = professionalId;
        ServiceId = serviceId;
        AssignedAt = assignedAt;
    }

    private ProfessionalServiceAssignment()
    {
    }

    public ShopId ShopId { get; private set; }

    public ProfessionalId ProfessionalId { get; private set; }

    public ShopServiceId ServiceId { get; private set; }

    public DateTimeOffset AssignedAt { get; private set; }
}

public static class CatalogErrors
{
    public static Error ServiceNotFound() => Error.NotFound("service.not_found", "The service was not found.");

    public static Error PackageNotFound() => Error.NotFound("package.not_found", "The package was not found.");

    public static Error CategoryNotFound() => Error.NotFound("service_category.not_found", "The category was not found.");

    public static Error Archived() => Error.Conflict("catalog.archived", "An archived item cannot be changed.");

    public static Error InUse() => Error.Conflict("service.in_use", "The service is used (package or booking history); archive it instead.");

    public static Error InvalidItems() => Field("serviceIds", "validation.package_items");

    public static Error ItemsNotInShop() => Field("serviceIds", "validation.invalid");

    public static Error InvalidCategory() => Field("categoryId", "validation.invalid");

    public static Error OrderMismatch() => Field("orderedIds", "validation.order_mismatch");

    public static Error? Validate(decimal price, int durationMinutes)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!CatalogRules.IsValidPrice(price))
        {
            errors["price"] = ["validation.price_invalid"];
        }

        if (!CatalogRules.IsValidDuration(durationMinutes))
        {
            errors["durationMinutes"] = ["validation.duration_invalid"];
        }

        return errors.Count == 0 ? null : Error.Validation("validation.failed", "The price or duration is invalid.", errors);
    }

    private static Error Field(string field, string code) =>
        Error.Validation("validation.failed", "The request is invalid.",
            new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [code] });
}
