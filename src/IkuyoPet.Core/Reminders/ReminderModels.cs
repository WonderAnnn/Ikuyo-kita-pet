namespace IkuyoPet.Core.Reminders;

public enum ReminderAction
{
    Complete,
    Snooze,
    Skip,
    NoResponse,
}

public enum ReminderOutcome
{
    None,
    Completed,
    Snoozed,
    Skipped,
    Unanswered,
    Suppressed,
}

public sealed record ReminderEvent(
    Guid Id,
    Guid? RuleId,
    DateTimeOffset ScheduledAt,
    DateTimeOffset? DisplayedAt,
    string Channel,
    ReminderOutcome Outcome,
    DateTimeOffset? ActionAt,
    int RetryIndex,
    string? SuppressedReason,
    DateTimeOffset CreatedAt)
{
    public static ReminderEvent Completed(
        Guid id,
        DateTimeOffset scheduledAt,
        string channel) => new(
            id,
            null,
            scheduledAt,
            scheduledAt,
            channel,
            ReminderOutcome.Completed,
            scheduledAt,
            0,
            null,
            scheduledAt);
}

public sealed record ReminderState(int Attempt)
{
    public static ReminderState Pending(int attempt = 1) => new(attempt);
}

public sealed record ReminderTransition(ReminderOutcome Outcome, bool ShouldRetry);