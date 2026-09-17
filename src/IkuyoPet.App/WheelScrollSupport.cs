using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace IkuyoPet.App;

/// <summary>
/// Makes every ScrollViewer scroll a comfortable fixed stride per wheel notch.
/// The default stride follows the system "lines per wheel notch" setting, which
/// can be as low as a few pixels and felt like the pages would not scroll.
/// </summary>
public static class WheelScrollSupport
{
    private const double PixelsPerNotch = 72;

    public static void Register()
    {
        EventManager.RegisterClassHandler(
            typeof(ScrollViewer),
            ScrollViewer.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(ForwardWheel),
            handledEventsToo: false);
    }

    private static void ForwardWheel(object sender, MouseWheelEventArgs eventArgs)
    {
        if (eventArgs.Handled) return;
        if (sender is not ScrollViewer scrollViewer) return;
        // Multi-line text boxes keep their native wheel behaviour.
        if (scrollViewer.TemplatedParent is TextBox) return;

        var notches = eventArgs.Delta / 120d;
        if (scrollViewer.ScrollableHeight > 0)
        {
            eventArgs.Handled = true;
            var target = scrollViewer.VerticalOffset - notches * PixelsPerNotch;
            scrollViewer.ScrollToVerticalOffset(
                Math.Max(0, Math.Min(scrollViewer.ScrollableHeight, target)));
        }
        else if (scrollViewer.ScrollableWidth > 0)
        {
            eventArgs.Handled = true;
            var target = scrollViewer.HorizontalOffset - notches * PixelsPerNotch;
            scrollViewer.ScrollToHorizontalOffset(
                Math.Max(0, Math.Min(scrollViewer.ScrollableWidth, target)));
        }
    }
}
