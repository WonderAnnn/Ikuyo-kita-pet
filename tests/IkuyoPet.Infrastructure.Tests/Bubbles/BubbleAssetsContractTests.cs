using System.Text.Json;
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

            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var manifest = document.RootElement;
            foreach (var field in new[] { "id", "name", "version", "resourcePath", "previewPath", "pixelWidth", "pixelHeight", "sliceInsets", "textSafeArea", "minContentSize", "maxContentSize" })
            {
                Assert.True(manifest.TryGetProperty(field, out _), $"{id} manifest lacks {field}");
            }
        }
    }
}
