using Xunit;
using System.Windows;
using IkuyoPet.Pet.Interaction;

namespace IkuyoPet.Infrastructure.Tests.Pet;

public sealed class PetGestureTrackerTests
{
    [Fact]
    public void ReleaseWithinThresholdIsClick()
    {
        var tracker = new PetGestureTracker(5, 7);
        tracker.Press(new Point(10, 10));
        tracker.Move(new Point(14, 16));

        Assert.Equal(PetGestureResult.Click, tracker.Release(new Point(14, 16)));
    }

    [Fact]
    public void EitherAxisAtThresholdStartsDragAndReleaseDoesNotClick()
    {
        var tracker = new PetGestureTracker(5, 7);
        tracker.Press(new Point(10, 10));

        Assert.Equal(PetGestureResult.DragStarted, tracker.Move(new Point(15, 10)));
        Assert.Equal(PetGestureResult.Dragging, tracker.Move(new Point(16, 10)));
        Assert.Equal(PetGestureResult.None, tracker.Release(new Point(16, 10)));
    }

    [Fact]
    public void CancelClearsPendingPress()
    {
        var tracker = new PetGestureTracker(5, 5);
        tracker.Press(new Point(1, 1));
        tracker.Cancel();

        Assert.Equal(PetGestureResult.None, tracker.Release(new Point(1, 1)));
    }
}
