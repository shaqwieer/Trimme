using System.Buffers.Binary;

namespace Trimme.BuildingBlocks.Infrastructure.Media;

/// <summary>A recognised image with its metadata removed.</summary>
public sealed record SanitizedImage(byte[] Bytes, string ContentType, int Width, int Height);

/// <summary>
/// Recognises JPEG, PNG and WebP from their bytes (never from a declared content type or file name), reads the pixel
/// size from the headers, and removes metadata segments that can carry personal data: EXIF (including GPS position),
/// XMP, IPTC and comments. The image data itself is copied unchanged; the API never decodes pixels.
/// Anything else (SVG, HTML, GIF, truncated or malformed files) is rejected.
/// </summary>
public static class ImageSanitizer
{
    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";
    public const string WebP = "image/webp";

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static SanitizedImage? TrySanitize(ReadOnlySpan<byte> input)
    {
        try
        {
            if (input.Length >= 4 && input[0] == 0xFF && input[1] == 0xD8 && input[2] == 0xFF)
            {
                return SanitizeJpeg(input);
            }

            if (input.Length >= 33 && input[..8].SequenceEqual(PngSignature))
            {
                return SanitizePng(input);
            }

            if (input.Length >= 30 && input[..4].SequenceEqual("RIFF"u8) && input.Slice(8, 4).SequenceEqual("WEBP"u8))
            {
                return SanitizeWebP(input);
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            // A length field pointed past the end of the file: malformed.
        }

        return null;
    }

    private static SanitizedImage? SanitizeJpeg(ReadOnlySpan<byte> input)
    {
        var output = new List<byte>(input.Length) { 0xFF, 0xD8 };
        int width = 0, height = 0;
        var pos = 2;
        while (pos < input.Length)
        {
            if (input[pos] != 0xFF)
            {
                return null;
            }

            while (pos < input.Length && input[pos] == 0xFF)
            {
                pos++; // fill bytes
            }

            var marker = input[pos];
            var segmentStart = pos - 1;
            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
            {
                output.Add(0xFF);
                output.Add(marker);
                pos++;
                continue;
            }

            if (marker == 0xD9)
            {
                return null; // end of image before any scan
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(input.Slice(pos + 1, 2));
            if (length < 2)
            {
                return null;
            }

            var segmentEnd = pos + 1 + length;
            var segment = input[segmentStart..segmentEnd];

            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                height = BinaryPrimitives.ReadUInt16BigEndian(input.Slice(pos + 4, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(input.Slice(pos + 6, 2));
            }

            if (marker == 0xDA)
            {
                // Start of scan: entropy-coded data follows until the end of the file; copy it verbatim.
                output.AddRange(input[segmentStart..]);
                return width > 0 && height > 0 ? new SanitizedImage([.. output], Jpeg, width, height) : null;
            }

            // Keep JFIF (APP0), ICC colour profiles (APP2) and Adobe colour transform (APP14); drop other APPn and comments.
            var strip = marker == 0xFE || (marker is >= 0xE0 and <= 0xEF && marker is not 0xE0 and not 0xE2 and not 0xEE);
            if (!strip)
            {
                output.AddRange(segment);
            }

            pos = segmentEnd;
        }

        return null;
    }

    private static SanitizedImage? SanitizePng(ReadOnlySpan<byte> input)
    {
        var output = new List<byte>(input.Length);
        output.AddRange(PngSignature);
        var pos = 8;
        int width = 0, height = 0;
        var first = true;
        while (pos + 12 <= input.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(input.Slice(pos, 4));
            if (length > int.MaxValue - 12 || pos + 12 + (int)length > input.Length)
            {
                return null;
            }

            var type = input.Slice(pos + 4, 4);
            var chunk = input.Slice(pos, 12 + (int)length);
            if (first)
            {
                if (!type.SequenceEqual("IHDR"u8) || length < 8)
                {
                    return null;
                }

                width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(input.Slice(pos + 8, 4)));
                height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(input.Slice(pos + 12, 4)));
                first = false;
            }

            var metadata = type.SequenceEqual("tEXt"u8) || type.SequenceEqual("zTXt"u8) || type.SequenceEqual("iTXt"u8)
                           || type.SequenceEqual("eXIf"u8) || type.SequenceEqual("tIME"u8);
            if (!metadata)
            {
                output.AddRange(chunk);
            }

            pos += chunk.Length;
            if (type.SequenceEqual("IEND"u8))
            {
                return width > 0 && height > 0 ? new SanitizedImage([.. output], Png, width, height) : null;
            }
        }

        return null;
    }

    private static SanitizedImage? SanitizeWebP(ReadOnlySpan<byte> input)
    {
        var riffEnd = 8 + (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(4, 4)), (uint)(input.Length - 8));
        var body = new List<byte>(input.Length) { (byte)'W', (byte)'E', (byte)'B', (byte)'P' };
        int width = 0, height = 0;
        var vp8xFlagsIndex = -1;
        var pos = 12;
        while (pos + 8 <= riffEnd)
        {
            var fourCc = input.Slice(pos, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(pos + 4, 4));
            var padded = (int)size + (int)(size & 1);
            if (size > int.MaxValue - 9 || pos + 8 + (int)size > riffEnd)
            {
                return null;
            }

            var data = input.Slice(pos + 8, (int)size);
            if (fourCc.SequenceEqual("VP8X"u8) && size >= 10)
            {
                width = ReadUInt24(data[4..]) + 1;
                height = ReadUInt24(data[7..]) + 1;
                vp8xFlagsIndex = body.Count + 8;
            }
            else if (fourCc.SequenceEqual("VP8 "u8) && size >= 10 && width == 0)
            {
                if (data[3] != 0x9D || data[4] != 0x01 || data[5] != 0x2A)
                {
                    return null;
                }

                width = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(6, 2)) & 0x3FFF;
                height = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(8, 2)) & 0x3FFF;
            }
            else if (fourCc.SequenceEqual("VP8L"u8) && size >= 5 && width == 0)
            {
                if (data[0] != 0x2F)
                {
                    return null;
                }

                var bits = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(1, 4));
                width = (int)(bits & 0x3FFF) + 1;
                height = (int)((bits >> 14) & 0x3FFF) + 1;
            }

            var metadata = fourCc.SequenceEqual("EXIF"u8) || fourCc.SequenceEqual("XMP "u8);
            if (!metadata)
            {
                body.AddRange(input.Slice(pos, Math.Min(8 + padded, riffEnd - pos)));
                if ((size & 1) == 1 && pos + 8 + padded > riffEnd)
                {
                    body.Add(0);
                }
            }

            pos += 8 + padded;
        }

        if (width <= 0 || height <= 0)
        {
            return null;
        }

        if (vp8xFlagsIndex >= 0)
        {
            body[vp8xFlagsIndex] &= unchecked((byte)~0x0C); // the EXIF and XMP chunks are gone
        }

        var output = new byte[8 + body.Count];
        "RIFF"u8.CopyTo(output);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(4, 4), (uint)body.Count);
        body.CopyTo(output, 8);
        return new SanitizedImage(output, WebP, width, height);
    }

    private static int ReadUInt24(ReadOnlySpan<byte> data) => data[0] | (data[1] << 8) | (data[2] << 16);
}
