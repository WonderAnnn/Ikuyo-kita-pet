namespace IkuyoPet.Core.Diagnostics;

/// <summary>Stable severity used by the read-only runtime diagnostic view.</summary>
public enum HealthLevel
{
    Healthy,
    Warning,
    Error,
}

/// <summary>One background loop's latest observable lifecycle state.</summary>
public sealed record LoopHealthSnapshot(
    string Name,
    HealthLevel Level,
    DateTimeOffset? LastStartedAt,
    DateTimeOffset? LastSucceededAt,
    DateTimeOffset? NextWakeAt,
    string? LastErrorCode,
    DateTimeOffset? LastErrorAt);

/// <summary>Metadata only for the latest reminder; reminder text is deliberately excluded.</summary>
public sealed record RecentReminderSnapshot(
    Guid EventId,
    string Kind,
    string Channel,
    string Outcome,
    DateTimeOffset OccurredAt);

/// <summary>An immutable snapshot consumed by UI and future local diagnostics export.</summary>
public sealed record RuntimeHealthSnapshot(
    IReadOnlyList<LoopHealthSnapshot> Loops,
    RecentReminderSnapshot? RecentReminder);
