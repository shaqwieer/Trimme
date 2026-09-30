using System.Globalization;
using System.Text;
using QRCoder;

namespace Trimme.Modules.QrAnalytics.Application;

public enum QrImageFormat
{
    Png,
    Svg,
    Pdf,
}

/// <summary>A generated file: its bytes, media type and a download name.</summary>
public sealed record QrImageFile(byte[] Content, string ContentType, string FileName);

/// <summary>
/// Server-side QR images (16.4, D-114). QRCoder (MIT) encodes the URL into its module matrix with the quiet zone; PNG uses
/// its managed PNG writer (no System.Drawing), and SVG and PDF are drawn here from the same matrix as vector squares, so
/// print shops get a sharp code at any size. Error correction Q (25%) survives a scratched or partly covered sticker.
/// </summary>
public static class QrImages
{
    public const int DefaultPixelsPerModule = 16;
    public const int MinPixelsPerModule = 4;
    public const int MaxPixelsPerModule = 40;

    /// <summary>The PDF page is the code itself, 70 mm square (a common sticker size), in points.</summary>
    private const double PdfSizePoints = 70 / 25.4 * 72;

    public static bool TryParseFormat(string? value, out QrImageFormat format)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case null or "" or "png":
                format = QrImageFormat.Png;
                return true;
            case "svg":
                format = QrImageFormat.Svg;
                return true;
            case "pdf":
                format = QrImageFormat.Pdf;
                return true;
            default:
                format = default;
                return false;
        }
    }

    public static QrImageFile Render(string url, string code, QrImageFormat format, int? pixelsPerModule = null)
    {
        var matrix = Matrix(url);
        var name = $"trimme-qr-{code}";
        return format switch
        {
            QrImageFormat.Svg => new QrImageFile(Encoding.UTF8.GetBytes(Svg(matrix)), "image/svg+xml", $"{name}.svg"),
            QrImageFormat.Pdf => new QrImageFile(Pdf(matrix), "application/pdf", $"{name}.pdf"),
            _ => new QrImageFile(Png(url, Math.Clamp(pixelsPerModule ?? DefaultPixelsPerModule, MinPixelsPerModule, MaxPixelsPerModule)), "image/png", $"{name}.png"),
        };
    }

    /// <summary>Dark modules as rows of booleans, quiet zone included (4 modules on every side).</summary>
    public static bool[][] Matrix(string url)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        return [.. data.ModuleMatrix.Select(row => Enumerable.Range(0, row.Length).Select(i => row[i]).ToArray())];
    }

    private static byte[] Png(string url, int pixelsPerModule)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        using var png = new PngByteQRCode(data);
        return png.GetGraphic(pixelsPerModule);
    }

    /// <summary>Horizontal runs of dark modules per row: (row, first column, length).</summary>
    private static IEnumerable<(int Row, int Column, int Length)> Runs(bool[][] matrix)
    {
        for (var y = 0; y < matrix.Length; y++)
        {
            var row = matrix[y];
            var x = 0;
            while (x < row.Length)
            {
                if (!row[x])
                {
                    x++;
                    continue;
                }

                var start = x;
                while (x < row.Length && row[x])
                {
                    x++;
                }

                yield return (y, start, x - start);
            }
        }
    }

    public static string Svg(bool[][] matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        var size = matrix.Length;
        var path = new StringBuilder();
        foreach (var (row, column, length) in Runs(matrix))
        {
            path.Append(CultureInfo.InvariantCulture, $"M{column} {row}h{length}v1h-{length}z");
        }

        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {size} {size}\" shape-rendering=\"crispEdges\" role=\"img\">"
               + $"<rect width=\"{size}\" height=\"{size}\" fill=\"#fff\"/><path fill=\"#000\" d=\"{path}\"/></svg>";
    }

    /// <summary>A one-page PDF 1.4 whose content stream fills one rectangle per run of dark modules (no fonts, no images).</summary>
    public static byte[] Pdf(bool[][] matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        var size = matrix.Length;
        var module = PdfSizePoints / size;
        string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        var content = new StringBuilder("0 g\n");
        foreach (var (row, column, length) in Runs(matrix))
        {
            // PDF's origin is the bottom-left corner.
            content.Append(CultureInfo.InvariantCulture, $"{N(column * module)} {N((size - row - 1) * module)} {N(length * module)} {N(module)} re\n");
        }

        content.Append("f\n");
        var stream = content.ToString();
        var page = N(PdfSizePoints);
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {page} {page}] /Contents 4 0 R /Resources << >> >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(stream)} >>\nstream\n{stream}endstream",
        ];

        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.Append(CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            pdf.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        pdf.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
}
