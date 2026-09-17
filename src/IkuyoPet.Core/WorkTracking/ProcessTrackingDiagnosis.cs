namespace IkuyoPet.Core.WorkTracking;

public sealed record ProcessObservation(
    string ProcessName,
    bool IsLocked,
    TimeSpan IdleTime,
    bool IsFullScreen = false,
    bool IsPresentationMode = false);

public sealed record ProcessTrackingDiagnosis(
    bool IsRunning,
    bool IsForeground,
    bool IsUnlocked,
    bool HasRecentInput,
    bool IsSuppressionClear,
    bool CanAccumulate);

public static class ProcessTrackingDiagnosisEvaluator
{
    public static ProcessTrackingDiagnosis Evaluate(
        string expectedProcessName,
        IEnumerable<string> runningProcessNames,
        ProcessObservation current,
        TimeSpan idleLimit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedProcessName);
        ArgumentNullException.ThrowIfNull(runningProcessNames);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(idleLimit, TimeSpan.Zero);

        var expected = expectedProcessName.Trim();
        var isRunning = runningProcessNames.Any(processName =>
            string.Equals(processName, expected, StringComparison.OrdinalIgnoreCase));
        var isForeground = string.Equals(current.ProcessName, expected, StringComparison.OrdinalIgnoreCase);
        var isUnlocked = !current.IsLocked;
        var hasRecentInput = current.IdleTime < idleLimit;
        var isSuppressionClear = !current.IsFullScreen && !current.IsPresentationMode;
        return new ProcessTrackingDiagnosis(
            isRunning,
            isForeground,
            isUnlocked,
            hasRecentInput,
            isSuppressionClear,
            isRunning && isForeground && isUnlocked && hasRecentInput && isSuppressionClear);
    }
}