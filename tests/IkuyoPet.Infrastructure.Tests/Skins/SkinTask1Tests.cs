using System.IO;
using IkuyoPet.Infrastructure.Storage;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IkuyoPet.Core.Skins;
using IkuyoPet.Pet.Skins;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.Skins;

public sealed class SkinTask1Tests
{
    [Fact]
    public void RejectsManifestWhenActualPngSizeDoesNotMatch()
    {
        var directory = Directory.CreateTempSubdirectory("ikuyo-skin-task1-").FullName;
        try
        {
            WriteManifest(directory, 406, 996);
            WritePng(Path.Combine(directory, "idle.png"), 405, 996, transparent: true);
            WritePng(Path.Combine(directory, "remind.png"), 406, 996, transparent: true);

            var result = new SkinPackageValidator().Validate(directory);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.Contains("idle.png", StringComparison.Ordinal));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void AcceptsTransparent406x996Package()
    {
        var directory = Directory.CreateTempSubdirectory("ikuyo-skin-task1-").FullName;
        try
        {
            WriteManifest(directory, 406, 996);
            WritePng(Path.Combine(directory, "idle.png"), 406, 996, transparent: true);
            WritePng(Path.Combine(directory, "remind.png"), 406, 996, transparent: true);

            var result = new SkinPackageValidator().Validate(directory);

            Assert.True(result.IsValid);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task SelectionStoreRoundTripsTheCurrentSkin()
    {
        var root = Directory.CreateTempSubdirectory("ikuyo-selection-").FullName;
        try
        {
            var store = new SkinSelectionStore(root);
            await store.SaveAsync(new SkinSelection("user.ikuyo-local", "1.0.0"), TestContext.Current.CancellationToken);

            var selection = await store.LoadAsync(TestContext.Current.CancellationToken);

            Assert.Equal(new SkinSelection("user.ikuyo-local", "1.0.0"), selection);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void CalculatesUniformPortraitSizeWithoutStretching()
    {
        var size = SkinLayout.CalculateUniformSize(406, 996, 160, 280);

        Assert.Equal(114.14, size.Width, 2);
        Assert.Equal(280, size.Height, 2);
    }

    [Fact]
    public void RejectsFakePngSignature()
    {
        var directory = Directory.CreateTempSubdirectory("ikuyo-skin-task1-").FullName;
        try
        {
            WriteManifest(directory, 406, 996);
            File.WriteAllText(Path.Combine(directory, "idle.png"), "not-a-png");
            File.WriteAllText(Path.Combine(directory, "remind.png"), "not-a-png");
            var result = new SkinPackageValidator().Validate(directory);
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.Contains("not a PNG", StringComparison.Ordinal));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void BootstrapperRejectsPathEscapeAndReturnsDiagnosticFallback()
    {
        var root = Directory.CreateTempSubdirectory("ikuyo-skin-root-").FullName;
        try
        {
            var bootstrapper = new SkinBootstrapper(new SkinPackageValidator(), root);
            var traversal = bootstrapper.ResolveWithDiagnostics(new SkinSelection("..", "1.0.0"));
            Assert.True(traversal.UsesPlaceholder);
            Assert.False(string.IsNullOrWhiteSpace(traversal.Error));

            var missing = bootstrapper.ResolveWithDiagnostics(new SkinSelection("missing", "1.0.0"));
            Assert.True(missing.UsesPlaceholder);
            Assert.Contains("占位符", missing.Error, StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    [Fact]
    public void CorruptManifestReturnsDiagnosticPlaceholder()
    {
        var root = Directory.CreateTempSubdirectory("ikuyo-skin-root-").FullName;
        try
        {
            var package = Path.Combine(root, "broken", "1.0.0");
            Directory.CreateDirectory(package);
            File.WriteAllText(Path.Combine(package, "manifest.json"), "{broken");
            var result = new SkinBootstrapper(new SkinPackageValidator(), root)
                .ResolveWithDiagnostics(new SkinSelection("broken", "1.0.0"));
            Assert.True(result.UsesPlaceholder);
            Assert.Contains("皮肤加载失败", result.Error, StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    private static void WriteManifest(string directory, int width, int height) =>
        File.WriteAllText(Path.Combine(directory, "manifest.json"), $$"""
            {"id":"task1","name":"测试","version":"1.0.0","author":"test","license":"test","canvasWidth":{{width}},"canvasHeight":{{height}},"fps":1}
            """);

    private static void WritePng(string path, int width, int height, bool transparent)
    {
        var pixels = new byte[width * height * 4];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = 255;
            pixels[index + 1] = 128;
            pixels[index + 2] = 196;
            pixels[index + 3] = transparent ? (byte)0 : (byte)255;
        }

        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        encoder.Save(stream);
    }
}