using System.Windows;
using IkuyoPet.Core.Bubbles;
using IkuyoPet.Pet;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.Pet;

public sealed class BubbleRendererMatrixTests
{
    public static TheoryData<int, string, double> Cases
    {
        get
        {
            var data = new TheoryData<int, string, double>();
            var texts = new[] { "休息啦", new string('提', 28), new string('醒', 90), new string('长', 500) };
            foreach (var theme in Enumerable.Range(0, 3))
            foreach (var text in texts)
            foreach (var dpi in new[] { 1d, 1.25d, 1.5d })
                data.Add(theme, text, dpi);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void SharedRenderModelKeepsTextInsideFilledPngAtEveryDpi(int themeIndex, string text, double dpi)
    {
        var theme = BubbleThemeCatalog.BuiltInThemes[themeIndex];
        var model = BubbleRenderer.Create(theme, null, text, null, dpi);
        var bounds = model.TextBounds;

        Assert.Equal((double)theme.PixelWidth / theme.PixelHeight,
            model.LogicalSize.Width / model.LogicalSize.Height, precision: 6);
        Assert.Equal(theme.TextSafeArea.Left, bounds.Left / model.LogicalSize.Width, precision: 6);
        Assert.Equal(theme.TextSafeArea.Top, bounds.Top / model.LogicalSize.Height, precision: 6);
        Assert.True(bounds.Left >= 0 && bounds.Top >= 0);
        Assert.True(bounds.Right <= model.LogicalSize.Width);
        Assert.True(bounds.Bottom <= model.LogicalSize.Height);
        Assert.True(model.Layout.WindowHeight <= 460);
        Assert.True(model.Layout.TextViewportHeight > 0);
        Assert.Equal(bounds.Left * dpi, model.PixelTextBounds.Left, precision: 6);
        Assert.Equal(bounds.Top * dpi, model.PixelTextBounds.Top, precision: 6);
        Assert.Equal(model.LogicalSize.Width * dpi, model.PixelSize.Width, precision: 6);
        Assert.EndsWith("bubble-filled.png", theme.ResourcePath);
    }
}
