using System.Windows;

namespace IkuyoPet.Pet;

public readonly record struct PetBubbleLayoutResult(
    double BubbleLeft,
    double BubbleTop,
    double PetLeft,
    double PetTop,
    double WindowWidth,
    double WindowHeight,
    double BubbleWidth,
    double BubbleHeight,
    double PetWidth,
    double PetHeight)
{
    public double BubbleRight => BubbleLeft + BubbleWidth;

    public double BubbleBottom => BubbleTop + BubbleHeight;

    public double PetRight => PetLeft + PetWidth;

    public double PetBottom => PetTop + PetHeight;
}

public static class PetBubbleLayout
{
    public const double DefaultOuterMargin = 12;
    public const double DefaultGap = 12;
    public const double DefaultBottomMargin = 16;

    public static PetBubbleLayoutResult Calculate(
        Size bubbleSize,
        Size petSize,
        double outerMargin = DefaultOuterMargin,
        double gap = DefaultGap,
        double minimumWindowWidth = 380,
        double bottomMargin = DefaultBottomMargin)
    {
        ValidateSize(bubbleSize, nameof(bubbleSize));
        ValidateSize(petSize, nameof(petSize));
        ValidateNonNegative(outerMargin, nameof(outerMargin));
        ValidateNonNegative(gap, nameof(gap));
        ValidateNonNegative(minimumWindowWidth, nameof(minimumWindowWidth));
        ValidateNonNegative(bottomMargin, nameof(bottomMargin));

        var bubbleLeft = outerMargin;
        // Lift the bubble slightly toward the pet upper body.
        var bubbleTop = Math.Max(0, outerMargin - 8);
        var petLeft = bubbleLeft + bubbleSize.Width + gap;
        // Keep the pet beside the bubble so the asset tail at the lower-right
        // can visually point toward the upper portion of the character.
        var petTop = Math.Max(
            outerMargin,
            bubbleTop + bubbleSize.Height - petSize.Height * 0.25);
        var right = petLeft + petSize.Width;
        var bottom = petTop + petSize.Height;

        return new PetBubbleLayoutResult(
            bubbleLeft,
            bubbleTop,
            petLeft,
            petTop,
            Math.Max(minimumWindowWidth, right + outerMargin),
            bottom + bottomMargin,
            bubbleSize.Width,
            bubbleSize.Height,
            petSize.Width,
            petSize.Height);
    }

    public static Point CalculateWindowOriginForPetAnchor(Point petScreenAnchor, PetBubbleLayoutResult layout)
    {
        if (!double.IsFinite(petScreenAnchor.X) || !double.IsFinite(petScreenAnchor.Y))
            throw new ArgumentOutOfRangeException(nameof(petScreenAnchor));

        return new Point(
            petScreenAnchor.X - layout.PetLeft,
            petScreenAnchor.Y - layout.PetTop);
    }

    private static void ValidateSize(Size size, string parameterName)
    {
        if (!double.IsFinite(size.Width) || !double.IsFinite(size.Height) ||
            size.Width < 0 || size.Height < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void ValidateNonNegative(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}


