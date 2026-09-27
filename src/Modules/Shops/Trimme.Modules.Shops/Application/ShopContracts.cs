using FluentValidation;
using Trimme.BuildingBlocks.Application.Media;
using Trimme.BuildingBlocks.Domain.Media;
using Trimme.BuildingBlocks.Domain.Privacy;
using Trimme.Modules.Shops.Domain;

namespace Trimme.Modules.Shops.Application;

/// <summary>The confirmed shop entrance. Coordinates are WGS 84 decimal degrees.</summary>
public sealed record ShopLocationResponse(
    double Latitude,
    double Longitude,
    string? AddressLine,
    string? District,
    string? City,
    string? FormattedAddress,
    LocationSource Source,
    DateTimeOffset ConfirmedAt);

public sealed record ShopImageResponse(Guid Id, string Url);

/// <summary>The profile text an admin or (within the policy) the shop edits. The public phone is the shop's own number.</summary>
public interface IShopProfileFields
{
    string NameAr { get; }

    string NameEn { get; }

    string? DescriptionAr { get; }

    string? DescriptionEn { get; }

    ShopCategory Category { get; }

    string? PublicPhone { get; }

    IReadOnlyList<ShopAmenity>? Amenities { get; }
}

/// <summary>A confirmed map point and its address. Source: pin/typed coordinates, a search result, or the device position.</summary>
public interface IShopLocationFields
{
    double Latitude { get; }

    double Longitude { get; }

    string? AddressLine { get; }

    string? District { get; }

    string? City { get; }

    string? FormattedAddress { get; }

    LocationSource Source { get; }
}

internal sealed class ShopProfileRules<T> : AbstractValidator<T>
    where T : IShopProfileFields
{
    public ShopProfileRules()
    {
        RuleFor(p => p.NameAr).Must(n => !string.IsNullOrWhiteSpace(n)).WithErrorCode("validation.required")
            .MaximumLength(Shop.MaxNameLength).WithErrorCode("validation.too_long");
        RuleFor(p => p.NameEn).Must(n => !string.IsNullOrWhiteSpace(n)).WithErrorCode("validation.required")
            .MaximumLength(Shop.MaxNameLength).WithErrorCode("validation.too_long");
        RuleFor(p => p.DescriptionAr).MaximumLength(Shop.MaxDescriptionLength).WithErrorCode("validation.too_long");
        RuleFor(p => p.DescriptionEn).MaximumLength(Shop.MaxDescriptionLength).WithErrorCode("validation.too_long");
        RuleFor(p => p.Category).IsInEnum().WithErrorCode("validation.invalid");
        RuleFor(p => p.PublicPhone)
            .Must(phone => string.IsNullOrWhiteSpace(phone) || PhoneNumber.TryParse(phone, out _))
            .WithErrorCode("validation.phone_invalid");
        RuleForEach(p => p.Amenities).IsInEnum().WithErrorCode("validation.invalid");
    }
}

internal sealed class ShopLocationRules<T> : AbstractValidator<T>
    where T : IShopLocationFields
{
    public ShopLocationRules()
    {
        RuleFor(l => l.Latitude).InclusiveBetween(-90, 90).WithErrorCode("validation.coordinate_invalid");
        RuleFor(l => l.Longitude).InclusiveBetween(-180, 180).WithErrorCode("validation.coordinate_invalid");
        RuleFor(l => l.AddressLine).MaximumLength(ShopLocation.MaxAddressLineLength).WithErrorCode("validation.too_long");
        RuleFor(l => l.District).MaximumLength(ShopLocation.MaxAreaLength).WithErrorCode("validation.too_long");
        RuleFor(l => l.City).MaximumLength(ShopLocation.MaxAreaLength).WithErrorCode("validation.too_long");
        RuleFor(l => l.FormattedAddress).MaximumLength(ShopLocation.MaxFormattedAddressLength).WithErrorCode("validation.too_long");
        RuleFor(l => l.Source).IsInEnum().WithErrorCode("validation.invalid");
    }
}

internal static class ShopInputMapping
{
    /// <summary>Normalizes validated input: trimmed text, empty → null, phone in E.164.</summary>
    public static ShopProfile ToProfile(IShopProfileFields input)
    {
        var phone = PhoneNumber.TryParse(input.PublicPhone, out var parsed) ? parsed.E164 : null;
        return ShopProfile.Create(input.NameAr, input.NameEn, input.DescriptionAr, input.DescriptionEn, input.Category, phone, input.Amenities);
    }

    public static BuildingBlocks.Domain.Results.Result<ShopLocation> ToLocation(IShopLocationFields input, DateTimeOffset now, Guid? confirmedBy) =>
        ShopLocation.Create(input.Latitude, input.Longitude, input.AddressLine, input.District, input.City, input.FormattedAddress, input.Source, now, confirmedBy);

    public static ShopLocationResponse? ToResponse(ShopLocation? location) =>
        location is null
            ? null
            : new ShopLocationResponse(
                location.Latitude,
                location.Longitude,
                location.AddressLine,
                location.District,
                location.City,
                location.FormattedAddress,
                location.Source,
                location.ConfirmedAt);

    public static IReadOnlyList<ShopImageResponse> Gallery(Shop shop) =>
        [.. shop.GalleryMediaIds.Select(id => new ShopImageResponse(id, MediaRules.Url(new MediaId(id))))];
}

/// <summary>Which single image of a shop an upload replaces.</summary>
public enum ShopImageSlot
{
    Logo,
    Cover,
}

/// <summary>Image changes shared by the admin and shop use cases. The old image is deleted in the same unit of work.</summary>
internal static class ShopImages
{
    public static ShopProfileField Field(ShopImageSlot slot) => slot == ShopImageSlot.Logo ? ShopProfileField.Logo : ShopProfileField.Cover;

    /// <summary>Stores <paramref name="content"/> (or clears the slot when null) and deletes the image it replaces.</summary>
    public static BuildingBlocks.Domain.Results.Result Replace(Shop shop, ShopImageSlot slot, byte[]? content, IMediaStore media, DateTimeOffset now)
    {
        MediaId? next = null;
        if (content is not null)
        {
            var stored = media.AddImage(content, slot == ShopImageSlot.Logo ? MediaPurpose.ShopLogo : MediaPurpose.ShopCover);
            if (stored.IsFailure)
            {
                return stored.Error;
            }

            next = stored.Value.Id;
        }

        var previous = slot == ShopImageSlot.Logo ? shop.ReplaceLogo(next, now) : shop.ReplaceCover(next, now);
        if (previous is { } old)
        {
            media.Remove(old);
        }

        return BuildingBlocks.Domain.Results.Result.Success();
    }

    public static BuildingBlocks.Domain.Results.Result AddToGallery(Shop shop, byte[] content, IMediaStore media, DateTimeOffset now)
    {
        if (shop.GalleryMediaIds.Length >= Shop.MaxGalleryImages)
        {
            return ShopErrors.GalleryFull();
        }

        var stored = media.AddImage(content, MediaPurpose.ShopGallery);
        return stored.IsFailure ? stored.Error : shop.AddGalleryImage(stored.Value.Id, now);
    }

    public static BuildingBlocks.Domain.Results.Result RemoveFromGallery(Shop shop, Guid mediaId, IMediaStore media, DateTimeOffset now)
    {
        var id = new MediaId(mediaId);
        if (!shop.RemoveGalleryImage(id, now))
        {
            return ShopErrors.GalleryImageNotFound();
        }

        media.Remove(id);
        return BuildingBlocks.Domain.Results.Result.Success();
    }
}
