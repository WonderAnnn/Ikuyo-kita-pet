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

    public int IntervalMinMinutes { get; init; } = IntervalMinutes;

    public int IntervalMaxMinutes { get; init; } = IntervalMinutes;

    public int ActivityDurationMinutes { get; init; } = 5;

    public string ParameterSource { get; init; } = "legacy";

    public string ParameterVersion { get; init; } = "legacy";

    public static ReminderRule CreateDefaultActiveWork(Guid id) => new(
        id,
        "activity",
        "离开屏幕，轻缓活动 5 分钟吧",
        new TimeOnly(8, 0),
        new TimeOnly(23, 0),
        45,
        true)
    {
        IntervalMinMinutes = 40,
        IntervalMaxMinutes = 50,
        ActivityDurationMinutes = 5,
        ParameterSource = "general-default",
        ParameterVersion = "2026-09-09",
    };
}

public sealed record QuietHours(
    TimeOnly StartLocalTime,
    TimeOnly EndLocalTime,
    bool Enabled)
{
    public static QuietHours Disabled { get; } =
        new(TimeOnly.MinValue, TimeOnly.MinValue, false);
}
