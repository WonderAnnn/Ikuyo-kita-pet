using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.Bubbles;

public sealed class BubbleAssetsContractTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    [Fact]
    public void ThreeBuiltInManifestsDeclareRequiredAdaptiveLayoutMetadata()
    {
        foreach (var id in new[] { "cloud-chibi", "cloud-guitar", "cloud-smile" })
        {
            var directory = Path.Combine(Root, "assets", "bubbles", id);
            var manifestPath = Path.Combine(directory, "manifest.json");
            Assert.True(File.Exists(manifestPath), $"Missing {manifestPath}");
            Assert.True(File.Exists(Path.Combine(directory, "bubble.png")), $"Missing bubble image for {id}");
            Assert.True(File.Exists(Path.Combine(directory, "bubble-source.png")), $"Missing preserved source image for {id}");
            Assert.True(File.Exists(Path.Combine(directory, "bubble-filled.png")), $"Missing filled bubble image for {id}");

            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var manifest = document.RootElement;
            foreach (var field in new[] { "id", "name", "version", "resourcePath", "previewPath", "pixelWidth", "pixelHeight", "sliceInsets", "textSafeArea", "minContentSize", "maxContentSize" })
            {
                Assert.True(manifest.TryGetProperty(field, out _), $"{id} manifest lacks {field}");
            }
            Assert.EndsWith("bubble-filled.png", manifest.GetProperty("resourcePath").GetString());
            Assert.EndsWith("bubble-filled.png", manifest.GetProperty("previewPath").GetString());
        }
    }

    [Theory]
    [InlineData("cloud-chibi", false)]
    [InlineData("cloud-guitar", true)]
    [InlineData("cloud-smile", true)]
    public void FilledBubbleKeepsOutsideTransparentAndOriginalArtworkUnchanged(
        string id,
        bool expectsFilledPixels)
    {
        var directory = Path.Combine(Root, "assets", "bubbles", id);
        var sourcePath = Path.Combine(directory, "bubble-source.png");
        var filledPath = Path.Combine(directory, "bubble-filled.png");
        Assert.True(File.Exists(sourcePath));
        Assert.True(File.Exists(filledPath));

        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")));
        var safe = document.RootElement.GetProperty("textSafeArea");
        var source = ReadBgra(sourcePath);
        var filled = ReadBgra(filledPath);
        Assert.Equal(source.Width, filled.Width);
        Assert.Equal(source.Height, filled.Height);

        var centerX = (int)((safe.GetProperty("left").GetDouble() + safe.GetProperty("width").GetDouble() / 2) * source.Width);
        var centerY = (int)((safe.GetProperty("top").GetDouble() + safe.GetProperty("height").GetDouble() / 2) * source.Height);
        Assert.Equal(255, filled.Pixels[(centerY * filled.Width + centerX) * 4 + 3]);
        Assert.Equal(0, filled.Pixels[3]);

        var changedTransparentPixels = 0;
        for (var offset = 0; offset < source.Pixels.Length; offset += 4)
        {
            if (source.Pixels[offset + 3] != 0)
            {
                Assert.Equal(source.Pixels[offset], filled.Pixels[offset]);
                Assert.Equal(source.Pixels[offset + 1], filled.Pixels[offset + 1]);
                Assert.Equal(source.Pixels[offset + 2], filled.Pixels[offset + 2]);
                Assert.Equal(source.Pixels[offset + 3], filled.Pixels[offset + 3]);
            }
            else if (filled.Pixels[offset + 3] == 255)
            {
                changedTransparentPixels++;
                Assert.Equal(255, filled.Pixels[offset]);
                Assert.Equal(255, filled.Pixels[offset + 1]);
                Assert.Equal(255, filled.Pixels[offset + 2]);
            }
        }
        Assert.Equal(expectsFilledPixels, changedTransparentPixels > 0);
    }

    private static (int Width, int Height, byte[] Pixels) ReadBgra(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        return (converted.PixelWidth, converted.PixelHeight, pixels);
    }
}
