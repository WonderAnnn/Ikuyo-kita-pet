namespace IkuyoPet.Core.Reminders;

public sealed record ReminderRule(
    Guid Id,
    string Kind,
    string Message,
    TimeOnly StartLocal,
    TimeOnly EndLocal,
    int IntervalMinutes,
    bool Enabled)
{
    public string Type => Kind;

    public TimeOnly StartLocalTime => StartLocal;

    public TimeOnly EndLocalTime => EndLocal;

    public int DailyGoal { get; init; }

    public QuietHours QuietHours { get; init; } =
        global::IkuyoPet.Core.Reminders.QuietHours.Disabled;
}

public sealed record QuietHours(
    TimeOnly StartLocalTime,
    TimeOnly EndLocalTime,
    bool Enabled)
{
    public static QuietHours Disabled { get; } =
        new(TimeOnly.MinValue, TimeOnly.MinValue, false);
}
