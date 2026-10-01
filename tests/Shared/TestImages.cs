using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Trimme.Tests.Shared;

/// <summary>
/// Builds small image files for media tests: a real, decodable PNG, and structurally valid JPEG and WebP headers with
/// metadata segments to prove they are stripped. Linked into the unit and integration test projects.
/// </summary>
internal static class TestImages
{
    public static readonly byte[] ExifMarker = Encoding.ASCII.GetBytes("Exif\0\0GPSLatitude=24.8123");

    /// <summary>A valid, decodable RGB PNG of the given size, optionally with a text chunk (metadata to strip).</summary>
    public static byte[] Png(int width, int height, bool withTextChunk = false)
    {
        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bit depth
        header[9] = 2; // RGB
        WriteChunk(output, "IHDR", header);

        if (withTextChunk)
        {
            WriteChunk(output, "tEXt", Encoding.ASCII.GetBytes("Comment\0GPSLatitude=24.8123"));
        }

        var raw = new byte[height * (1 + (width * 3))];
        for (var row = 0; row < height; row++)
        {
            var offset = row * (1 + (width * 3));
            for (var x = 0; x < width; x++)
            {
                raw[offset + 1 + (x * 3)] = 0x2C;
                raw[offset + 2 + (x * 3)] = 0x5C;
                raw[offset + 3 + (x * 3)] = 0x8C;
            }
        }

        using (var compressed = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            {
                zlib.Write(raw);
            }

            WriteChunk(output, "IDAT", compressed.ToArray());
        }

        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    /// <summary>A JPEG header (SOI, APP0, APP1 Exif with a GPS tag, SOF0 with the size, SOS, scan bytes, EOI).</summary>
    public static byte[] Jpeg(int width, int height)
    {
        using var output = new MemoryStream();
        output.Write([0xFF, 0xD8]);
        WriteSegment(output, 0xE0, [.. "JFIF\0"u8, 1, 1, 0, 0, 1, 0, 1, 0, 0]);
        WriteSegment(output, 0xE1, ExifMarker);
        var sof = new byte[15];
        sof[0] = 8;
        BinaryPrimitives.WriteUInt16BigEndian(sof.AsSpan(1), (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(sof.AsSpan(3), (ushort)width);
        sof[5] = 3;
        WriteSegment(output, 0xC0, sof);
        WriteSegment(output, 0xDA, [3, 1, 0, 2, 0x11, 3, 0x11, 0, 0x3F, 0]);
        output.Write([0x12, 0x34, 0x56, 0x78, 0xFF, 0xD9]);
        return output.ToArray();
    }

    /// <summary>A lossless WebP (VP8L) header inside an extended (VP8X) container with an EXIF chunk.</summary>
    public static byte[] WebP(int width, int height)
    {
        using var body = new MemoryStream();
        body.Write("WEBP"u8);

        var vp8x = new byte[10];
        vp8x[0] = 0x08; // EXIF present
        WriteUInt24(vp8x.AsSpan(4), width - 1);
        WriteUInt24(vp8x.AsSpan(7), height - 1);
        WriteRiffChunk(body, "VP8X", vp8x);

        var vp8l = new byte[6];
        vp8l[0] = 0x2F;
        var bits = (uint)(width - 1) | ((uint)(height - 1) << 14);
        BinaryPrimitives.WriteUInt32LittleEndian(vp8l.AsSpan(1), bits);
        WriteRiffChunk(body, "VP8L", vp8l);
        WriteRiffChunk(body, "EXIF", ExifMarker);

        var payload = body.ToArray();
        var file = new byte[8 + payload.Length];
        "RIFF"u8.CopyTo(file);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), (uint)payload.Length);
        payload.CopyTo(file, 8);
        return file;
    }

    /// <summary>
    /// A real, decodable JPEG: its left half red and its right half blue. It carries an EXIF segment with the given
    /// orientation (1 = as stored, 6 = rotate 90° clockwise to view) and a comment holding <see cref="ExifMarker"/>.
    /// </summary>
    public static byte[] EncodedJpeg(int width, int height, ushort orientation = 1)
    {
        var encoded = Encode(width, height, SkiaSharp.SKEncodedImageFormat.Jpeg);
        using var output = new MemoryStream();
        output.Write(encoded.AsSpan(0, 2)); // SOI

        // APP1 Exif: big-endian TIFF header, one IFD with one entry (0x0112 Orientation, SHORT, count 1).
        var exif = new byte[6 + 8 + 2 + 12 + 4];
        "Exif\0\0"u8.CopyTo(exif);
        "MM\0*"u8.CopyTo(exif.AsSpan(6));
        BinaryPrimitives.WriteUInt32BigEndian(exif.AsSpan(10), 8);
        BinaryPrimitives.WriteUInt16BigEndian(exif.AsSpan(14), 1);
        BinaryPrimitives.WriteUInt16BigEndian(exif.AsSpan(16), 0x0112);
        BinaryPrimitives.WriteUInt16BigEndian(exif.AsSpan(18), 3);
        BinaryPrimitives.WriteUInt32BigEndian(exif.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt16BigEndian(exif.AsSpan(24), orientation);
        WriteSegment(output, 0xE1, exif);
        WriteSegment(output, 0xFE, ExifMarker);
        output.Write(encoded.AsSpan(2));
        return output.ToArray();
    }

    /// <summary>A real, decodable lossy WebP (left half red, right half blue).</summary>
    public static byte[] EncodedWebP(int width, int height) => Encode(width, height, SkiaSharp.SKEncodedImageFormat.Webp);

    /// <summary>A valid PNG followed by an HTML payload after its end chunk: an image and a page at once (a polyglot).</summary>
    public static byte[] PolyglotPng(int width, int height) => [.. Png(width, height), .. PolyglotPayload];

    public static readonly byte[] PolyglotPayload = Encoding.ASCII.GetBytes("<html><script>alert(document.cookie)</script></html>");

    public static bool Contains(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle) >= 0;

    private static byte[] Encode(int width, int height, SkiaSharp.SKEncodedImageFormat format)
    {
        using var bitmap = new SkiaSharp.SKBitmap(width, height, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Opaque);
        using (var canvas = new SkiaSharp.SKCanvas(bitmap))
        {
            canvas.Clear(new SkiaSharp.SKColor(0, 0, 255));
            using var red = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(255, 0, 0) };
            canvas.DrawRect(0, 0, width / 2f, height, red);
        }

        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        var crc = Crc32([.. typeBytes, .. data]);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }

    private static void WriteSegment(Stream output, byte marker, byte[] data)
    {
        output.Write([0xFF, marker, (byte)((data.Length + 2) >> 8), (byte)((data.Length + 2) & 0xFF)]);
        output.Write(data);
    }

    private static void WriteRiffChunk(Stream output, string fourCc, byte[] data)
    {
        output.Write(Encoding.ASCII.GetBytes(fourCc));
        Span<byte> size = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)data.Length);
        output.Write(size);
        output.Write(data);
        if (data.Length % 2 == 1)
        {
            output.WriteByte(0);
        }
    }

    private static void WriteUInt24(Span<byte> target, int value)
    {
        target[0] = (byte)(value & 0xFF);
        target[1] = (byte)((value >> 8) & 0xFF);
        target[2] = (byte)((value >> 16) & 0xFF);
    }
}
