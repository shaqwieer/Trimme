using System.Text;
using System.Text.RegularExpressions;
using Shouldly;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.Modules.QrAnalytics.Application;
using Trimme.Modules.QrAnalytics.Domain;

namespace Trimme.UnitTests.Qr;

/// <summary>
/// Phase 16 pure rules (D-114): the short code, the device class kept instead of the user agent, the code's lifecycle,
/// and the printed images drawn from one module matrix.
/// </summary>
public sealed partial class QrDomainTests
{
    private const string Url = "https://trimme.sa/q/aswn7qkd";

    [Fact]
    public void NewCodes_AreEightCharactersOfTheUnambiguousAlphabet_AndRarelyRepeat()
    {
        var codes = Enumerable.Range(0, 2000).Select(_ => QrCodeFormat.NewCode()).ToList();
        codes.ShouldAllBe(c => QrCodeFormat.IsWellFormed(c));
        codes.Distinct().Count().ShouldBe(codes.Count);
        QrCodeFormat.Alphabet.ShouldNotContain('0');
        QrCodeFormat.Alphabet.ShouldNotContain('o');
        QrCodeFormat.Alphabet.ShouldNotContain('1');
        QrCodeFormat.Alphabet.ShouldNotContain('l');
        QrCodeFormat.Alphabet.ShouldNotContain('i');
    }

    [Theory]
    [InlineData("ASWN7QKD", "aswn7qkd")]
    [InlineData(" aswn7qkd ", "aswn7qkd")]
    [InlineData("aswn7qk", null)]
    [InlineData("aswn7qkd1", null)]
    [InlineData("aswn0qkd", null)]
    [InlineData("../../x", null)]
    [InlineData(null, null)]
    public void Codes_AreMatchedLowercase_AndMalformedOnesNeverReachTheDatabase(string? input, string? expected) =>
        QrCodeFormat.Normalize(input).ShouldBe(expected);

    [Theory]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) Mobile/15E148", QrDeviceClass.Mobile)]
    [InlineData("Mozilla/5.0 (Linux; Android 15; SM-S921B) AppleWebKit/537.36 Chrome/130.0 Mobile Safari/537.36", QrDeviceClass.Mobile)]
    [InlineData("Mozilla/5.0 (Linux; Android 14; SM-X710) AppleWebKit/537.36 Chrome/130.0 Safari/537.36", QrDeviceClass.Tablet)]
    [InlineData("Mozilla/5.0 (iPad; CPU OS 18_0 like Mac OS X)", QrDeviceClass.Tablet)]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130.0 Safari/537.36", QrDeviceClass.Desktop)]
    [InlineData("WhatsApp/2.24 link preview", QrDeviceClass.Bot)]
    [InlineData("Googlebot/2.1", QrDeviceClass.Bot)]
    [InlineData("", QrDeviceClass.Other)]
    [InlineData(null, QrDeviceClass.Other)]
    public void TheDeviceClass_IsAllThatIsKeptOfTheUserAgent(string? userAgent, QrDeviceClass expected) =>
        QrDevices.Classify(userAgent).ShouldBe(expected);

    [Fact]
    public void ACode_TargetsTheShopOrOneProfessional_AndSwitchesOffAndOnOnce()
    {
        var shop = new ShopId(Guid.CreateVersion7());
        var professional = new ProfessionalId(Guid.CreateVersion7());
        var now = DateTimeOffset.UtcNow;
        var link = QrCodeLink.Create(new QrCodeLinkId(Guid.CreateVersion7()), shop, "aswn7qkd", professional, "  مرآة سلطان ", null, now).Value;
        link.TargetType.ShouldBe(QrTargetType.Professional);
        link.Label.ShouldBe("مرآة سلطان");
        link.IsActive.ShouldBeTrue();
        QrCodeLink.Create(new QrCodeLinkId(Guid.CreateVersion7()), shop, "aswn7qkd", null, " ", null, now).Value.TargetType.ShouldBe(QrTargetType.Shop);
        QrCodeLink.Create(new QrCodeLinkId(Guid.CreateVersion7()), shop, "aswn7qkd", null, new string('x', 81), null, now).IsFailure.ShouldBeTrue();
        Should.Throw<ArgumentException>(() => QrCodeLink.Create(new QrCodeLinkId(Guid.CreateVersion7()), shop, "BAD", null, null, null, now));

        link.Activate().Error!.Code.ShouldBe("qr.already_active");
        link.Deactivate(now).IsSuccess.ShouldBeTrue();
        link.DeactivatedAt.ShouldBe(now);
        link.Deactivate(now).Error!.Code.ShouldBe("qr.already_inactive");
        link.Activate().IsSuccess.ShouldBeTrue();
        link.DeactivatedAt.ShouldBeNull();
        QrCodeRoute.For(link).ShouldSatisfyAllConditions(r => r.Code.ShouldBe("aswn7qkd"), r => r.ShopId.ShouldBe(shop), r => r.LinkId.ShouldBe(link.Id));
    }

    [Fact]
    public void TheMatrix_HasAQuietZoneAndThreeFinderPatterns()
    {
        var matrix = QrImages.Matrix(Url);
        var size = matrix.Length;
        matrix.ShouldAllBe(row => row.Length == size);
        ((size - 8 - 17) % 4).ShouldBe(0, "version size 17 + 4v, plus a 4-module quiet zone on each side");

        for (var i = 0; i < size; i++)
        {
            foreach (var edge in new[] { 0, 1, 2, 3, size - 4, size - 3, size - 2, size - 1 })
            {
                matrix[edge][i].ShouldBeFalse();
                matrix[i][edge].ShouldBeFalse();
            }
        }

        // A finder pattern: a dark 7×7 ring, a light ring, and a dark 3×3 centre.
        void Finder(int top, int left)
        {
            for (var y = 0; y < 7; y++)
            {
                for (var x = 0; x < 7; x++)
                {
                    var ring = Math.Max(Math.Abs(y - 3), Math.Abs(x - 3));
                    matrix[top + y][left + x].ShouldBe(ring != 2, $"module ({top + y}, {left + x})");
                }
            }
        }

        Finder(4, 4);
        Finder(4, size - 11);
        Finder(size - 11, 4);
    }

    [Fact]
    public void TheSvgAndPdf_DrawExactlyTheDarkModules()
    {
        var matrix = QrImages.Matrix(Url);
        var dark = matrix.Sum(row => row.Count(m => m));

        var svg = QrImages.Svg(matrix);
        svg.ShouldStartWith($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {matrix.Length} {matrix.Length}\"");
        SvgRun().Matches(svg).Sum(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).ShouldBe(dark);

        var pdf = Encoding.ASCII.GetString(QrImages.Pdf(matrix));
        pdf.ShouldStartWith("%PDF-1.4\n");
        pdf.ShouldEndWith("%%EOF\n");
        var module = 70 / 25.4 * 72 / matrix.Length;
        PdfRun().Matches(pdf).Sum(m => (int)Math.Round(double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) / module)).ShouldBe(dark);

        // Every cross-reference offset points at its object.
        var xref = int.Parse(Regex.Match(pdf, @"startxref\n(\d+)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        pdf[xref..].ShouldStartWith("xref\n0 5\n");
        var offsets = Regex.Matches(pdf[xref..], @"(\d{10}) 00000 n").Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        offsets.Count.ShouldBe(4);
        for (var i = 0; i < offsets.Count; i++)
        {
            pdf[offsets[i]..].ShouldStartWith($"{i + 1} 0 obj");
        }
    }

    [Theory]
    [InlineData(null, QrImageFormat.Png)]
    [InlineData("PNG", QrImageFormat.Png)]
    [InlineData("svg", QrImageFormat.Svg)]
    [InlineData("pdf", QrImageFormat.Pdf)]
    public void Formats_AreParsed(string? input, QrImageFormat expected)
    {
        QrImages.TryParseFormat(input, out var format).ShouldBeTrue();
        format.ShouldBe(expected);
        QrImages.TryParseFormat("gif", out _).ShouldBeFalse();
    }

    [Fact]
    public void PrintedUrls_HaveNoLocale()
    {
        new QrLinkOptions { PublicBaseUrl = "https://trimme.sa/" }.UrlFor("aswn7qkd").ShouldBe("https://trimme.sa/q/aswn7qkd");
        var png = QrImages.Render(Url, "aswn7qkd", QrImageFormat.Png, 100);
        png.ContentType.ShouldBe("image/png");
        png.FileName.ShouldBe("trimme-qr-aswn7qkd.png");
    }

    [GeneratedRegex(@"h(\d+)v1")]
    private static partial Regex SvgRun();

    [GeneratedRegex(@"[\d.]+ [\d.]+ ([\d.]+) [\d.]+ re")]
    private static partial Regex PdfRun();
}
