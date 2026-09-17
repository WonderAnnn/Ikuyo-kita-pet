using IkuyoPet.Infrastructure.Windows;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.Windows;

public sealed class ProcessInspectorTests
{
    [Fact]
    public void NormalizesProcessOptionsByNameWithoutWindowTitles()
    {
        var normalized = WindowsRunningProcessInspector.Normalize([
            new RunningProcessInfo("pycharm64", 101),
            new RunningProcessInfo("pycharm64", 102),
            new RunningProcessInfo("WINWORD", 201),
        ]);

        Assert.Equal(2, normalized.Count);
        Assert.Contains(normalized, item => item.ProcessName == "pycharm64");
        Assert.DoesNotContain(normalized, item => item.DisplayName.Contains(" - ", StringComparison.Ordinal));
    }

    [Fact]
    public void ReusesProcessListWithinCacheWindowAndForceRefreshBypassesIt()
    {
        var calls = 0;
        var inspector = new WindowsRunningProcessInspector(
            sessionProbe: null,
            processReader: () =>
            {
                calls++;
                return [new RunningProcessInfo("chrome", calls)];
            },
            timeProvider: TimeProvider.System,
            cacheWindow: TimeSpan.FromSeconds(10));

        _ = inspector.GetRunningProcesses();
        _ = inspector.GetRunningProcesses();
        Assert.Equal(1, calls);

        _ = inspector.GetRunningProcesses(forceRefresh: true);
        Assert.Equal(2, calls);
    }
}