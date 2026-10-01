using Shouldly;
using SkiaSharp;
using Trimme.BuildingBlocks.Infrastructure.Media;
using Trimme.Tests.Shared;

namespace Trimme.UnitTests.BuildingBlocks.Media;

/// <summary>D-119 (Phase 17): uploads are stored as fresh files encoded from their pixels.</summary>
public sealed class ImageReencoderTests
{
    [Fact]
    public void Jpeg_KeepsOnlyPixels_AndAppliesTheExifOrientation()
    {
        var upload = TestImages.EncodedJpeg(1600, 900, orientation: 6);
        TestImages.Contains(upload, TestImages.ExifMarker).ShouldBeTrue("the probe carries metadata");

        var stored = Reencode(upload).ShouldNotBeNull();

        stored.ContentType.ShouldBe(ImageSanitizer.Jpeg);
        (stored.Width, stored.Height).ShouldBe((900, 1600), "rotated 90° as the orientation says, since the tag is gone");
        TestImages.Contains(stored.Bytes, TestImages.ExifMarker).ShouldBeFalse();
        TestImages.Contains(stored.Bytes, "Exif\0\0"u8.ToArray()).ShouldBeFalse("no EXIF segment at all");

        // The left (red) half of the stored pixels is now the top half.
        using var bitmap = SKBitmap.Decode(stored.Bytes);
        (bitmap.Width, bitmap.Height).ShouldBe((900, 1600));
        bitmap.GetPixel(450, 300).Red.ShouldBeGreaterThan((byte)200);
        bitmap.GetPixel(450, 1300).Blue.ShouldBeGreaterThan((byte)200);
    }

    [Theory]
    [InlineData(3, 1600, 900)]
    [InlineData(8, 900, 1600)]
    [InlineData(2, 1600, 900)]
    public void OtherOrientations_GiveTheViewedSize(ushort orientation, int width, int height)
    {
        var stored = Reencode(TestImages.EncodedJpeg(1600, 900, orientation)).ShouldNotBeNull();
        (stored.Width, stored.Height).ShouldBe((width, height));
    }

    [Fact]
    public void LargeImages_AreScaledDownToTheStoredMaximum()
    {
        var stored = Reencode(TestImages.EncodedJpeg(4000, 3000)).ShouldNotBeNull();
        (stored.Width, stored.Height).ShouldBe((ImageReencoder.MaxStoredDimension, 1920));

        var png = Reencode(TestImages.Png(3000, 1500)).ShouldNotBeNull();
        (png.ContentType, png.Width, png.Height).ShouldBe((ImageSanitizer.Png, ImageReencoder.MaxStoredDimension, 1280));
    }

    [Fact]
    public void APolyglot_LosesItsPayload()
    {
        var upload = TestImages.PolyglotPng(400, 300);

        var stored = Reencode(upload).ShouldNotBeNull();

        TestImages.Contains(stored.Bytes, TestImages.PolyglotPayload).ShouldBeFalse();
        using var bitmap = SKBitmap.Decode(stored.Bytes);
        (bitmap.Width, bitmap.Height).ShouldBe((400, 300));
    }

    [Fact]
    public void WebP_IsReencodedAsWebP()
    {
        var stored = Reencode(TestImages.EncodedWebP(640, 480)).ShouldNotBeNull();
        (stored.ContentType, stored.Width, stored.Height).ShouldBe((ImageSanitizer.WebP, 640, 480));
        stored.Bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8).ShouldBeTrue();
    }

    [Fact]
    public void FilesThatDoNotDecode_AreRefused()
    {
        // Structurally valid headers with no real image data behind them.
        Reencode(TestImages.Jpeg(1600, 900)).ShouldBeNull();
        Reencode(TestImages.WebP(640, 480)).ShouldBeNull();

        // A real PNG cut short.
        var png = TestImages.Png(400, 300);
        Reencode(png[..(png.Length / 2)]).ShouldBeNull();
    }

    [Fact]
    public void AHeaderThatDisagreesWithTheCodec_IsRefused()
    {
        var png = TestImages.Png(400, 300);
        var header = ImageSanitizer.TrySanitize(png).ShouldNotBeNull();

        ImageReencoder.TryReencode(png, header with { Width = 401 }).ShouldBeNull();
        ImageReencoder.TryReencode(png, header with { ContentType = ImageSanitizer.Jpeg }).ShouldBeNull();
    }

    private static SanitizedImage? Reencode(byte[] upload) =>
        ImageSanitizer.TrySanitize(upload) is { } header ? ImageReencoder.TryReencode(upload, header) : null;
}
