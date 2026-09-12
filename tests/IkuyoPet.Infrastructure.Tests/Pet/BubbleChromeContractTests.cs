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
    }
}
