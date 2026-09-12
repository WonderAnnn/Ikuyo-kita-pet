using System.IO;
using System.Windows;
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
    public void PetWindowUsesOneDecorativeChromeWithTextAboveIt()
    {
        var xaml = File.ReadAllText(Path.Combine(
            RepositoryPaths.Root, "src", "IkuyoPet.Pet", "PetWindow.xaml"));

        Assert.Contains("local:BubbleChrome", xaml);
        Assert.Contains("Panel.ZIndex=\"20\"", xaml);
        Assert.Contains("TextAlignment=\"Left\"", xaml);
        Assert.Contains("VerticalAlignment=\"Center\"", xaml);
        Assert.DoesNotContain("BubbleArrow", xaml);
        Assert.DoesNotContain("CornerRadius=", xaml);
    }

    [Fact]
    public void PetWindowExposesThemeSetterAndAppliesCoreLayoutToEveryBubbleState()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryPaths.Root, "src", "IkuyoPet.Pet", "PetWindow.xaml.cs"));

        Assert.Contains("public void SetBubbleTheme(BubbleThemeDefinition theme, string resourcePath)", source);
        Assert.Contains("BubbleLayoutCalculator.Calculate", source);
        Assert.Contains("ApplyBubbleLayout(text", source);
        Assert.Contains("ApplyBubbleLayout(view.Text", source);
        Assert.Contains("BubbleThemeCatalog.BuiltInThemes[0]", source);
        Assert.Contains("Path.Combine(AppContext.BaseDirectory, \"assets\", \"bubbles\")", source);
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
    public void NormalThemeFillsOnlyTheExpandableCenterSlice()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryPaths.Root, "src", "IkuyoPet.Pet", "BubbleChrome.cs"));

        Assert.DoesNotContain("DrawRectangle(Brushes.White, null, new Rect(RenderSize))", source);
        Assert.Contains("DrawRectangle(Brushes.White, null, slices[4].Destination)", source);
    }

    [Fact]
    public void PetProjectCopiesBubblePngsUnderAssetsFolder()
    {
        var project = File.ReadAllText(Path.Combine(
            RepositoryPaths.Root, "src", "IkuyoPet.Pet", "IkuyoPet.Pet.csproj"));

        Assert.Contains(@"assets\bubbles\**\bubble.png", project);
        Assert.Contains(@"assets\bubbles\%(RecursiveDir)%(Filename)%(Extension)", project);
        Assert.Contains("CopyToOutputDirectory=\"PreserveNewest\"", project);
        Assert.Contains("CopyToPublishDirectory=\"PreserveNewest\"", project);
    }
}
