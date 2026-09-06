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
}

public sealed record ReminderState(int Attempt)
{
    public static ReminderState Pending(int attempt = 1) => new(attempt);
}

public sealed record ReminderTransition(ReminderOutcome Outcome, bool ShouldRetry);
