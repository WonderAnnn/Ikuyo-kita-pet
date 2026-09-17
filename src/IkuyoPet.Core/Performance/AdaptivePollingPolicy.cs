namespace IkuyoPet.Core.Performance;

/// <summary>
/// Keeps frequent checks while the user is active and backs off when the
/// desktop is locked or idle. Reminder accuracy remains bounded separately.
/// </summary>
public sealed class AdaptivePollingPolicy
{
    private readonly TimeSpan activeWorkInterval = TimeSpan.FromSeconds(30);
    private readonly TimeSpan deferredWorkInterval = TimeSpan.FromSeconds(60);
    private readonly TimeSpan reminderInterval = TimeSpan.FromSeconds(30);
    private readonly TimeSpan diagnosticsInterval = TimeSpan.FromSeconds(15);

    public TimeSpan GetWorkTrackingInterval(bool isLocked, bool isLongIdle) =>
        isLocked || isLongIdle
            ? deferredWorkInterval
            : activeWorkInterval;

    public TimeSpan GetReminderInterval() => reminderInterval;

    public TimeSpan GetDiagnosticsInterval(bool isVisible) =>
        isVisible ? diagnosticsInterval : Timeout.InfiniteTimeSpan;
}
