using Trimme.BuildingBlocks.Domain.Media;
using Trimme.BuildingBlocks.Domain.Results;

namespace Trimme.BuildingBlocks.Application.Media;

/// <summary>
/// Images stored in PostgreSQL (D-064). Both methods only stage the change in the current unit of work: the caller's
/// <c>SaveChanges</c> commits the image together with the aggregate that references it, or neither.
/// Clients never attach an existing media id to an aggregate; an upload creates the image and its reference in one
/// command, so one shop can never point at another shop's image.
/// </summary>
public interface IMediaStore
{
    /// <summary>
    /// Validates <paramref name="content"/> by its bytes (JPEG, PNG or WebP only; the declared content type is ignored),
    /// re-encodes it from its pixels (no metadata such as EXIF location survives; the longer side is at most 2560 px),
    /// and stages it.
    /// </summary>
    Result<StoredImage> AddImage(ReadOnlySpan<byte> content, MediaPurpose purpose);

    /// <summary>Stages the deletion of an image that is no longer referenced.</summary>
    void Remove(MediaId id);
}

public sealed record StoredImage(MediaId Id, string ContentType, int Width, int Height, long SizeBytes);

public static class MediaRules
{
    /// <summary>Largest accepted image file.</summary>
    public const int MaxImageBytes = 5 * 1024 * 1024;

    /// <summary>Upload request ceiling: the image plus multipart framing.</summary>
    public const long MaxUploadRequestBytes = MaxImageBytes + 64 * 1024;

    public const int MaxDimension = 8000;

    /// <summary>Decompression-bomb guard, checked on the header before the API decodes the image to re-encode it (D-119).</summary>
    public const long MaxPixels = 40_000_000;

    public static int MinDimension(MediaPurpose purpose) => purpose switch
    {
        MediaPurpose.ShopCover => 480,
        MediaPurpose.ShopGallery => 320,
        _ => 128,
    };

    /// <summary>Same-origin URL of a stored image; the web app and Nginx route <c>/api</c> to the API.</summary>
    public static string Url(MediaId id) => $"/api/v1/media/{id.Value}";

    public static string? Url(MediaId? id) => id is { } value ? Url(value) : null;
}

public static class MediaErrors
{
    public static Error Missing() => Invalid("validation.required");

    public static Error UnsupportedType() => Invalid("validation.image_type");

    public static Error TooLarge() => Invalid("validation.image_too_large");

    public static Error TooSmall() => Invalid("validation.image_too_small");

    private static Error Invalid(string code) =>
        Error.Validation("validation.failed", "The image is not acceptable.",
            new Dictionary<string, string[]>(StringComparer.Ordinal) { ["file"] = [code] });
}
