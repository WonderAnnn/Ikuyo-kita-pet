using Xunit;

namespace IkuyoPet.Infrastructure.Tests.Pet;

public sealed class PetWindowLayoutContractTests
{
    [Fact]
    public void UpdateWindowLayoutUsesLeftUpperBubbleLayout()
    {
        var repository = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var source = File.ReadAllText(
            Path.Combine(repository, "src", "IkuyoPet.Pet", "PetWindow.xaml.cs"));

        Assert.Contains("PetBubbleLayout.Calculate", source, StringComparison.Ordinal);
        Assert.Contains("Canvas.SetLeft(BubbleRoot, layout.BubbleLeft)", source, StringComparison.Ordinal);
        Assert.Contains("Canvas.SetTop(BubbleRoot, layout.BubbleTop)", source, StringComparison.Ordinal);
        Assert.Contains("Canvas.SetLeft(PetHitArea, layout.PetLeft)", source, StringComparison.Ordinal);
        Assert.Contains("Canvas.SetTop(PetHitArea, layout.PetTop)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("(Width - BubbleRoot.Width) / 2", source, StringComparison.Ordinal);
        Assert.DoesNotContain("(Width - PetHitArea.Width) / 2", source, StringComparison.Ordinal);
        Assert.Contains("CapturePetScreenAnchor()", source, StringComparison.Ordinal);
        Assert.Contains("UpdateWindowLayout(petScreenAnchor)", source, StringComparison.Ordinal);
        Assert.Contains("RestoreCompactWindowLayout(petScreenAnchor)", source, StringComparison.Ordinal);
        Assert.Contains("CalculateWindowOriginForPetAnchor", source, StringComparison.Ordinal);
    }
}

