using IkuyoPet.Core.WorkTracking;
using Xunit;

namespace IkuyoPet.Core.Tests.WorkTracking;

public sealed class ProcessTrackingDiagnosisTests
{
    private static readonly TimeSpan IdleLimit = TimeSpan.FromMinutes(5);
    private static readonly string[] CommonProcesses = ["pycharm64", "WINWORD"];
    private static readonly string[] PyCharmOnly = ["pycharm64"];

    [Fact]
    public void AllowsAccumulationWhenProcessIsRunningForegroundUnlockedAndRecentlyUsed()
    {
        var result = ProcessTrackingDiagnosisEvaluator.Evaluate(
            "pycharm64",
            CommonProcesses,
            new ProcessObservation("pycharm64", false, TimeSpan.FromSeconds(12)),
            IdleLimit);

        Assert.True(result.IsRunning);
        Assert.True(result.IsForeground);
        Assert.True(result.IsUnlocked);
        Assert.True(result.HasRecentInput);
        Assert.True(result.CanAccumulate);
    }

    [Fact]
    public void ExplainsThatRunningInBackgroundDoesNotCount()
    {
        var result = ProcessTrackingDiagnosisEvaluator.Evaluate(
            "pycharm64",
            CommonProcesses,
            new ProcessObservation("WINWORD", false, TimeSpan.FromSeconds(12)),
            IdleLimit);

        Assert.True(result.IsRunning);
        Assert.False(result.IsForeground);
        Assert.False(result.CanAccumulate);
    }

    [Fact]
    public void RejectsLockedOrIdleForegroundProcess()
    {
        var locked = ProcessTrackingDiagnosisEvaluator.Evaluate(
            "pycharm64",
            PyCharmOnly,
            new ProcessObservation("pycharm64", true, TimeSpan.FromSeconds(1)),
            IdleLimit);
        var idle = ProcessTrackingDiagnosisEvaluator.Evaluate(
            "pycharm64",
            PyCharmOnly,
            new ProcessObservation("pycharm64", false, IdleLimit),
            IdleLimit);

        Assert.False(locked.IsUnlocked);
        Assert.False(locked.CanAccumulate);
        Assert.False(idle.HasRecentInput);
        Assert.False(idle.CanAccumulate);
    }
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void RejectsFullscreenOrPresentationMode(bool isFullScreen, bool isPresentationMode)
    {
        var result = ProcessTrackingDiagnosisEvaluator.Evaluate(
            "pycharm64",
            PyCharmOnly,
            new ProcessObservation(
                "pycharm64",
                false,
                TimeSpan.FromSeconds(1),
                isFullScreen,
                isPresentationMode),
            IdleLimit);

        Assert.False(result.CanAccumulate);
        Assert.False(result.IsSuppressionClear);
    }
}