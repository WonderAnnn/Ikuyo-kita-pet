using System.IO;
using System.Windows;
using System.Xml.Linq;
using IkuyoPet.Core.Bubbles;
using IkuyoPet.Infrastructure.Tests.TestSupport;
using IkuyoPet.Pet;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.Pet;

public sealed class BubbleChromeContractTests
{
    [Fact]
    public void NineSliceLayoutKeepsCornersFixedAndOnlyStretchesEdgesOnOneAxis()
    {
        var slices = BubbleChrome.CreateSlices(
            new Size(100, 80),
            new Thickness(10, 8, 12, 6),
            new Size(180, 130));

        Assert.Equal(9, slices.Count);

        Assert.Equal(new Rect(0, 0, 10, 8), slices[0].Source);
        Assert.Equal(new Rect(0, 0, 10, 8), slices[0].Destination);
        Assert.Equal(new Rect(88, 0, 12, 8), slices[2].Source);
        Assert.Equal(new Rect(168, 0, 12, 8), slices[2].Destination);

        Assert.Equal(slices[1].Source.Height, slices[1].Destination.Height);
        Assert.NotEqual(slices[1].Source.Width, slices[1].Destination.Width);
        Assert.Equal(slices[3].Source.Width, slices[3].Destination.Width);
        Assert.NotEqual(slices[3].Source.Height, slices[3].Destination.Height);
    }

    [Fact]
    public void LogicalCornerInsetsStayFixedWhenTheChromeGrows()
    {
        var sourceInsets = new Thickness(200, 120, 180, 100);
        var logicalInsets = new Thickness(20, 12, 18, 10);

        var compact = BubbleChrome.CreateSlices(
            new Size(1000, 800), sourceInsets, logicalInsets, new Size(300, 180));
        var expanded = BubbleChrome.CreateSlices(
            new Size(1000, 800), sourceInsets, logicalInsets, new Size(620, 360));

        Assert.Equal(new Rect(0, 0, 20, 12), compact[0].Destination);
        Assert.Equal(compact[0].Destination.Size, expanded[0].Destination.Size);
        Assert.Equal(compact[1].Destination.Height, expanded[1].Destination.Height);
        Assert.NotEqual(compact[1].Destination.Width, expanded[1].Destination.Width);
        Assert.Equal(compact[3].Destination.Width, expanded[3].Destination.Width);
        Assert.NotEqual(compact[3].Destination.Height, expanded[3].Destination.Height);
    }

    [Fact]
    public void DecorationLayerPreservesAspectRatioAndStaysLeftAnchoredWhenBackgroundGrows()
    {
        var compact = BubbleChrome.CalculateDecorationBounds(
            new Size(2048, 2048), new Size(376, 132), new Size(376, 132));
        var expanded = BubbleChrome.CalculateDecorationBounds(
            new Size(2048, 2048), new Size(816, 364), new Size(376, 132));

        Assert.Equal(1, compact.Width / compact.Height, precision: 6);
        Assert.Equal(1, expanded.Width / expanded.Height, precision: 6);
        Assert.Equal(compact.Size, expanded.Size);
        Assert.Equal(0, expanded.Left);
    }

    [Fact]
    public void InteriorExtensionKeepsItsThemeInsetsAndAddsNoOuterWhiteFrame()
    {
        var insets = new Thickness(120, 38, 42, 36);
        var compact = BubbleChrome.CalculateInteriorBounds(new Size(376, 132), insets);
        var expanded = BubbleChrome.CalculateInteriorBounds(new Size(816, 364), insets);

        Assert.Equal(compact.Left, expanded.Left);
        Assert.Equal(compact.Top, expanded.Top);
        Assert.Equal(440, expanded.Width - compact.Width);
        Assert.Equal(232, expanded.Height - compact.Height);
    }

    [Fact]
    public void GuitarDecorationReachesPastTheTextAreaOriginWithoutChangingAspectRatio()
    {
        const double outerLeftPadding = 28;
        const double interiorOverlap = 24;
        var theme = BubbleThemeCatalog.BuiltInThemes[1];
        var layout = BubbleLayoutCalculator.Calculate(theme, "短句");
        var sourceSize = new Size(theme.PixelWidth, theme.PixelHeight);
        var bubbleSize = new Size(layout.WindowWidth, layout.WindowHeight);
        var textOrigin = outerLeftPadding + layout.TextArea.Left;
        var viewport = BubbleChrome.CalculateDecorationViewport(
            sourceSize, bubbleSize, textOrigin, interiorOverlap);
        var decoration = BubbleChrome.CalculateDecorationBounds(sourceSize, bubbleSize, viewport);

        Assert.True(decoration.Right >= textOrigin + interiorOverlap);
        Assert.Equal(sourceSize.Width / sourceSize.Height, decoration.Width / decoration.Height, precision: 6);
    }
    [Fact]
    public void PetWindowUsesOneDecorativeChromeWithTextAboveIt()
    {
        var path = Path.Combine(RepositoryPaths.Root, "src", "IkuyoPet.Pet", "PetWindow.xaml");
        var xaml = File.ReadAllText(path);
        var document = XDocument.Load(path);
        var bubbleRoot = document.Descendants().Single(element =>
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "BubbleRoot"));
        var reminderText = document.Descendants().Single(element =>
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "ReminderText"));
        var reminderScroll = document.Descendants().Single(element =>
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "ReminderScroll"));

        Assert.Contains("local:BubbleChrome", xaml);
        Assert.Equal("BubbleChrome", bubbleRoot.Name.LocalName);
        Assert.Same(bubbleRoot, reminderScroll.Parent);
        Assert.Same(reminderScroll, reminderText.Parent);
        Assert.Equal("ScrollViewer", reminderScroll.Name.LocalName);
        Assert.Contains("Panel.ZIndex=\"20\"", xaml);
        Assert.Contains("TextAlignment=\"Left\"", xaml);
        Assert.Contains("VerticalAlignment=\"Top\"", xaml);
        Assert.Contains("ClipToBounds=\"True\"", xaml);
        Assert.DoesNotContain("x:Name=\"BubbleDecoration\"", xaml);
        Assert.DoesNotContain("BubbleArrow", xaml);
        Assert.DoesNotContain("CornerRadius=", xaml);
    }

    [Fact]
    public void PetWindowExposesThemeSetterAndAppliesCoreLayoutToEveryBubbleState()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryPaths.Root, "src", "IkuyoPet.Pet", "PetWindow.xaml.cs"));

        Assert.Contains("public void SetBubbleTheme(BubbleThemeDefinition theme, string resourcePath)", source);
        Assert.Contains("BubbleRenderer.Create", source);
        Assert.Contains("BubbleRoot.ApplyRender", source);
        Assert.Contains("ApplyBubbleLayout(text", source);
        Assert.Contains("ApplyBubbleLayout(view.Text", source);
        Assert.DoesNotContain("Canvas.SetLeft(ReminderText", source);
        Assert.DoesNotContain("Canvas.SetTop(ReminderText", source);
        Assert.Contains("BubbleThemeCatalog.BuiltInThemes[0]", source);
        Assert.Contains("Path.Combine(AppContext.BaseDirectory, \"assets\", \"bubbles\")", source);
    }
    [Fact]
    public void PreviewAndPetWindowUseTheSameRendererAndChromeEntryPoint()
    {
        var preview = File.ReadAllText(Path.Combine(
            RepositoryPaths.Root, "src", "IkuyoPet.Pet", "BubbleThemePreview.cs"));
        var window = File.ReadAllText(Path.Combine(
            RepositoryPaths.Root, "src", "IkuyoPet.Pet", "PetWindow.xaml.cs"));

        Assert.Contains("BubbleRenderer.Create", preview);
        Assert.Contains("chrome.ApplyRender(model)", preview);
        Assert.Contains("BubbleRenderer.Create", window);
        Assert.Contains("BubbleRoot.ApplyRender(render)", window);
    }
    [Fact]
    public void PublicSliceGeometryRejectsNonFiniteInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BubbleChrome.CreateSlices(
            new Size(double.NaN, 80), new Thickness(10), new Size(180, 130)));
        Assert.Throws<ArgumentOutOfRangeException>(() => BubbleChrome.CreateSlices(
            new Size(100, 80), new Thickness(double.PositiveInfinity, 8, 12, 6), new Size(180, 130)));
    }

    [Fact]
    public void CompositeThemeDrawsOneFullPngAndArrangesTextInTheSameControl()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryPaths.Root, "src", "IkuyoPet.Pet", "BubbleChrome.cs"));

        Assert.Contains("class BubbleChrome : Decorator", source);
        Assert.Contains("Child?.Arrange(TextBounds)", source);
        Assert.Contains("drawingContext.DrawImage(Source, new Rect(RenderSize))", source);
        Assert.Contains("if (Source is null)", source);
        Assert.DoesNotContain("DrawRoundedRectangle", source);
        Assert.DoesNotContain("public Rect InteriorBounds", source);
        Assert.DoesNotContain("public Rect DecorationBounds", source);
        Assert.DoesNotContain("drawingContext.DrawImage(crop", source);
    }

    [Fact]
    public void PetProjectCopiesBubblePngsUnderAssetsFolder()
    {
        var project = File.ReadAllText(Path.Combine(
            RepositoryPaths.Root, "src", "IkuyoPet.Pet", "IkuyoPet.Pet.csproj"));

        Assert.Contains(@"assets\bubbles\**\bubble-filled.png", project);
        Assert.Contains(@"assets\bubbles\%(RecursiveDir)%(Filename)%(Extension)", project);
        Assert.Contains("CopyToOutputDirectory=\"PreserveNewest\"", project);
        Assert.Contains("CopyToPublishDirectory=\"PreserveNewest\"", project);
    }

    [Fact]
    public void PetWindowAppliesInteriorDecorationAndTextBoundsFromOneLayout()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryPaths.Root, "src", "IkuyoPet.Pet", "PetWindow.xaml.cs"));

        Assert.Contains("BubbleRoot.TextBounds =", source);
        Assert.DoesNotContain("BubbleRoot.InteriorBounds =", source);
        Assert.DoesNotContain("BubbleRoot.DecorationBounds =", source);
    }
}
