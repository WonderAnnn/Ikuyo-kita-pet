using System.Windows;

namespace IkuyoPet.Pet.Interaction;

public enum PetGestureResult
{
    None,
    Click,
    DragStarted,
    Dragging,
}

public sealed class PetGestureTracker(double horizontalThreshold, double verticalThreshold)
{
    private readonly double horizontalThreshold = Math.Max(0, horizontalThreshold);
    private readonly double verticalThreshold = Math.Max(0, verticalThreshold);
    private Point pressPoint;
    private bool pressed;
    private bool dragging;

    public void Press(Point point)
    {
        pressPoint = point;
        pressed = true;
        dragging = false;
    }

    public PetGestureResult Move(Point point)
    {
        if (!pressed) return PetGestureResult.None;
        if (!dragging && (Math.Abs(point.X - pressPoint.X) >= horizontalThreshold || Math.Abs(point.Y - pressPoint.Y) >= verticalThreshold))
        {
            dragging = true;
            return PetGestureResult.DragStarted;
        }

        return dragging ? PetGestureResult.Dragging : PetGestureResult.None;
    }

    public PetGestureResult Release(Point point)
    {
        if (!pressed) return PetGestureResult.None;
        var result = dragging ? PetGestureResult.None : PetGestureResult.Click;
        pressed = false;
        dragging = false;
        return result;
    }

    public void Cancel()
    {
        pressed = false;
        dragging = false;
    }
}
