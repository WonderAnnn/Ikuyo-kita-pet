using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using IkuyoPet.Core.Bubbles;

namespace IkuyoPet.Pet;

public sealed record BubbleRenderModel(
    BubbleThemeDefinition Theme,
    BitmapSource? Source,
    BubbleLayoutResult Layout,
    Size LogicalSize,
    Rect TextBounds,
    double DpiScale,
    Size PixelSize,
    Rect PixelTextBounds);

public static class BubbleRenderer
{
    public static BubbleAssetCache Assets { get; } = new(LoadBitmapUncached);

    public static BubbleRenderModel Create(
        BubbleThemeDefinition theme,
        BitmapSource? source,
        string? text,
        IReadOnlyList<string>? actionLabels = null,
        double dpiScale = 1,
        double maximumWindowHeight = 460)
    {
        ArgumentNullException.ThrowIfNull(theme);
        if (!double.IsFinite(dpiScale) || dpiScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(dpiScale));
        var layout = BubbleLayoutCalculator.Calculate(theme, text, actionLabels, maximumWindowHeight);
        var logical = new Size(layout.WindowWidth, layout.WindowHeight);
        var textBounds = new Rect(
            layout.TextArea.Left, layout.TextArea.Top,
            layout.TextArea.Width, layout.TextArea.Height);
        return new BubbleRenderModel(
            theme, source, layout, logical, textBounds, dpiScale,
            new Size(logical.Width * dpiScale, logical.Height * dpiScale),
            new Rect(
                textBounds.Left * dpiScale, textBounds.Top * dpiScale,
                textBounds.Width * dpiScale, textBounds.Height * dpiScale));
    }

    public static BitmapSource? LoadBitmap(string resourcePath) => Assets.Get(resourcePath);

    internal static BitmapImage? LoadBitmapUncached(string resourcePath)
    {
        try
        {
            if (!File.Exists(resourcePath)) return null;
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(Path.GetFullPath(resourcePath), UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }
}
