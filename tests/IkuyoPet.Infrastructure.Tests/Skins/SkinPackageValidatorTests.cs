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

    [Fact]
    public async Task InvalidImportLeavesCurrentSkinAndStagingClean()
    {
        var source = CreateInvalidPackage();
        var root = Directory.CreateTempSubdirectory("ikuyo-skins-").FullName;
        var current = Path.Combine(root, "default", "1.0.0");
        Directory.CreateDirectory(current);
        File.WriteAllText(Path.Combine(current, "keep.txt"), "keep");
        try
        {
            var result = await new SkinPackageImporter(new SkinPackageValidator(), root)
                .ImportAsync(source, TestContext.Current.CancellationToken);

            Assert.False(result.IsImported);
            Assert.Equal("keep", File.ReadAllText(Path.Combine(current, "keep.txt")));
            var staging = Path.Combine(root, ".staging");
            Assert.True(!Directory.Exists(staging) || !Directory.EnumerateDirectories(staging).Any());
        }
        finally
        {
            Directory.Delete(source, recursive: true);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ValidImportMovesStagedPackageToVersionedDirectory()
    {
        var source = CreateValidPackage("default", "1.0.0", "first");
        var root = Directory.CreateTempSubdirectory("ikuyo-skins-").FullName;
        try
        {
            var result = await new SkinPackageImporter(new SkinPackageValidator(), root)
                .ImportAsync(source, TestContext.Current.CancellationToken);

            Assert.True(result.IsImported);
            Assert.NotNull(result.InstalledDirectory);
            Assert.True(File.Exists(Path.Combine(result.InstalledDirectory!, "manifest.json")));
            Assert.Equal("first", File.ReadAllText(Path.Combine(result.InstalledDirectory!, "payload.txt")));
        }
        finally
        {
            Directory.Delete(source, recursive: true);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ImportWithExistingIdAndVersionDoesNotOverwriteExistingFiles()
    {
        var first = CreateValidPackage("default", "1.0.0", "first");
        var second = CreateValidPackage("default", "1.0.0", "second");
        var root = Directory.CreateTempSubdirectory("ikuyo-skins-").FullName;
        try
        {
            var importer = new SkinPackageImporter(new SkinPackageValidator(), root);
            var firstResult = await importer.ImportAsync(first, TestContext.Current.CancellationToken);
            var secondResult = await importer.ImportAsync(second, TestContext.Current.CancellationToken);

            Assert.True(firstResult.IsImported);
            Assert.True(secondResult.IsImported);
            Assert.NotEqual(firstResult.InstalledDirectory, secondResult.InstalledDirectory);
            Assert.Equal(
                "first",
                File.ReadAllText(Path.Combine(firstResult.InstalledDirectory!, "payload.txt")));
            Assert.Equal(
                "second",
                File.ReadAllText(Path.Combine(secondResult.InstalledDirectory!, "payload.txt")));
        }
        finally
        {
            Directory.Delete(first, recursive: true);
            Directory.Delete(second, recursive: true);
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreatePackageDirectory() =>
        Directory.CreateTempSubdirectory("ikuyo-skin-").FullName;

    private static string CreateInvalidPackage()
    {
        var directory = CreatePackageDirectory();
        WriteManifest(directory, canvasWidth: 16, canvasHeight: 160, fps: 12);
        WritePng(Path.Combine(directory, "idle.png"), hasTransparentPixel: true);
        return directory;
    }

    private static string CreateValidPackage(string id, string version, string payload)
    {
        var directory = CreatePackageDirectory();
        WriteManifest(directory, canvasWidth: 160, canvasHeight: 160, fps: 12, id, version);
        WritePng(Path.Combine(directory, "idle.png"), hasTransparentPixel: true);
        WritePng(Path.Combine(directory, "remind.png"), hasTransparentPixel: true);
        File.WriteAllText(Path.Combine(directory, "payload.txt"), payload);
        return directory;
    }

    private static void WriteManifest(
        string directory,
        int canvasWidth,
        int canvasHeight,
        int fps,
        string id = "default",
        string version = "1.0.0") =>
        File.WriteAllText(
            Path.Combine(directory, "manifest.json"),
            $$"""
            {"id":"{{id}}","name":"测试皮肤","version":"{{version}}","author":"test","license":"MIT","canvasWidth":{{canvasWidth}},"canvasHeight":{{canvasHeight}},"fps":{{fps}}}
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
