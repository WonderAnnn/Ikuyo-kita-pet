using System.Windows;

namespace IkuyoPet.App;

public interface ITrayWindowSurface
{
    bool IsVisible { get; }
    bool ShowInTaskbar { get; set; }
    WindowState WindowState { get; set; }

    void Show();
    void Hide();
    bool Activate();
}

public sealed class TrayWindowController(ITrayWindowSurface surface)
{
    private readonly ITrayWindowSurface surface = surface ?? throw new ArgumentNullException(nameof(surface));

    public void HideToTray()
    {
        surface.ShowInTaskbar = false;
        surface.Hide();
        surface.WindowState = WindowState.Normal;
    }

    public void Restore()
    {
        surface.ShowInTaskbar = true;
        surface.WindowState = WindowState.Normal;
        surface.Show();
        surface.Activate();
    }
}

public sealed class WpfTrayWindowSurface(Window window) : ITrayWindowSurface
{
    private readonly Window window = window ?? throw new ArgumentNullException(nameof(window));

    public bool IsVisible => window.IsVisible;

    public bool ShowInTaskbar
    {
        get => window.ShowInTaskbar;
        set => window.ShowInTaskbar = value;
    }

    public WindowState WindowState
    {
        get => window.WindowState;
        set => window.WindowState = value;
    }

    public void Show() => window.Show();

    public void Hide() => window.Hide();

    public bool Activate() => window.Activate();
}

