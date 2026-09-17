using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IkuyoPet.Core.Bubbles;

namespace IkuyoPet.Pet;

public sealed class BubbleThemePreview : Viewbox
{
    private readonly BubbleChrome chrome = new();
    private readonly ScrollViewer scroll = new()
    {
        Background = Brushes.Transparent,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        CanContentScroll = false,
        Focusable = false,
    };
    private readonly TextBlock text = new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x2B, 0x3D)),
        FontSize = 16,
        LineHeight = 24,
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Left,
    };

    public BubbleThemePreview()
    {
        Stretch = Stretch.Uniform;
        scroll.Content = text;
        chrome.Child = scroll;
        Child = chrome;
        Loaded += (_, _) => Refresh();
    }

    public static readonly DependencyProperty ThemeProperty = DependencyProperty.Register(
        nameof(Theme), typeof(BubbleThemeDefinition), typeof(BubbleThemePreview),
        new PropertyMetadata(null, Refresh));
    public static readonly DependencyProperty ResourcePathProperty = DependencyProperty.Register(
        nameof(ResourcePath), typeof(string), typeof(BubbleThemePreview),
        new PropertyMetadata(null, Refresh));
    public static readonly DependencyProperty PreviewTextProperty = DependencyProperty.Register(
        nameof(PreviewText), typeof(string), typeof(BubbleThemePreview),
        new PropertyMetadata("该休息一下啦", Refresh));

    public BubbleThemeDefinition? Theme { get => (BubbleThemeDefinition?)GetValue(ThemeProperty); set => SetValue(ThemeProperty, value); }
    public string? ResourcePath { get => (string?)GetValue(ResourcePathProperty); set => SetValue(ResourcePathProperty, value); }
    public string PreviewText { get => (string)GetValue(PreviewTextProperty); set => SetValue(PreviewTextProperty, value); }

    private static void Refresh(DependencyObject target, DependencyPropertyChangedEventArgs _) =>
        ((BubbleThemePreview)target).Refresh();

    private void Refresh()
    {
        if (Theme is null || string.IsNullOrWhiteSpace(ResourcePath)) return;
        var model = BubbleRenderer.Create(
            Theme, BubbleRenderer.LoadBitmap(ResourcePath), PreviewText,
            dpiScale: VisualTreeHelper.GetDpi(this).DpiScaleX);
        text.Text = PreviewText;
        chrome.ApplyRender(model);
    }
}
