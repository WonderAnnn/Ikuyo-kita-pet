namespace IkuyoPet.Core.Reminders;

public sealed record ReminderRule(
    Guid Id,
    string Kind,
    string Message,
    TimeOnly StartLocal,
    TimeOnly EndLocal,
    int IntervalMinutes,
    bool Enabled);
