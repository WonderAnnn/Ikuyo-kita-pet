namespace IkuyoPet.Core.WorkTracking;

/// <summary>
/// A point-in-time observation used to decide whether foreground work time may be accumulated.
/// </summary>
public sealed record ActivitySample(
    string AppName,
    bool IsWhitelistedForeground,
    bool IsLocked,
    TimeSpan IdleTime,
    DateTimeOffset ObservedAt,
    bool IsFullScreen = false,
    bool IsPresentationMode = false);

/// <summary>
/// The single active-work increment produced by one foreground sample cycle.
/// Consumers must share this value rather than probing the desktop again.
/// </summary>
public sealed record ActiveWorkDelta(
    int ActiveSeconds,
    string? ProcessName,
    DateTimeOffset ObservedAt);

public sealed record WorkSession(
    Guid Id,
    string ProcessName,
    string DisplayName,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int ActiveSeconds,
    string EndReason);

public sealed record TrackedApplication(
    string ProcessName,
    string DisplayName,
    bool Enabled);
