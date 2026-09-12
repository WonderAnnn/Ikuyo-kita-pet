using IkuyoPet.Infrastructure.Storage;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.Bubbles;

public sealed class BubbleThemeSelectionStoreTests
{
    [Fact]
    public async Task SelectionRoundTripsInDedicatedBubbleThemeFile()
    {
        var root = Directory.CreateTempSubdirectory("ikuyo-bubble-selection-").FullName;
        try
        {
            var store = new BubbleThemeSelectionStore(root);
            await store.SaveAsync("cloud-guitar", TestContext.Current.CancellationToken);

            Assert.Equal("cloud-guitar", await store.LoadAsync(TestContext.Current.CancellationToken));
            Assert.True(File.Exists(Path.Combine(root, "bubble-theme.json")));
            Assert.False(Directory.EnumerateFiles(root, ".bubble-theme.*.tmp").Any());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CorruptOrUnknownSelectionLoadsAsNull()
    {
        var root = Directory.CreateTempSubdirectory("ikuyo-bubble-selection-").FullName;
        try
        {
            var path = Path.Combine(root, "bubble-theme.json");
            File.WriteAllText(path, "{broken");
            var store = new BubbleThemeSelectionStore(root);
            Assert.Null(await store.LoadAsync(TestContext.Current.CancellationToken));

            File.WriteAllText(path, "{\"id\":\"unknown\"}");
            Assert.Null(await store.LoadAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task UnsafeSelectionCannotEscapeRootDirectory()
    {
        var root = Directory.CreateTempSubdirectory("ikuyo-bubble-selection-").FullName;
        try
        {
            var store = new BubbleThemeSelectionStore(root);
            await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync("..", TestContext.Current.CancellationToken));
            Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(root)!, "bubble-theme.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
