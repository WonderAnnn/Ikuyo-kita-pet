using IkuyoPet.Core.Reminders;

namespace IkuyoPet.Core.Dashboard;

public sealed record TimelineItem(
    DateTimeOffset ScheduledAt,
    string Kind,
    string Channel,
    ReminderOutcome Outcome,
    DateTimeOffset? ActionAt,
    int RetryIndex)
{
    public DateTimeOffset LocalScheduledAt => ScheduledAt.ToLocalTime();

    public DateTimeOffset? LocalActionAt => ActionAt?.ToLocalTime();

    public string KindText => Kind switch
    {
        "work" => "工作",
        _ => ReminderKinds.ToDisplayText(Kind),
    };

    public string ChannelText => Channel switch
    {
        "pet" => "桌宠",
        "notification" => "Windows 通知",
        _ => Channel,
    };

    public string OutcomeText => Outcome switch
    {
        ReminderOutcome.Completed => "完成",
        ReminderOutcome.Snoozed => "搁置",
        ReminderOutcome.Skipped => "跳过",
        ReminderOutcome.Unanswered => "未响应",
        ReminderOutcome.None => "待处理",
        ReminderOutcome.Suppressed => "已抑制",
        _ => Outcome.ToString(),
    };
}

public sealed record DashboardSnapshot(
    DateOnly Day,
    IReadOnlyList<TimelineItem> Timeline,
    int CompletedCount,
    int SnoozedCount,
    int SkippedCount,
    int UnansweredCount,
    TimeSpan WorkTime)
{
    public int HydrationCount { get; init; }

    public int ActivityCount { get; init; }

    public TimeSpan WorkDuration => WorkTime;

    public ReminderRule? ActiveWorkRule { get; init; }

    public ReminderRuntimeState? ActiveWorkRuntime { get; init; }

    public ReminderRule? HydrationRule { get; init; }

    public ReminderRuntimeState? HydrationRuntime { get; init; }
}

public enum MainWindowPage
{
    Today,
    Log,
    Rules,
    Diagnostics,
    Pet,
    Settings,
}
