namespace IkuyoPet.Core.Reminders;

public enum ReminderRuntimeStatus
{
    Accumulating,
    Due,
    WaitingRetry,
    Unanswered,
}

public sealed record ReminderRuntimeState
{
    public ReminderRuntimeState(
        Guid ruleId,
        Guid cycleId,
        int targetActiveSeconds,
        int accumulatedActiveSeconds,
        ReminderRuntimeStatus status,
        int attempt,
        DateTimeOffset? retryDueAt,
        DateTimeOffset updatedAt)
    {
        if (ruleId == Guid.Empty)
        {
            throw new ArgumentException("Rule id cannot be empty.", nameof(ruleId));
        }

        if (cycleId == Guid.Empty)
        {
            throw new ArgumentException("Cycle id cannot be empty.", nameof(cycleId));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetActiveSeconds);
        ArgumentOutOfRangeException.ThrowIfNegative(accumulatedActiveSeconds);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            accumulatedActiveSeconds,
            targetActiveSeconds);

        ArgumentOutOfRangeException.ThrowIfNegative(attempt);
        RuleId = ruleId;
        CycleId = cycleId;
        TargetActiveSeconds = targetActiveSeconds;
        AccumulatedActiveSeconds = accumulatedActiveSeconds;
        Status = status;
        Attempt = attempt;
        RetryDueAt = retryDueAt;
        UpdatedAt = updatedAt;
    }

    public Guid RuleId { get; init; }

    public Guid CycleId { get; init; }

    public int TargetActiveSeconds { get; init; }

    public int AccumulatedActiveSeconds { get; init; }

    public ReminderRuntimeStatus Status { get; init; }

    public int Attempt { get; init; }

    public DateTimeOffset? RetryDueAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}
