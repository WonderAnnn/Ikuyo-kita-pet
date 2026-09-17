using IkuyoPet.Core.Bubbles;
using Xunit;

namespace IkuyoPet.Core.Tests.Bubbles;

public sealed class BubbleThemeCatalogTests
{
    [Fact]
    public void BuiltInCatalogExposesExactlyThreeStableThemes()
    {
        var themes = BubbleThemeCatalog.BuiltInThemes;

        Assert.Equal(3, themes.Count);
        Assert.Equal(["cloud-chibi", "cloud-guitar", "cloud-smile"], themes.Select(theme => theme.Id));
        Assert.All(themes, theme =>
        {
            Assert.False(string.IsNullOrWhiteSpace(theme.Name));
            Assert.False(string.IsNullOrWhiteSpace(theme.Version));
            Assert.False(string.IsNullOrWhiteSpace(theme.ResourcePath));
            Assert.True(theme.PixelWidth > 0);
            Assert.True(theme.PixelHeight > 0);
        });
    }

    [Fact]
    public void UnknownOrMissingThemeResolvesToFirstBuiltInTheme()
    {
        var root = Directory.CreateTempSubdirectory("ikuyo-bubbles-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "cloud-chibi"));
            File.WriteAllBytes(Path.Combine(root, "cloud-chibi", "bubble.png"), [1]);
            var catalog = new BubbleThemeCatalog(root);

            Assert.Same(BubbleThemeCatalog.BuiltInThemes[0], catalog.Resolve("not-a-theme"));
            Assert.Same(BubbleThemeCatalog.BuiltInThemes[0], catalog.Resolve("cloud-guitar"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
    [Fact]
    public void ThemeDimensionsRejectNonFiniteValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BubbleSafeArea(double.NaN, 0.2, 0.4, 0.4));
        var template = BubbleThemeCatalog.BuiltInThemes[0];
        Assert.Throws<ArgumentOutOfRangeException>(() => template with { MinContentWidth = double.PositiveInfinity });
        Assert.Throws<ArgumentOutOfRangeException>(() => template with { MaxContentHeight = double.NaN });
    }
}
