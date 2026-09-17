using IkuyoPet.Infrastructure.Windows;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.Windows;

public sealed class WindowsSessionProbeTests
{
    [Fact]
    public void CapturesLockFullscreenAndPresentationAsIndependentSignals()
    {
        var foregroundWindow = new IntPtr(42);
        var probe = new WindowsSessionProbe(
            isLocked: () => true,
            isFullScreen: window => window == foregroundWindow,
            isPresentationMode: () => true);

        var state = probe.Capture(foregroundWindow);

        Assert.True(state.IsLocked);
        Assert.True(state.IsFullScreen);
        Assert.True(state.IsPresentationMode);
        Assert.True(state.SuppressActiveWork);
    }

    [Fact]
    public void OrdinaryDesktopDoesNotSuppressActiveWork()
    {
        var probe = new WindowsSessionProbe(
            isLocked: () => false,
            isFullScreen: _ => false,
            isPresentationMode: () => false);

        var state = probe.Capture(new IntPtr(42));

        Assert.False(state.SuppressActiveWork);
    }

    [Theory]
    [InlineData("Default", false)]
    [InlineData("default", false)]
    [InlineData("Winlogon", true)]
    [InlineData("Disconnect", true)]
    [InlineData(null, true)]
    public void OnlyDefaultInputDesktopIsTreatedAsUnlocked(string? desktopName, bool expectedLocked)
    {
        Assert.Equal(expectedLocked, WindowsSessionProbe.IsLockedDesktopName(desktopName));
    }
}
