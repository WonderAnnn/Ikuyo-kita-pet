using System.Collections.ObjectModel;

namespace IkuyoPet.Core.Diagnostics;

/// <summary>
/// Thread-safe in-memory health state for long-running loops. It keeps only
/// stable codes and timestamps: exception details and reminder text stay out
/// of this snapshot.
/// </summary>
public sealed class RuntimeHealthRegistry
{
    private readonly object gate = new();
    private readonly Dictionary<string, LoopState> loops = new(StringComparer.Ordinal);
    private RecentReminderSnapshot? recentReminder;

    public void MarkLoopStarted(string loop, DateTimeOffset at) =>
        Update(loop, at, state => state with
        {
            Level = HealthLevel.Healthy,
            LastStartedAt = at,
            LastErrorCode = null,
            LastErrorAt = null,
        });

    public void MarkLoopSucceeded(string loop, DateTimeOffset at, DateTimeOffset nextWakeAt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(nextWakeAt, at);
        Update(loop, at, state => state with
        {
            Level = HealthLevel.Healthy,
            LastSucceededAt = at,
            NextWakeAt = nextWakeAt,
            LastErrorCode = null,
            LastErrorAt = null,
        });
    }

    public void MarkLoopFailed(string loop, string errorCode, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        Update(loop, at, state => state with
        {
            Level = HealthLevel.Error,
            LastErrorCode = errorCode,
            LastErrorAt = at,
        });
    }

    public void RecordReminder(RecentReminderSnapshot reminder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reminder.Kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(reminder.Channel);
        ArgumentException.ThrowIfNullOrWhiteSpace(reminder.Outcome);

        lock (gate)
        {
            if (recentReminder is null || reminder.OccurredAt >= recentReminder.OccurredAt)
            {
                recentReminder = reminder;
            }
        }
    }

    public RuntimeHealthSnapshot GetSnapshot()
    {
        lock (gate)
        {
            var snapshot = loops.Values
                .OrderBy(static state => state.Name, StringComparer.Ordinal)
                .Select(static state => new LoopHealthSnapshot(
                    state.Name,
                    state.Level,
                    state.LastStartedAt,
                    state.LastSucceededAt,
                    state.NextWakeAt,
                    state.LastErrorCode,
                    state.LastErrorAt))
                .ToArray();
            return new RuntimeHealthSnapshot(Array.AsReadOnly(snapshot), recentReminder);
        }
    }

    private void Update(string loop, DateTimeOffset at, Func<LoopState, LoopState> update)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(loop);
        lock (gate)
        {
            if (loops.TryGetValue(loop, out var current) && at < current.LastObservedAt)
            {
                return;
            }

            var initial = current ?? new LoopState(
                loop,
                HealthLevel.Healthy,
                null,
                null,
                null,
                null,
                null,
                DateTimeOffset.MinValue);
            loops[loop] = update(initial) with { LastObservedAt = at };
        }
    }

    private sealed record LoopState(
        string Name,
        HealthLevel Level,
        DateTimeOffset? LastStartedAt,
        DateTimeOffset? LastSucceededAt,
        DateTimeOffset? NextWakeAt,
        string? LastErrorCode,
        DateTimeOffset? LastErrorAt,
        DateTimeOffset LastObservedAt);
}
