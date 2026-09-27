using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Auditing;
using Trimme.BuildingBlocks.Application.Media;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Shops.Domain;

namespace Trimme.Modules.Shops.Application;

/// <summary>
/// The signed-in shop's own profile, for its settings screen (s-settings). <see cref="EditableFields"/> is the admin
/// policy: the server rejects a change to any other field (R-SHP-03). Shop-facing contract: no customer contact data;
/// <see cref="PublicPhone"/> is the shop's own business number.
/// </summary>
public sealed record ShopOwnProfileResponse(
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
    uint Version);

internal sealed record GetOwnShopProfileQuery : IQuery<ShopOwnProfileResponse?>;

internal sealed record UpdateOwnShopProfileCommand(
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    ShopCategory Category,
    string? PublicPhone,
    IReadOnlyList<ShopAmenity>? Amenities,
    uint Version) : ICommand<Result<ShopOwnProfileResponse>>, IShopProfileFields;

internal sealed record SetOwnShopLocationCommand(
    double Latitude,
    double Longitude,
    string? AddressLine,
    string? District,
    string? City,
    string? FormattedAddress,
    LocationSource Source) : ICommand<Result<ShopOwnProfileResponse>>, IShopLocationFields;

internal sealed record ReplaceOwnShopImageCommand(ShopImageSlot Slot, byte[]? Content) : ICommand<Result<ShopOwnProfileResponse>>;

internal sealed record AddOwnShopGalleryImageCommand(byte[] Content) : ICommand<Result<ShopOwnProfileResponse>>;

internal sealed record RemoveOwnShopGalleryImageCommand(Guid MediaId) : ICommand<Result<ShopOwnProfileResponse>>;

internal sealed class UpdateOwnShopProfileValidator : AbstractValidator<UpdateOwnShopProfileCommand>
{
    public UpdateOwnShopProfileValidator() => Include(new ShopProfileRules<UpdateOwnShopProfileCommand>());
}

internal sealed class SetOwnShopLocationValidator : AbstractValidator<SetOwnShopLocationCommand>
{
    public SetOwnShopLocationValidator() => Include(new ShopLocationRules<SetOwnShopLocationCommand>());
}

/// <summary>
/// The shop comes from <see cref="ICurrentTenant"/> — never from the request, and never from the session claim, which
/// is still present while the shop is suspended. No tenant (suspended shop) means not found.
/// </summary>
internal sealed class GetOwnShopProfileHandler(TrimmeDbContext db, ICurrentTenant tenant) : IQueryHandler<GetOwnShopProfileQuery, ShopOwnProfileResponse?>
{
    public async Task<ShopOwnProfileResponse?> Handle(GetOwnShopProfileQuery query, CancellationToken cancellationToken)
    {
        if (tenant.ShopId is not { } shopId)
        {
            return null;
        }

        var shop = await db.Set<Shop>().AsNoTracking().SingleOrDefaultAsync(s => s.Id == shopId, cancellationToken);
        return shop is null ? null : OwnProfileMapping.ToResponse(shop);
    }
}

/// <summary>Shop self-service edits: load the tenant's shop, check the admin policy, apply, audit, save.</summary>
internal abstract class OwnShopEditHandler<TCommand>(TrimmeDbContext db, ICurrentTenant tenant, IAuditLog audit)
    : ICommandHandler<TCommand, Result<ShopOwnProfileResponse>>
    where TCommand : ICommand<Result<ShopOwnProfileResponse>>
{
    protected TrimmeDbContext Db => db;

    public async Task<Result<ShopOwnProfileResponse>> Handle(TCommand command, CancellationToken cancellationToken)
    {
        if (tenant.ShopId is not { } shopId)
        {
            return ShopErrors.NotFound();
        }

        var shop = await db.Set<Shop>().SingleOrDefaultAsync(s => s.Id == shopId, cancellationToken);
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
        return OwnProfileMapping.ToResponse(shop);
    }

    protected static Error? Locked(Shop shop, params ShopProfileField[] fields)
    {
        var locked = fields.Where(f => !shop.IsEditableByShop(f)).ToArray();
        return locked.Length == 0 ? null : ShopErrors.FieldsLocked(locked);
    }

    protected abstract Result<Admin.AuditNote> Apply(Shop shop, TCommand command);
}

internal sealed class UpdateOwnShopProfileHandler(TrimmeDbContext db, ICurrentTenant tenant, IAuditLog audit, TimeProvider clock)
    : OwnShopEditHandler<UpdateOwnShopProfileCommand>(db, tenant, audit)
{
    protected override Result<Admin.AuditNote> Apply(Shop shop, UpdateOwnShopProfileCommand command)
    {
        // The whole form is sent; only fields whose value actually changes must be open to the shop.
        var profile = ShopInputMapping.ToProfile(command);
        if (Locked(shop, [.. shop.ChangedFields(profile)]) is { } locked)
        {
            return locked;
        }

        Db.Entry(shop).Property(s => s.Version).OriginalValue = command.Version;
        var changed = shop.UpdateProfile(profile, clock.GetUtcNow());
        return new Admin.AuditNote("shop.profile_updated", changed.Count == 0 ? "No changes" : $"Changed by shop: {string.Join(", ", changed)}");
    }
}

internal sealed class SetOwnShopLocationHandler(TrimmeDbContext db, ICurrentTenant tenant, IAuditLog audit, TimeProvider clock, ICurrentUser user)
    : OwnShopEditHandler<SetOwnShopLocationCommand>(db, tenant, audit)
{
    protected override Result<Admin.AuditNote> Apply(Shop shop, SetOwnShopLocationCommand command)
    {
        if (Locked(shop, ShopProfileField.Location) is { } locked)
        {
            return locked;
        }

        var location = ShopInputMapping.ToLocation(command, clock.GetUtcNow(), user.UserId);
        if (location.IsFailure)
        {
            return location.Error;
        }

        shop.SetLocation(location.Value, clock.GetUtcNow());
        return new Admin.AuditNote("shop.location_set", $"Location confirmed by shop ({command.Source})");
    }
}

internal sealed class ReplaceOwnShopImageHandler(TrimmeDbContext db, ICurrentTenant tenant, IAuditLog audit, TimeProvider clock, IMediaStore media)
    : OwnShopEditHandler<ReplaceOwnShopImageCommand>(db, tenant, audit)
{
    protected override Result<Admin.AuditNote> Apply(Shop shop, ReplaceOwnShopImageCommand command)
    {
        if (Locked(shop, ShopImages.Field(command.Slot)) is { } locked)
        {
            return locked;
        }

        var replaced = ShopImages.Replace(shop, command.Slot, command.Content, media, clock.GetUtcNow());
        return replaced.IsFailure
            ? replaced.Error
            : new Admin.AuditNote("shop.image_changed", $"{command.Slot} {(command.Content is null ? "removed" : "replaced")} by shop");
    }
}

internal sealed class AddOwnShopGalleryImageHandler(TrimmeDbContext db, ICurrentTenant tenant, IAuditLog audit, TimeProvider clock, IMediaStore media)
    : OwnShopEditHandler<AddOwnShopGalleryImageCommand>(db, tenant, audit)
{
    protected override Result<Admin.AuditNote> Apply(Shop shop, AddOwnShopGalleryImageCommand command)
    {
        if (Locked(shop, ShopProfileField.Gallery) is { } locked)
        {
            return locked;
        }

        var added = ShopImages.AddToGallery(shop, command.Content, media, clock.GetUtcNow());
        return added.IsFailure ? added.Error : new Admin.AuditNote("shop.image_changed", "Gallery image added by shop");
    }
}

internal sealed class RemoveOwnShopGalleryImageHandler(TrimmeDbContext db, ICurrentTenant tenant, IAuditLog audit, TimeProvider clock, IMediaStore media)
    : OwnShopEditHandler<RemoveOwnShopGalleryImageCommand>(db, tenant, audit)
{
    protected override Result<Admin.AuditNote> Apply(Shop shop, RemoveOwnShopGalleryImageCommand command)
    {
        if (Locked(shop, ShopProfileField.Gallery) is { } locked)
        {
            return locked;
        }

        var removed = ShopImages.RemoveFromGallery(shop, command.MediaId, media, clock.GetUtcNow());
        return removed.IsFailure ? removed.Error : new Admin.AuditNote("shop.image_changed", $"Gallery image {command.MediaId} removed by shop");
    }
}

internal static class OwnProfileMapping
{
    public static ShopOwnProfileResponse ToResponse(Shop shop) => new(
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
        shop.Version);
}
