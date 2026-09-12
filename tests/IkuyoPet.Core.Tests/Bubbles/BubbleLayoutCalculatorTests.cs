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

        Assert.True(layout.ContentWidth >= theme.MinContentWidth);
        Assert.True(layout.ContentHeight >= theme.MinContentHeight);
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
        Assert.InRange(longLayout.ContentWidth, theme.MinContentWidth, theme.MaxContentWidth);
        Assert.InRange(longLayout.ContentHeight, theme.MinContentHeight, theme.MaxContentHeight);
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

        Assert.True(guitarLayout.ContentWidth > chibiLayout.ContentWidth);
        Assert.True(guitarLayout.ContentWidth <= guitar.MaxContentWidth);
    }
    [Fact]
    public void VeryLongTextAreaCoversEveryEstimatedLine()
    {
        var theme = BubbleThemeCatalog.BuiltInThemes[0];
        var layout = BubbleLayoutCalculator.Calculate(theme, new string('长', 500));

        Assert.True(layout.ContentHeight > theme.MaxContentHeight);
        Assert.True(layout.TextArea.Height >= layout.LineCount * 24);
    }
    [Fact]
    public void WrappedLineCountAddsEveryExplicitParagraph()
    {
        var theme = BubbleThemeCatalog.BuiltInThemes[0];
        var layout = BubbleLayoutCalculator.Calculate(theme, $"短行\n{new string('长', 60)}\n");

        Assert.True(layout.LineCount >= 7);
        Assert.True(layout.TextArea.Height >= layout.LineCount * 24);
    }

    [Fact]
    public void PathologicalTextUsesTheWindowSafetyHeightCap()
    {
        var theme = BubbleThemeCatalog.BuiltInThemes[0];
        var layout = BubbleLayoutCalculator.Calculate(theme, new string('极', 5000));

        Assert.Equal(BubbleLayoutCalculator.ContentHeightHardCap, layout.ContentHeight);
        Assert.Equal(theme.MaxContentWidth, layout.ContentWidth);
    }
}
