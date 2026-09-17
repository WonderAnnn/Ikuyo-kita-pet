using IkuyoPet.Core.Bubbles;
using Xunit;

namespace IkuyoPet.Core.Tests.Bubbles;

public sealed class BubbleLayoutCalculatorTests
{
    [Fact]
    public void EmptyTextStillProducesMinimumReadableLayout()
    {
        var theme = BubbleThemeCatalog.BuiltInThemes[0];
        var layout = BubbleLayoutCalculator.Calculate(theme, string.Empty);

        Assert.True(layout.ContentWidth >= 112);
        Assert.True(layout.ContentHeight >= 24);
        Assert.True(layout.WindowWidth > layout.ContentWidth);
        Assert.True(layout.WindowHeight > layout.ContentHeight);
        Assert.True(layout.TextArea.Width > 0);
        Assert.True(layout.TextArea.Height > 0);
    }

    [Fact]
    public void LongChineseTextGrowsWithoutExceedingMaximumContentWidth()
    {
        var theme = BubbleThemeCatalog.BuiltInThemes[0];
        var shortLayout = BubbleLayoutCalculator.Calculate(theme, "今天辛苦啦");
        var longLayout = BubbleLayoutCalculator.Calculate(theme, new string('今', 80));

        Assert.True(longLayout.ContentWidth > shortLayout.ContentWidth || longLayout.ContentHeight > shortLayout.ContentHeight);
        Assert.True(longLayout.ContentWidth >= theme.MinContentWidth);
        Assert.True(longLayout.ContentHeight >= theme.MinContentHeight);
        Assert.True(longLayout.WindowWidth > shortLayout.WindowWidth);
        Assert.True(longLayout.TextArea.Height >= longLayout.LineCount * 24);
    }

    [Fact]
    public void ActionLabelsReserveAdditionalHorizontalSpace()
    {
        var theme = BubbleThemeCatalog.BuiltInThemes[0];
        var withoutActions = BubbleLayoutCalculator.Calculate(theme, "该休息一下啦");
        var withActions = BubbleLayoutCalculator.Calculate(theme, "该休息一下啦", ["现在休息", "稍后提醒"]);

        Assert.True(withActions.ContentWidth > withoutActions.ContentWidth);
    }

    [Fact]
    public void GuitarThemeKeepsTextAreaFurtherRightAndExpandsHorizontallyFirst()
    {
        var chibi = BubbleThemeCatalog.BuiltInThemes[0];
        var guitar = BubbleThemeCatalog.BuiltInThemes[1];
        var text = new string('气', 80);

        Assert.True(guitar.TextSafeArea.Left > chibi.TextSafeArea.Left);

        var chibiLayout = BubbleLayoutCalculator.Calculate(chibi, text);
        var guitarLayout = BubbleLayoutCalculator.Calculate(guitar, text);

        Assert.True(guitarLayout.TextArea.Left / guitarLayout.WindowWidth >
                    chibiLayout.TextArea.Left / chibiLayout.WindowWidth);
        Assert.True(guitarLayout.WindowHeight <= 460);
    }

    [Fact]
    public void CompositeBubbleKeepsTextInsideTheNormalizedPngSafeAreaAtEverySize()
    {
        var theme = BubbleThemeCatalog.BuiltInThemes[1];
        var compact = BubbleLayoutCalculator.Calculate(theme, "短句");
        var expanded = BubbleLayoutCalculator.Calculate(theme, new string('长', 80));

        Assert.True(expanded.TextArea.Width > compact.TextArea.Width);
        Assert.Equal(theme.TextSafeArea.Left, compact.TextArea.Left / compact.WindowWidth, precision: 6);
        Assert.Equal(theme.TextSafeArea.Width, compact.TextArea.Width / compact.WindowWidth, precision: 6);
        Assert.Equal(theme.TextSafeArea.Left, expanded.TextArea.Left / expanded.WindowWidth, precision: 6);
    }
    [Fact]
    public void VeryLongTextAreaCoversEveryEstimatedLine()
    {
        var theme = BubbleThemeCatalog.BuiltInThemes[0];
        var layout = BubbleLayoutCalculator.Calculate(theme, new string('长', 500));

        Assert.True(layout.ContentHeight > 0);
        Assert.True(layout.TextArea.Height <= layout.WindowHeight * theme.TextSafeArea.Height);
    }
    [Fact]
    public void WrappedLineCountAddsEveryExplicitParagraph()
    {
        var theme = BubbleThemeCatalog.BuiltInThemes[0];
        var layout = BubbleLayoutCalculator.Calculate(theme, $"短行\n{new string('长', 60)}\n");

        Assert.True(layout.LineCount >= 3);
        Assert.True(layout.TextArea.Height >= layout.LineCount * 24);
    }

    [Fact]
    public void PathologicalTextUsesTheWindowSafetyHeightCap()
    {
        var theme = BubbleThemeCatalog.BuiltInThemes[0];
        var layout = BubbleLayoutCalculator.Calculate(theme, new string('极', 5000));

        Assert.True(layout.ContentHeight > BubbleLayoutCalculator.ContentHeightHardCap);
        Assert.True(layout.ContentHeight > layout.TextViewportHeight);
        Assert.True(layout.WindowHeight <= 460);
        Assert.True(layout.TextArea.Left + layout.TextArea.Width <= layout.WindowWidth);
        Assert.True(layout.TextArea.Top + layout.TextArea.Height <= layout.WindowHeight);
    }


    [Fact]
    public void VeryLongTextIsScrollableAndNeverTallerThanViewport()
    {
        var theme = BubbleThemeCatalog.BuiltInThemes[0];
        var text = string.Join('，', Enumerable.Repeat("完整保留这一段提醒文字", 120));
        var result = BubbleLayoutCalculator.Calculate(theme, text, maximumWindowHeight: 360);

        Assert.Equal(360, result.WindowHeight);
        Assert.True(result.RequiresVerticalScroll);
        Assert.True(result.ContentHeight > result.TextViewportHeight);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ShortMediumAndLongTextGrowOnlyAsNeededForEveryTheme(int themeIndex)
    {
        var theme = BubbleThemeCatalog.BuiltInThemes[themeIndex];
        var compact = BubbleLayoutCalculator.Calculate(theme, "该休息啦");
        var medium = BubbleLayoutCalculator.Calculate(theme, new string('提', 28));
        var expanded = BubbleLayoutCalculator.Calculate(theme, new string('醒', 90));

        Assert.True(compact.WindowWidth < medium.WindowWidth);
        Assert.True(medium.WindowWidth <= expanded.WindowWidth);
        Assert.True(compact.WindowHeight < medium.WindowHeight);
        Assert.True(medium.WindowHeight <= expanded.WindowHeight);
        Assert.True(expanded.WindowHeight <= 480);
        Assert.Equal((double)theme.PixelWidth / theme.PixelHeight, compact.WindowWidth / compact.WindowHeight, precision: 6);
        Assert.Equal((double)theme.PixelWidth / theme.PixelHeight, expanded.WindowWidth / expanded.WindowHeight, precision: 6);
    }
}
