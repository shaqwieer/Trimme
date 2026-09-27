using System.Text;
using Shouldly;
using Trimme.BuildingBlocks.Infrastructure.Media;
using Trimme.Tests.Shared;

namespace Trimme.UnitTests.BuildingBlocks.Media;

/// <summary>D-064: images are recognised by their bytes, measured from their headers, and stripped of metadata.</summary>
public sealed class ImageSanitizerTests
{
    [Fact]
    public void Png_IsRecognised_Measured_AndTextChunksRemoved()
    {
        var png = TestImages.Png(640, 360, withTextChunk: true);
        TestImages.Contains(png, Encoding.ASCII.GetBytes("GPSLatitude")).ShouldBeTrue("the probe carries metadata");

        var image = ImageSanitizer.TrySanitize(png).ShouldNotBeNull();

        image.ContentType.ShouldBe("image/png");
        (image.Width, image.Height).ShouldBe((640, 360));
        TestImages.Contains(image.Bytes, Encoding.ASCII.GetBytes("GPSLatitude")).ShouldBeFalse();
        image.Bytes.ShouldBe(TestImages.Png(640, 360), "only the metadata chunk is removed");
    }

    [Fact]
    public void Jpeg_ExifSegmentIsRemoved_AndSizeReadFromFrameHeader()
    {
        var jpeg = TestImages.Jpeg(1600, 900);

        var image = ImageSanitizer.TrySanitize(jpeg).ShouldNotBeNull();

        image.ContentType.ShouldBe("image/jpeg");
        (image.Width, image.Height).ShouldBe((1600, 900));
        TestImages.Contains(image.Bytes, TestImages.ExifMarker).ShouldBeFalse();
        TestImages.Contains(image.Bytes, "JFIF"u8.ToArray()).ShouldBeTrue("APP0 is kept");
        image.Bytes.Length.ShouldBe(jpeg.Length - (TestImages.ExifMarker.Length + 4));
    }

    [Fact]
    public void WebP_ExifChunkIsRemoved_FlagCleared_AndRiffSizeFixed()
    {
        var webp = TestImages.WebP(800, 600);

        var image = ImageSanitizer.TrySanitize(webp).ShouldNotBeNull();

        image.ContentType.ShouldBe("image/webp");
        (image.Width, image.Height).ShouldBe((800, 600));
        TestImages.Contains(image.Bytes, TestImages.ExifMarker).ShouldBeFalse();
        BitConverter.ToUInt32(image.Bytes, 4).ShouldBe((uint)(image.Bytes.Length - 8));
        (image.Bytes[20] & 0x08).ShouldBe(0, "the VP8X EXIF flag no longer claims an EXIF chunk");
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"/>")]
    [InlineData("<!DOCTYPE html><html><script>alert(1)</script></html>")]
    [InlineData("GIF89a\u0001\u0000\u0001\u0000")]
    [InlineData("")]
    public void NonImages_AreRejected(string content) =>
        ImageSanitizer.TrySanitize(Encoding.UTF8.GetBytes(content)).ShouldBeNull();

    [Fact]
    public void TruncatedOrMalformedFiles_AreRejected()
    {
        var png = TestImages.Png(200, 200);
        ImageSanitizer.TrySanitize(png.AsSpan(0, 40)).ShouldBeNull("no IEND");

        var jpeg = TestImages.Jpeg(200, 200);
        ImageSanitizer.TrySanitize(jpeg.AsSpan(0, 12)).ShouldBeNull("segment length runs past the end");

        var html = Encoding.UTF8.GetBytes("<html>");
        ImageSanitizer.TrySanitize([.. png.AsSpan(0, 8), .. html]).ShouldBeNull("PNG signature followed by HTML");
    }
}
