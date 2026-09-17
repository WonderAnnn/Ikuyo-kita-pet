using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IkuyoPet.Pet;

public sealed record BubbleSlice(Rect Source, Rect Destination);

public sealed class BubbleChrome : Decorator
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(BitmapSource), typeof(BubbleChrome),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private Thickness sliceInsets;
    private Thickness logicalSliceInsets;
    private Size decorationViewportSize = new(276, 116);
    private Thickness interiorInsets = new(80, 32, 40, 28);
    private Rect textBounds = new(18, 14, 320, 88);

    public BubbleChrome()
    {
        ClipToBounds = true;
    }

    public BitmapSource? Source
    {
        get => (BitmapSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public void ApplyRender(BubbleRenderModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        Source = model.Source;
        Width = model.LogicalSize.Width;
        Height = model.LogicalSize.Height;
        TextBounds = model.TextBounds;
    }

    public Thickness SliceInsets
    {
        get => sliceInsets;
        set
        {
            ValidateInsets(value, nameof(value));
            sliceInsets = value;
            InvalidateVisual();
        }
    }

    public Thickness LogicalSliceInsets
    {
        get => logicalSliceInsets;
        set
        {
            ValidateInsets(value, nameof(value));
            logicalSliceInsets = value;
            InvalidateVisual();
        }
    }

    public Size DecorationViewportSize
    {
        get => decorationViewportSize;
        set
        {
            ValidateSize(value, nameof(value));
            decorationViewportSize = value;
            InvalidateVisual();
        }
    }

    public Thickness InteriorInsets
    {
        get => interiorInsets;
        set
        {
            ValidateInsets(value, nameof(value));
            interiorInsets = value;
            InvalidateVisual();
        }
    }

    public Rect TextBounds
    {
        get => textBounds;
        set
        {
            if (!double.IsFinite(value.X) || !double.IsFinite(value.Y) ||
                !double.IsFinite(value.Width) || !double.IsFinite(value.Height) ||
                value.X < 0 || value.Y < 0 || value.Width <= 0 || value.Height <= 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            textBounds = value;
            InvalidateMeasure();
            InvalidateArrange();
        }
    }

    public static IReadOnlyList<BubbleSlice> CreateSlices(
        Size sourceSize, Thickness sliceInsets, Size destinationSize) =>
        CreateSlices(sourceSize, sliceInsets, sliceInsets, destinationSize);

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (Source is null) return;
        drawingContext.DrawImage(Source, new Rect(RenderSize));
    }

    protected override Size MeasureOverride(Size constraint)
    {
        Child?.Measure(TextBounds.Size);
        return new Size(
            Math.Max(TextBounds.Right, Child?.DesiredSize.Width ?? 0),
            Math.Max(TextBounds.Bottom, Child?.DesiredSize.Height ?? 0));
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        Child?.Arrange(TextBounds);
        return arrangeSize;
    }

    public static Rect CalculateDecorationBounds(
        Size sourceSize,
        Size destinationSize,
        Size decorationViewportSize)
    {
        ValidateSize(sourceSize, nameof(sourceSize));
        ValidateSize(destinationSize, nameof(destinationSize));
        ValidateSize(decorationViewportSize, nameof(decorationViewportSize));
        var scale = Math.Min(
            decorationViewportSize.Width / sourceSize.Width,
            decorationViewportSize.Height / sourceSize.Height);
        var width = sourceSize.Width * scale;
        var height = sourceSize.Height * scale;
        return new Rect(0, (destinationSize.Height - height) / 2, width, height);
    }

    public static Size CalculateDecorationViewport(
        Size sourceSize,
        Size baselineBubbleSize,
        double textOrigin,
        double interiorOverlap)
    {
        ValidateSize(sourceSize, nameof(sourceSize));
        ValidateSize(baselineBubbleSize, nameof(baselineBubbleSize));
        if (!double.IsFinite(textOrigin) || textOrigin < 0)
            throw new ArgumentOutOfRangeException(nameof(textOrigin));
        if (!double.IsFinite(interiorOverlap) || interiorOverlap < 0)
            throw new ArgumentOutOfRangeException(nameof(interiorOverlap));

        var baselineScale = Math.Min(
            baselineBubbleSize.Width / sourceSize.Width,
            baselineBubbleSize.Height / sourceSize.Height);
        var overlapScale = (textOrigin + interiorOverlap) / sourceSize.Width;
        var scale = Math.Max(baselineScale, overlapScale);
        return new Size(sourceSize.Width * scale, sourceSize.Height * scale);
    }

    public static Rect CalculateInteriorBounds(Size destinationSize, Thickness insets)
    {
        ValidateSize(destinationSize, nameof(destinationSize));
        ValidateInsets(insets, nameof(insets));
        var clamped = ClampInsets(insets, destinationSize);
        return new Rect(
            clamped.Left,
            clamped.Top,
            Math.Max(0, destinationSize.Width - clamped.Left - clamped.Right),
            Math.Max(0, destinationSize.Height - clamped.Top - clamped.Bottom));
    }

    public static IReadOnlyList<BubbleSlice> CreateSlices(
        Size sourceSize, Thickness sourceInsets, Thickness destinationInsets, Size destinationSize)
    {
        ValidateSize(sourceSize, nameof(sourceSize));
        ValidateSize(destinationSize, nameof(destinationSize));
        ValidateInsets(sourceInsets, nameof(sourceInsets));
        ValidateInsets(destinationInsets, nameof(destinationInsets));
        var source = ClampInsets(sourceInsets, sourceSize);
        var destination = ClampInsets(destinationInsets, destinationSize);
        var sourceX = new[] { 0d, source.Left, sourceSize.Width - source.Right, sourceSize.Width };
        var sourceY = new[] { 0d, source.Top, sourceSize.Height - source.Bottom, sourceSize.Height };
        var destinationX = new[] { 0d, destination.Left, destinationSize.Width - destination.Right, destinationSize.Width };
        var destinationY = new[] { 0d, destination.Top, destinationSize.Height - destination.Bottom, destinationSize.Height };
        var slices = new List<BubbleSlice>(9);
        for (var row = 0; row < 3; row++)
        for (var column = 0; column < 3; column++)
            slices.Add(new BubbleSlice(
                CreateRect(sourceX, sourceY, column, row),
                CreateRect(destinationX, destinationY, column, row)));
        return slices;
    }

    private static Rect CreateRect(double[] x, double[] y, int column, int row) =>
        new(x[column], y[row], x[column + 1] - x[column], y[row + 1] - y[row]);

    private static Thickness ClampInsets(Thickness insets, Size size)
    {
        var horizontalScale = insets.Left + insets.Right > size.Width
            ? size.Width / (insets.Left + insets.Right) : 1;
        var verticalScale = insets.Top + insets.Bottom > size.Height
            ? size.Height / (insets.Top + insets.Bottom) : 1;
        return new Thickness(
            insets.Left * horizontalScale, insets.Top * verticalScale,
            insets.Right * horizontalScale, insets.Bottom * verticalScale);
    }

    private static void ValidateInsets(Thickness insets, string parameterName)
    {
        if (!double.IsFinite(insets.Left) || !double.IsFinite(insets.Top) ||
            !double.IsFinite(insets.Right) || !double.IsFinite(insets.Bottom) ||
            insets.Left < 0 || insets.Top < 0 || insets.Right < 0 || insets.Bottom < 0)
            throw new ArgumentOutOfRangeException(parameterName);
    }
    private static void ValidateSize(Size size, string parameterName)
    {
        if (!double.IsFinite(size.Width) || !double.IsFinite(size.Height) || size.Width <= 0 || size.Height <= 0)
            throw new ArgumentOutOfRangeException(parameterName);
    }

}
