namespace IkuyoPet.Core.Diagnostics;

/// <summary>Why work time is or is not accumulating right now.</summary>
public sealed record WorkTrackingStatus(
    string? ForegroundProcessName,
    bool IsWhitelistedForeground,
    bool IsLocked,
    bool IsFullScreen,
    bool IsPresentationMode,
    int IdleSeconds,
    bool CanAccumulate,
    IReadOnlyList<string> Reasons);

/// <summary>One rule's scheduler state, next estimate and blocking reasons.</summary>
public sealed record RuleDiagnostics(
    Guid RuleId,
    string Kind,
    string KindText,
    bool Enabled,
    bool IsWallClock,
    string StateText,
    string NextEstimateText,
    IReadOnlyList<string> Reasons);

public sealed record DiagnosticsReport(
    DateTimeOffset GeneratedAt,
    string ReminderChannelText,
    string PauseText,
    string SuppressionText,
    WorkTrackingStatus WorkTracking,
    IReadOnlyList<RuleDiagnostics> Rules)
{
    public static readonly DiagnosticsReport Empty = new(
        DateTimeOffset.MinValue,
        "未知",
        "未知",
        "未知",
        new WorkTrackingStatus(null, false, false, false, false, 0, false, ["尚未诊断"]),
        []);
}
