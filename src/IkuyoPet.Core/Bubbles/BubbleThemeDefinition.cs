using System;

namespace IkuyoPet.Core.Bubbles;

public sealed record BubbleSliceInsets
{
    public BubbleSliceInsets(int left, int top, int right, int bottom)
    {
        if (left < 0 || top < 0 || right < 0 || bottom < 0)
            throw new ArgumentOutOfRangeException(nameof(left), "Slice insets cannot be negative.");
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public int Left { get; init; }
    public int Top { get; init; }
    public int Right { get; init; }
    public int Bottom { get; init; }
}

public sealed record BubbleSafeArea
{
    public BubbleSafeArea(double left, double top, double width, double height)
    {
        if (!double.IsFinite(left) || !double.IsFinite(top) || !double.IsFinite(width) || !double.IsFinite(height) ||
            left is < 0 or > 1 || top is < 0 or > 1 || width <= 0 || height <= 0 || left + width > 1 || top + height > 1)
            throw new ArgumentOutOfRangeException(nameof(left), "Safe area must be a normalized rectangle inside the image.");
        Left = left;
        Top = top;
        Width = width;
        Height = height;
    }

    public double Left { get; init; }
    public double Top { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
}

public sealed record BubbleThemeDefinition
{
    private double minContentWidth;
    private double maxContentWidth;
    private double minContentHeight;
    private double maxContentHeight;

    public BubbleThemeDefinition(
        string id,
        string name,
        string version,
        string resourcePath,
        string previewPath,
        int pixelWidth,
        int pixelHeight,
        BubbleSliceInsets sliceInsets,
        BubbleSafeArea textSafeArea,
        double minContentWidth,
        double maxContentWidth,
        double minContentHeight,
        double maxContentHeight)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Theme id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Theme name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("Theme version is required.", nameof(version));
        if (string.IsNullOrWhiteSpace(resourcePath)) throw new ArgumentException("Theme resource path is required.", nameof(resourcePath));
        if (string.IsNullOrWhiteSpace(previewPath)) throw new ArgumentException("Theme preview path is required.", nameof(previewPath));
        if (pixelWidth <= 0 || pixelHeight <= 0) throw new ArgumentOutOfRangeException(nameof(pixelWidth));
        if (!double.IsFinite(minContentWidth) || !double.IsFinite(maxContentWidth) || minContentWidth <= 0 || maxContentWidth < minContentWidth)
            throw new ArgumentOutOfRangeException(nameof(minContentWidth));
        if (!double.IsFinite(minContentHeight) || !double.IsFinite(maxContentHeight) || minContentHeight <= 0 || maxContentHeight < minContentHeight)
            throw new ArgumentOutOfRangeException(nameof(minContentHeight));
        ArgumentNullException.ThrowIfNull(sliceInsets);
        ArgumentNullException.ThrowIfNull(textSafeArea);

        Id = id;
        Name = name;
        Version = version;
        ResourcePath = resourcePath;
        PreviewPath = previewPath;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        SliceInsets = sliceInsets;
        TextSafeArea = textSafeArea;
        MinContentWidth = minContentWidth;
        MaxContentWidth = maxContentWidth;
        MinContentHeight = minContentHeight;
        MaxContentHeight = maxContentHeight;
    }

    public string Id { get; init; }
    public string Name { get; init; }
    public string Version { get; init; }
    public string ResourcePath { get; init; }
    public string PreviewPath { get; init; }
    public int PixelWidth { get; init; }
    public int PixelHeight { get; init; }
    public BubbleSliceInsets SliceInsets { get; init; }
    public BubbleSafeArea TextSafeArea { get; init; }
    public double MinContentWidth
    {
        get => minContentWidth;
        init
        {
            if (!double.IsFinite(value) || value <= 0 || maxContentWidth > 0 && value > maxContentWidth)
                throw new ArgumentOutOfRangeException(nameof(value));
            minContentWidth = value;
        }
    }

    public double MaxContentWidth
    {
        get => maxContentWidth;
        init
        {
            if (!double.IsFinite(value) || value <= 0 || minContentWidth > 0 && value < minContentWidth)
                throw new ArgumentOutOfRangeException(nameof(value));
            maxContentWidth = value;
        }
    }

    public double MinContentHeight
    {
        get => minContentHeight;
        init
        {
            if (!double.IsFinite(value) || value <= 0 || maxContentHeight > 0 && value > maxContentHeight)
                throw new ArgumentOutOfRangeException(nameof(value));
            minContentHeight = value;
        }
    }

    public double MaxContentHeight
    {
        get => maxContentHeight;
        init
        {
            if (!double.IsFinite(value) || value <= 0 || minContentHeight > 0 && value < minContentHeight)
                throw new ArgumentOutOfRangeException(nameof(value));
            maxContentHeight = value;
        }
    }
}

public sealed record BubbleTextArea(double Left, double Top, double Width, double Height);

public sealed record BubbleLayoutResult(
    double ContentWidth,
    double ContentHeight,
    double WindowWidth,
    double WindowHeight,
    BubbleTextArea TextArea,
    int LineCount)
{
    public bool RequiresVerticalScroll { get; init; }
    public double TextViewportHeight { get; init; }
}
