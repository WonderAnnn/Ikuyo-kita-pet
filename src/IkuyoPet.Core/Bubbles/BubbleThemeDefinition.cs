namespace IkuyoPet.Core.Bubbles;

public sealed record BubbleSliceInsets(int Left, int Top, int Right, int Bottom)
{
    public BubbleSliceInsets
    {
        if (Left < 0 || Top < 0 || Right < 0 || Bottom < 0)
            throw new ArgumentOutOfRangeException(nameof(Left), "Slice insets cannot be negative.");
    }
}

public sealed record BubbleSafeArea(double Left, double Top, double Width, double Height)
{
    public BubbleSafeArea
    {
        if (Left is < 0 or > 1 || Top is < 0 or > 1 || Width <= 0 || Height <= 0 || Left + Width > 1 || Top + Height > 1)
            throw new ArgumentOutOfRangeException(nameof(Left), "Safe area must be a normalized rectangle inside the image.");
    }
}

public sealed record BubbleThemeDefinition(
    string Id,
    string Name,
    string Version,
    string ResourcePath,
    string PreviewPath,
    int PixelWidth,
    int PixelHeight,
    BubbleSliceInsets SliceInsets,
    BubbleSafeArea TextSafeArea,
    double MinContentWidth,
    double MaxContentWidth,
    double MinContentHeight,
    double MaxContentHeight)
{
    public BubbleThemeDefinition
    {
        if (string.IsNullOrWhiteSpace(Id)) throw new ArgumentException("Theme id is required.", nameof(Id));
        if (string.IsNullOrWhiteSpace(Name)) throw new ArgumentException("Theme name is required.", nameof(Name));
        if (string.IsNullOrWhiteSpace(Version)) throw new ArgumentException("Theme version is required.", nameof(Version));
        if (string.IsNullOrWhiteSpace(ResourcePath)) throw new ArgumentException("Theme resource path is required.", nameof(ResourcePath));
        if (string.IsNullOrWhiteSpace(PreviewPath)) throw new ArgumentException("Theme preview path is required.", nameof(PreviewPath));
        if (PixelWidth <= 0 || PixelHeight <= 0) throw new ArgumentOutOfRangeException(nameof(PixelWidth));
        if (MinContentWidth <= 0 || MaxContentWidth < MinContentWidth) throw new ArgumentOutOfRangeException(nameof(MinContentWidth));
        if (MinContentHeight <= 0 || MaxContentHeight < MinContentHeight) throw new ArgumentOutOfRangeException(nameof(MinContentHeight));
        ArgumentNullException.ThrowIfNull(SliceInsets);
        ArgumentNullException.ThrowIfNull(TextSafeArea);
    }
}

public sealed record BubbleTextArea(double Left, double Top, double Width, double Height);

public sealed record BubbleLayoutResult(
    double ContentWidth,
    double ContentHeight,
    double WindowWidth,
    double WindowHeight,
    BubbleTextArea TextArea,
    int LineCount);
