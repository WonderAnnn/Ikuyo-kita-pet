using System.Windows;
using IkuyoPet.Pet;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.Pet;

public sealed class PetBubbleLayoutTests
{
    [Fact]
    public void PlacesBubbleLeftOfPetWithLowerRightTailAlignedToPetUpperBody()
    {
        var layout = PetBubbleLayout.Calculate(
            new Size(300, 120),
            new Size(180, 300));

        Assert.Equal(12, layout.BubbleLeft);
        Assert.Equal(4, layout.BubbleTop);
        Assert.Equal(324, layout.PetLeft);
        Assert.Equal(49, layout.PetTop);
        Assert.True(layout.BubbleRight <= layout.PetLeft - 12);
        Assert.True(layout.PetTop < layout.BubbleBottom);
        Assert.True(layout.PetBottom > layout.BubbleBottom);
        Assert.True(layout.WindowWidth >= layout.PetRight + 12);
        Assert.True(layout.WindowHeight >= layout.PetBottom + 12);
    }

    [Fact]
    public void CalculatesWindowOriginThatPreservesPetScreenAnchor()
    {
        var layout = PetBubbleLayout.Calculate(new Size(356, 200), new Size(180, 300));
        var origin = PetBubbleLayout.CalculateWindowOriginForPetAnchor(new Point(900, 700), layout);

        Assert.Equal(900, origin.X + layout.PetLeft);
        Assert.Equal(700, origin.Y + layout.PetTop);
    }
}

