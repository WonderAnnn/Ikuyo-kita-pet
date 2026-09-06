namespace IkuyoPet.Core.WorkTracking;

/// <summary>
/// A point-in-time observation used to decide whether foreground work time may be accumulated.
/// </summary>
public sealed record ActivitySample(
    string AppName,
    bool IsWhitelistedForeground,
    bool IsLocked,
    TimeSpan IdleTime,
    DateTimeOffset ObservedAt);

public sealed record WorkSession(
    Guid Id,
    string ProcessName,
    string DisplayName,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int ActiveSeconds,
    string EndReason);