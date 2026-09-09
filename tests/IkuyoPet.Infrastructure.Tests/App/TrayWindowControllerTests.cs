using System.Windows;
using IkuyoPet.App;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.App;

public sealed class TrayWindowControllerTests
{
    [Fact]
    public void HideToTrayHidesWindowAndRemovesTaskbarEntry()
    {
        var surface = new FakeSurface
        {
            IsVisible = true,
            ShowInTaskbar = true,
            WindowState = WindowState.Minimized,
        };
        var controller = new TrayWindowController(surface);

        controller.HideToTray();

        Assert.False(surface.ShowInTaskbar);
        Assert.False(surface.IsVisible);
        Assert.Equal(WindowState.Normal, surface.WindowState);
    }

    [Fact]
    public void RestoreShowsWindowInTaskbarAndActivatesIt()
    {
        var surface = new FakeSurface();
        var controller = new TrayWindowController(surface);

        controller.Restore();

        Assert.True(surface.ShowInTaskbar);
        Assert.True(surface.IsVisible);
        Assert.Equal(WindowState.Normal, surface.WindowState);
        Assert.True(surface.ActivateCalled);
    }

    private sealed class FakeSurface : ITrayWindowSurface
    {
        public bool IsVisible { get; set; }
        public bool ShowInTaskbar { get; set; }
        public WindowState WindowState { get; set; }
        public bool ActivateCalled { get; private set; }

        public void Show() => IsVisible = true;

        public void Hide() => IsVisible = false;

        public bool Activate()
        {
            ActivateCalled = true;
            return true;
        }
    }
}

