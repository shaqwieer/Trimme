using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Auditing;
using Trimme.BuildingBlocks.Application.Media;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Paging;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Shops.Domain;

namespace Trimme.Modules.Shops.Application.Admin;

/// <summary>A shop in the admin list.</summary>
public sealed record AdminShopResponse(
    Guid Id,
    string Slug,
    string NameAr,
    string NameEn,
    string Status,
    string TimeZone,
    bool RequireManualConfirmation,
    string? District,
    bool IsVerified,
    string? LogoUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>A shop with its full profile, location and shop-edit policy, as the platform admin edits it.</summary>
public sealed record AdminShopDetailResponse(
    Guid Id,
    string Slug,
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    ShopCategory Category,
    string? PublicPhone,
    IReadOnlyList<ShopAmenity> Amenities,
    bool IsVerified,
    string? LogoUrl,
    string? CoverUrl,
    IReadOnlyList<ShopImageResponse> Gallery,
    ShopLocationResponse? Location,
    IReadOnlyList<ShopProfileField> EditableFields,
    string Status,
    string TimeZone,
    bool RequireManualConfirmation,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    uint Version);

internal sealed record CreateShopCommand(string Slug, string NameAr, string NameEn, string? TimeZone) : ICommand<Result<AdminShopResponse>>;

internal sealed record ListShopsQuery(PageRequest Page, string? Search, ShopStatus? Status) : IQuery<PagedResponse<AdminShopResponse>>;

internal sealed record GetShopQuery(Guid ShopId) : IQuery<AdminShopDetailResponse?>;

internal enum ShopStatusChange
{
    Activate,
    Suspend,
}

internal sealed record ChangeShopStatusCommand(Guid ShopId, ShopStatusChange Change, string? Reason) : ICommand<Result<AdminShopResponse>>;

internal sealed record UpdateShopProfileCommand(
    Guid ShopId,
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    ShopCategory Category,
    string? PublicPhone,
    IReadOnlyList<ShopAmenity>? Amenities,
    bool IsVerified,
    uint Version) : ICommand<Result<AdminShopDetailResponse>>, IShopProfileFields;

internal sealed record SetShopLocationCommand(
    Guid ShopId,
    double Latitude,
    double Longitude,
    string? AddressLine,
    string? District,
    string? City,
    string? FormattedAddress,
    LocationSource Source) : ICommand<Result<AdminShopDetailResponse>>, IShopLocationFields;

internal sealed record SetEditablePolicyCommand(Guid ShopId, IReadOnlyList<ShopProfileField> EditableFields) : ICommand<Result<AdminShopDetailResponse>>;

/// <summary>Replaces the logo or cover; a null <paramref name="Content"/> removes it.</summary>
internal sealed record ReplaceShopImageCommand(Guid ShopId, ShopImageSlot Slot, byte[]? Content) : ICommand<Result<AdminShopDetailResponse>>;

internal sealed record AddShopGalleryImageCommand(Guid ShopId, byte[] Content) : ICommand<Result<AdminShopDetailResponse>>;

internal sealed record RemoveShopGalleryImageCommand(Guid ShopId, Guid MediaId) : ICommand<Result<AdminShopDetailResponse>>;

internal sealed class CreateShopValidator : AbstractValidator<CreateShopCommand>
{
    public CreateShopValidator()
    {
        RuleFor(c => c.Slug).NotEmpty().WithErrorCode("validation.required")
            .Must(s => Shop.IsValidSlug(s.Trim().ToLowerInvariant())).WithErrorCode("validation.slug_invalid");
        RuleFor(c => c.NameAr).Must(n => !string.IsNullOrWhiteSpace(n)).WithErrorCode("validation.required")
            .MaximumLength(Shop.MaxNameLength).WithErrorCode("validation.too_long");
        RuleFor(c => c.NameEn).Must(n => !string.IsNullOrWhiteSpace(n)).WithErrorCode("validation.required")
            .MaximumLength(Shop.MaxNameLength).WithErrorCode("validation.too_long");
        RuleFor(c => c.TimeZone)
            .Must(tz => tz is null || TimeZoneInfo.TryFindSystemTimeZoneById(tz, out _)).WithErrorCode("validation.invalid");
    }
}

internal sealed class ChangeShopStatusValidator : AbstractValidator<ChangeShopStatusCommand>
{
    public ChangeShopStatusValidator()
    {
        RuleFor(c => c.Reason).MaximumLength(500).WithErrorCode("validation.too_long");
    }
}

internal sealed class UpdateShopProfileValidator : AbstractValidator<UpdateShopProfileCommand>
{
    public UpdateShopProfileValidator() => Include(new ShopProfileRules<UpdateShopProfileCommand>());
}

internal sealed class SetShopLocationValidator : AbstractValidator<SetShopLocationCommand>
{
    public SetShopLocationValidator() => Include(new ShopLocationRules<SetShopLocationCommand>());
}

internal sealed class SetEditablePolicyValidator : AbstractValidator<SetEditablePolicyCommand>
{
    public SetEditablePolicyValidator()
    {
        RuleFor(c => c.EditableFields).NotNull().WithErrorCode("validation.required");
        RuleForEach(c => c.EditableFields).IsInEnum().WithErrorCode("validation.invalid");
    }
}

internal sealed class CreateShopHandler(TrimmeDbContext db, TimeProvider clock, IAuditLog audit)
    : ICommandHandler<CreateShopCommand, Result<AdminShopResponse>>
{
    public async Task<Result<AdminShopResponse>> Handle(CreateShopCommand command, CancellationToken cancellationToken)
    {
        var created = Shop.Create(EntityId.New<ShopId>(), command.Slug, command.NameAr, command.NameEn, command.TimeZone, clock.GetUtcNow());
        if (created.IsFailure)
        {
            return created.Error;
        }

        var shop = created.Value;
        if (await db.Set<Shop>().AnyAsync(s => s.Slug == shop.Slug, cancellationToken))
        {
            return ShopErrors.SlugTaken();
        }

        db.Add(shop);
        audit.Record(new AuditRecord("shop.created", nameof(Shop), shop.Id.ToString(), shop.Id, $"Created as {shop.Status}"));
        await db.SaveChangesAsync(cancellationToken);
        return ShopMapping.ToAdmin(shop);
    }
}

internal sealed class ListShopsHandler(TrimmeDbContext db) : IQueryHandler<ListShopsQuery, PagedResponse<AdminShopResponse>>
{
    public async Task<PagedResponse<AdminShopResponse>> Handle(ListShopsQuery query, CancellationToken cancellationToken)
    {
        var shops = db.Set<Shop>().AsNoTracking();
        if (query.Status is { } status)
        {
            shops = shops.Where(s => s.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = $"%{query.Search.Trim()}%";
            shops = shops.Where(s => EF.Functions.ILike(s.NameAr, term) || EF.Functions.ILike(s.NameEn, term) || EF.Functions.ILike(s.Slug, term)
                                     || (s.Location != null && s.Location.District != null && EF.Functions.ILike(s.Location.District, term)));
        }

        var total = await shops.CountAsync(cancellationToken);
        var page = await shops
            .OrderByDescending(s => s.CreatedAt).ThenBy(s => s.Id)
            .Skip(query.Page.Skip).Take(query.Page.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResponse<AdminShopResponse>([.. page.Select(ShopMapping.ToAdmin)], query.Page.Page, query.Page.PageSize, total);
    }
}

internal sealed class GetShopHandler(TrimmeDbContext db) : IQueryHandler<GetShopQuery, AdminShopDetailResponse?>
{
    public async Task<AdminShopDetailResponse?> Handle(GetShopQuery query, CancellationToken cancellationToken)
    {
        var id = new ShopId(query.ShopId);
        var shop = await db.Set<Shop>().AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        return shop is null ? null : ShopMapping.ToAdminDetail(shop);
    }
}

internal sealed class ChangeShopStatusHandler(TrimmeDbContext db, TimeProvider clock, IAuditLog audit)
    : ICommandHandler<ChangeShopStatusCommand, Result<AdminShopResponse>>
{
    public async Task<Result<AdminShopResponse>> Handle(ChangeShopStatusCommand command, CancellationToken cancellationToken)
    {
        var id = new ShopId(command.ShopId);
        var shop = await db.Set<Shop>().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (shop is null)
        {
            return ShopErrors.NotFound();
        }

        var before = shop.Status;
        var changed = command.Change == ShopStatusChange.Activate ? shop.Activate(clock.GetUtcNow()) : shop.Suspend(clock.GetUtcNow());
        if (changed.IsFailure)
        {
            return changed.Error;
        }

        var action = command.Change == ShopStatusChange.Activate ? "shop.activated" : "shop.suspended";
        audit.Record(new AuditRecord(action, nameof(Shop), shop.Id.ToString(), shop.Id, $"{before} → {shop.Status}", command.Reason?.Trim()));
        await db.SaveChangesAsync(cancellationToken);
        return ShopMapping.ToAdmin(shop);
    }
}

/// <summary>Admin edits of one shop: load it, apply the change, audit it and save in one unit of work.</summary>
internal abstract class AdminShopEditHandler<TCommand>(TrimmeDbContext db, IAuditLog audit) : ICommandHandler<TCommand, Result<AdminShopDetailResponse>>
    where TCommand : ICommand<Result<AdminShopDetailResponse>>
{
    protected TrimmeDbContext Db => db;

    public async Task<Result<AdminShopDetailResponse>> Handle(TCommand command, CancellationToken cancellationToken)
    {
        var id = new ShopId(ShopIdOf(command));
        var shop = await db.Set<Shop>().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (shop is null)
        {
            return ShopErrors.NotFound();
        }

        var applied = Apply(shop, command);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        audit.Record(new AuditRecord(applied.Value.Action, nameof(Shop), shop.Id.ToString(), shop.Id, applied.Value.Summary));
        await db.SaveChangesAsync(cancellationToken);
        return ShopMapping.ToAdminDetail(shop);
    }

    protected abstract Guid ShopIdOf(TCommand command);

    protected abstract Result<AuditNote> Apply(Shop shop, TCommand command);
}

internal sealed record AuditNote(string Action, string Summary);

internal sealed class UpdateShopProfileHandler(TrimmeDbContext db, IAuditLog audit, TimeProvider clock)
    : AdminShopEditHandler<UpdateShopProfileCommand>(db, audit)
{
    protected override Guid ShopIdOf(UpdateShopProfileCommand command) => command.ShopId;

    protected override Result<AuditNote> Apply(Shop shop, UpdateShopProfileCommand command)
    {
        // Optimistic concurrency: the save fails with 409 if someone changed the shop since the client read it.
        Db.Entry(shop).Property(s => s.Version).OriginalValue = command.Version;
        var now = clock.GetUtcNow();
        var changed = shop.UpdateProfile(ShopInputMapping.ToProfile(command), now).Select(f => f.ToString()).ToList();
        if (shop.IsVerified != command.IsVerified)
        {
            shop.SetVerified(command.IsVerified, now);
            changed.Add(command.IsVerified ? "Verified" : "Unverified");
        }

        return new AuditNote("shop.profile_updated", changed.Count == 0 ? "No changes" : $"Changed: {string.Join(", ", changed)}");
    }
}

internal sealed class SetShopLocationHandler(TrimmeDbContext db, IAuditLog audit, TimeProvider clock, ICurrentUser user)
    : AdminShopEditHandler<SetShopLocationCommand>(db, audit)
{
    protected override Guid ShopIdOf(SetShopLocationCommand command) => command.ShopId;

    protected override Result<AuditNote> Apply(Shop shop, SetShopLocationCommand command)
    {
        var location = ShopInputMapping.ToLocation(command, clock.GetUtcNow(), user.UserId);
        if (location.IsFailure)
        {
            return location.Error;
        }

        shop.SetLocation(location.Value, clock.GetUtcNow());
        return new AuditNote("shop.location_set", $"Location confirmed ({command.Source})");
    }
}

internal sealed class SetEditablePolicyHandler(TrimmeDbContext db, IAuditLog audit, TimeProvider clock)
    : AdminShopEditHandler<SetEditablePolicyCommand>(db, audit)
{
    protected override Guid ShopIdOf(SetEditablePolicyCommand command) => command.ShopId;

    protected override Result<AuditNote> Apply(Shop shop, SetEditablePolicyCommand command)
    {
        shop.SetEditablePolicy(command.EditableFields, clock.GetUtcNow());
        var fields = shop.EditableFields.Length == 0 ? "none" : string.Join(", ", shop.EditableFields);
        return new AuditNote("shop.editable_policy_set", $"Shop may edit: {fields}");
    }
}

internal sealed class ReplaceShopImageHandler(TrimmeDbContext db, IAuditLog audit, TimeProvider clock, IMediaStore media)
    : AdminShopEditHandler<ReplaceShopImageCommand>(db, audit)
{
    protected override Guid ShopIdOf(ReplaceShopImageCommand command) => command.ShopId;

    protected override Result<AuditNote> Apply(Shop shop, ReplaceShopImageCommand command)
    {
        var replaced = ShopImages.Replace(shop, command.Slot, command.Content, media, clock.GetUtcNow());
        return replaced.IsFailure
            ? replaced.Error
            : new AuditNote("shop.image_changed", $"{command.Slot} {(command.Content is null ? "removed" : "replaced")}");
    }
}

internal sealed class AddShopGalleryImageHandler(TrimmeDbContext db, IAuditLog audit, TimeProvider clock, IMediaStore media)
    : AdminShopEditHandler<AddShopGalleryImageCommand>(db, audit)
{
    protected override Guid ShopIdOf(AddShopGalleryImageCommand command) => command.ShopId;

    protected override Result<AuditNote> Apply(Shop shop, AddShopGalleryImageCommand command)
    {
        var added = ShopImages.AddToGallery(shop, command.Content, media, clock.GetUtcNow());
        return added.IsFailure ? added.Error : new AuditNote("shop.image_changed", "Gallery image added");
    }
}

internal sealed class RemoveShopGalleryImageHandler(TrimmeDbContext db, IAuditLog audit, TimeProvider clock, IMediaStore media)
    : AdminShopEditHandler<RemoveShopGalleryImageCommand>(db, audit)
{
    protected override Guid ShopIdOf(RemoveShopGalleryImageCommand command) => command.ShopId;

    protected override Result<AuditNote> Apply(Shop shop, RemoveShopGalleryImageCommand command)
    {
        var removed = ShopImages.RemoveFromGallery(shop, command.MediaId, media, clock.GetUtcNow());
        return removed.IsFailure ? removed.Error : new AuditNote("shop.image_changed", $"Gallery image {command.MediaId} removed");
    }
}

internal static class ShopMapping
{
    public static AdminShopResponse ToAdmin(Shop shop) => new(
        shop.Id.Value,
        shop.Slug,
        shop.NameAr,
        shop.NameEn,
        shop.Status.ToString(),
        shop.TimeZone,
        shop.RequireManualConfirmation,
        shop.Location?.District,
        shop.IsVerified,
        MediaRules.Url(shop.LogoMediaId),
        shop.CreatedAt,
        shop.UpdatedAt);

    public static AdminShopDetailResponse ToAdminDetail(Shop shop) => new(
        shop.Id.Value,
        shop.Slug,
        shop.NameAr,
        shop.NameEn,
        shop.DescriptionAr,
        shop.DescriptionEn,
        shop.Category,
        shop.PublicPhone,
        shop.Amenities,
        shop.IsVerified,
        MediaRules.Url(shop.LogoMediaId),
        MediaRules.Url(shop.CoverMediaId),
        ShopInputMapping.Gallery(shop),
        ShopInputMapping.ToResponse(shop.Location),
        shop.EditableFields,
        shop.Status.ToString(),
        shop.TimeZone,
        shop.RequireManualConfirmation,
        shop.CreatedAt,
        shop.UpdatedAt,
        shop.Version);
}
