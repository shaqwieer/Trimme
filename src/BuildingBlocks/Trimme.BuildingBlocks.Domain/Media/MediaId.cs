using Trimme.BuildingBlocks.Domain.Primitives;

namespace Trimme.BuildingBlocks.Domain.Media;

/// <summary>
/// Identifier of an image stored in the database (D-064). A stored image is never changed in place: replacing a photo
/// stores a new one under a new id, so a media URL can be cached forever.
/// </summary>
public readonly record struct MediaId(Guid Value) : IEntityId<MediaId>
{
    public static MediaId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

/// <summary>What an image is used for. It sets the minimum size and is recorded with the image.</summary>
public enum MediaPurpose
{
    ShopLogo,
    ShopCover,
    ShopGallery,
    ProfessionalAvatar,
}
