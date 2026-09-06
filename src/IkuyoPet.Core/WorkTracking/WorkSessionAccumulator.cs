namespace IkuyoPet.Core.WorkTracking;

/// <summary>
/// Accumulates time only while the same whitelisted application remains foreground,
/// the workstation is unlocked, and recent user activity is observed.
/// </summary>
public sealed class WorkSessionAccumulator
{
    private readonly TimeSpan _idleLimit;
    private ActivitySample? _previous;

    public WorkSessionAccumulator(TimeSpan idleLimit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(idleLimit, TimeSpan.Zero);

        _idleLimit = idleLimit;
    }

    public TimeSpan ActiveTime { get; private set; }

    public void Observe(ActivitySample current)
    {
        if (_previous is { } previous &&
            previous.AppName == current.AppName &&
            previous.IsWhitelistedForeground &&
            !previous.IsLocked &&
            previous.IdleTime < _idleLimit)
        {
            var elapsed = current.ObservedAt - previous.ObservedAt;
            if (elapsed > TimeSpan.Zero)
            {
                ActiveTime += elapsed;
            }
        }

        _previous = current;
    }
}
