using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IkuyoPet.Pet.Skins;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.Skins;

public sealed class SkinPackageValidatorTests
{
    [Fact]
    public void RejectsPackageWhenRequiredImageIsMissing()
    {
        var directory = CreatePackageDirectory();
        try
        {
            WriteManifest(directory, canvasWidth: 160, canvasHeight: 160, fps: 12);
            WritePng(Path.Combine(directory, "idle.png"), hasTransparentPixel: true);

            var result = new SkinPackageValidator().Validate(directory);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.Contains("remind.png", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RejectsPackageWhenCanvasOrFpsIsOutOfRange()
    {
        var directory = CreatePackageDirectory();
        try
        {
            WriteManifest(directory, canvasWidth: 16, canvasHeight: 160, fps: 31);
            WritePng(Path.Combine(directory, "idle.png"), hasTransparentPixel: true);
            WritePng(Path.Combine(directory, "remind.png"), hasTransparentPixel: true);

            var result = new SkinPackageValidator().Validate(directory);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.Contains("canvasWidth", StringComparison.Ordinal));
            Assert.Contains(result.Errors, error => error.Contains("fps", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RejectsOpaquePngWithoutAlphaChannel()
    {
        var directory = CreatePackageDirectory();
        try
        {
            WriteManifest(directory, canvasWidth: 160, canvasHeight: 160, fps: 12);
            WritePng(Path.Combine(directory, "idle.png"), hasTransparentPixel: false);
            WritePng(Path.Combine(directory, "remind.png"), hasTransparentPixel: true);

            var result = new SkinPackageValidator().Validate(directory);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.Contains("idle.png", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AcceptsValidTransparentPackageAndReturnsManifest()
    {
        var directory = CreatePackageDirectory();
        try
        {
            WriteManifest(directory, canvasWidth: 160, canvasHeight: 160, fps: 12);
            WritePng(Path.Combine(directory, "idle.png"), hasTransparentPixel: true);
            WritePng(Path.Combine(directory, "remind.png"), hasTransparentPixel: true);

            var result = new SkinPackageValidator().Validate(directory);

            Assert.True(result.IsValid);
            Assert.Equal("default", result.Manifest!.Id);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreatePackageDirectory() =>
        Directory.CreateTempSubdirectory("ikuyo-skin-").FullName;

    private static void WriteManifest(string directory, int canvasWidth, int canvasHeight, int fps) =>
        File.WriteAllText(
            Path.Combine(directory, "manifest.json"),
            $$"""
            {"id":"default","name":"测试皮肤","version":"1.0.0","author":"test","license":"MIT","canvasWidth":{{canvasWidth}},"canvasHeight":{{canvasHeight}},"fps":{{fps}}}
            """);

    private static void WritePng(string path, bool hasTransparentPixel)
    {
        var alpha = hasTransparentPixel ? (byte)0 : (byte)255;
        var pixels = new[] { (byte)255, (byte)128, (byte)196, alpha };
        var source = BitmapSource.Create(
            1,
            1,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            4);

        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        encoder.Save(stream);
    }
}
