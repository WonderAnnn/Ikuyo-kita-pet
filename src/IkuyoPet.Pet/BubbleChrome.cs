using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IkuyoPet.Pet;

public sealed record BubbleSlice(Rect Source, Rect Destination);

public sealed class BubbleChrome : FrameworkElement
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(BitmapSource), typeof(BubbleChrome),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private Thickness sliceInsets;
    private double sliceScale = 1;

    public BitmapSource? Source
    {
        get => (BitmapSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public Thickness SliceInsets
    {
        get => sliceInsets;
        set
        {
            if (value.Left < 0 || value.Top < 0 || value.Right < 0 || value.Bottom < 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            sliceInsets = value;
            InvalidateVisual();
        }
    }

    public double SliceScale
    {
        get => sliceScale;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            sliceScale = value;
            InvalidateVisual();
        }
    }

    public static IReadOnlyList<BubbleSlice> CreateSlices(
        Size sourceSize, Thickness sliceInsets, Size destinationSize) =>
        CreateSlices(sourceSize, sliceInsets, sliceInsets, destinationSize);

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(Brushes.White, null, new Rect(RenderSize));
        if (Source is null)
        {
            drawingContext.DrawRoundedRectangle(
                Brushes.White,
                new Pen(new SolidColorBrush(Color.FromRgb(231, 221, 246)), 1),
                new Rect(RenderSize), 18, 18);
            return;
        }

        var sourceInsets = ClampInsets(SliceInsets, new Size(Source.PixelWidth, Source.PixelHeight));
        var destinationInsets = new Thickness(
            sourceInsets.Left * SliceScale, sourceInsets.Top * SliceScale,
            sourceInsets.Right * SliceScale, sourceInsets.Bottom * SliceScale);
        var slices = CreateSlices(
            new Size(Source.PixelWidth, Source.PixelHeight), sourceInsets,
            destinationInsets, RenderSize);

        foreach (var slice in slices)
        {
            if (slice.Source.Width <= 0 || slice.Source.Height <= 0 ||
                slice.Destination.Width <= 0 || slice.Destination.Height <= 0)
                continue;
            var crop = new CroppedBitmap(Source, new Int32Rect(
                (int)Math.Round(slice.Source.X), (int)Math.Round(slice.Source.Y),
                (int)Math.Round(slice.Source.Width), (int)Math.Round(slice.Source.Height)));
            crop.Freeze();
            drawingContext.DrawImage(crop, slice.Destination);
        }
    }

    private static IReadOnlyList<BubbleSlice> CreateSlices(
        Size sourceSize, Thickness sourceInsets, Thickness destinationInsets, Size destinationSize)
    {
        ValidateSize(sourceSize, nameof(sourceSize));
        ValidateSize(destinationSize, nameof(destinationSize));
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

    private static void ValidateSize(Size size, string parameterName)
    {
        if (size.Width <= 0 || size.Height <= 0 || double.IsInfinity(size.Width) || double.IsInfinity(size.Height))
            throw new ArgumentOutOfRangeException(parameterName);
    }
}
