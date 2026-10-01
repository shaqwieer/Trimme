using SkiaSharp;

namespace Trimme.BuildingBlocks.Infrastructure.Media;

/// <summary>
/// Decodes an uploaded image and encodes a fresh file from its pixels (D-119), so nothing but pixels survives: no
/// metadata, no trailing or embedded payload (polyglot files), no malformed structures aimed at the decoders of the people
/// who view the image. Along the way:
/// <list type="bullet">
/// <item>the EXIF orientation is applied to the pixels, since the metadata that carried it is gone;</item>
/// <item>the longer side is capped at <see cref="MaxStoredDimension"/>, which bounds what PostgreSQL stores (D-064);</item>
/// <item>the format is kept: JPEG and WebP at quality <see cref="Quality"/>, PNG lossless.</item>
/// </list>
/// Callers must check the header's pixel count first (<see cref="ImageSanitizer"/>): that is the decompression-bomb guard,
/// and large JPEGs are decoded at a reduced scale where the codec supports it.
/// </summary>
public static class ImageReencoder
{
    public const int MaxStoredDimension = 2560;

    public const int Quality = 85;

    public static SanitizedImage? TryReencode(ReadOnlySpan<byte> input, SanitizedImage header)
    {
        ArgumentNullException.ThrowIfNull(header);
        var format = header.ContentType switch
        {
            ImageSanitizer.Jpeg => SKEncodedImageFormat.Jpeg,
            ImageSanitizer.Png => SKEncodedImageFormat.Png,
            ImageSanitizer.WebP => SKEncodedImageFormat.Webp,
            _ => (SKEncodedImageFormat?)null,
        };
        if (format is null)
        {
            return null;
        }

        using var data = SKData.CreateCopy(input);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.EncodedFormat != format
            || codec.Info.Width != header.Width || codec.Info.Height != header.Height)
        {
            return null; // undecodable, or the header and the codec disagree about what the file is
        }

        using var decoded = Decode(codec);
        if (decoded is null)
        {
            return null;
        }

        using var oriented = Orient(decoded, codec.EncodedOrigin);
        using var sized = Fit(oriented);
        using var image = SKImage.FromBitmap(sized);
        using var encoded = image.Encode(format.Value, Quality);
        return encoded is null ? null : new SanitizedImage(encoded.ToArray(), header.ContentType, sized.Width, sized.Height);
    }

    private static SKBitmap? Decode(SKCodec codec)
    {
        // Decode below full size when even the scaled result is at least the stored size (JPEG supports 1/2, 1/4, 1/8).
        var longest = Math.Max(codec.Info.Width, codec.Info.Height);
        var size = codec.Info.Size;
        if (longest > MaxStoredDimension)
        {
            var scaled = codec.GetScaledDimensions((float)MaxStoredDimension / longest);
            if (Math.Max(scaled.Width, scaled.Height) >= MaxStoredDimension)
            {
                size = scaled;
            }
        }

        var alpha = codec.Info.AlphaType == SKAlphaType.Opaque ? SKAlphaType.Opaque : SKAlphaType.Premul;
        var info = new SKImageInfo(size.Width, size.Height, SKImageInfo.PlatformColorType, alpha);
        var bitmap = new SKBitmap(info);
        if (codec.GetPixels(info, bitmap.GetPixels()) != SKCodecResult.Success)
        {
            bitmap.Dispose();
            return null; // truncated or corrupt image data
        }

        return bitmap;
    }

    /// <summary>Draws the pixels the way the EXIF orientation says they are meant to be seen.</summary>
    private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var width = swap ? source.Height : source.Width;
        var height = swap ? source.Width : source.Height;
        var target = new SKBitmap(new SKImageInfo(width, height, source.ColorType, source.AlphaType));
        using var canvas = new SKCanvas(target);
        switch (origin)
        {
            case SKEncodedOrigin.TopRight: // mirrored
                canvas.Translate(width, 0);
                canvas.Scale(-1, 1);
                break;
            case SKEncodedOrigin.BottomRight: // 180°
                canvas.Translate(width, height);
                canvas.RotateDegrees(180);
                break;
            case SKEncodedOrigin.BottomLeft: // flipped
                canvas.Translate(0, height);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.LeftTop: // transposed
                canvas.Scale(-1, 1);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightTop: // 90° clockwise
                canvas.Translate(width, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightBottom: // transverse
                canvas.Translate(width, height);
                canvas.RotateDegrees(90);
                canvas.Scale(-1, 1);
                break;
            case SKEncodedOrigin.LeftBottom: // 90° counter-clockwise
                canvas.Translate(0, height);
                canvas.RotateDegrees(270);
                break;
        }

        using var pixels = SKImage.FromBitmap(source);
        canvas.DrawImage(pixels, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        return target;
    }

    private static SKBitmap Fit(SKBitmap source)
    {
        var longest = Math.Max(source.Width, source.Height);
        if (longest <= MaxStoredDimension)
        {
            return source.Copy();
        }

        var scale = (double)MaxStoredDimension / longest;
        var info = new SKImageInfo(
            Math.Max(1, (int)Math.Round(source.Width * scale)), Math.Max(1, (int)Math.Round(source.Height * scale)), source.ColorType, source.AlphaType);
        return source.Resize(info, new SKSamplingOptions(SKCubicResampler.Mitchell))
               ?? throw new InvalidOperationException("Resizing the image failed.");
    }
}
