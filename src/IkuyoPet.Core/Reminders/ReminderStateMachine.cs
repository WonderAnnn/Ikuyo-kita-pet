namespace IkuyoPet.Core.Reminders;

public sealed class ReminderStateMachine(int maxAttempts)
{
    public ReminderTransition Apply(ReminderState state, ReminderAction action) => action switch
    {
        ReminderAction.Complete => new(ReminderOutcome.Completed, false),
        ReminderAction.Snooze => new(ReminderOutcome.Snoozed, true),
        ReminderAction.Skip => new(ReminderOutcome.Skipped, false),
        ReminderAction.NoResponse when state.Attempt >= maxAttempts => new(ReminderOutcome.Unanswered, false),
        ReminderAction.NoResponse => new(ReminderOutcome.None, true),
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };
}
