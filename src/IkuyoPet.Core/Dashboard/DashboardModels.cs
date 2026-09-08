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
    public string KindText => Kind switch
    {
        "water" or "hydration" => "喝水",
        "activity" or "move" => "活动",
        "work" => "工作",
        _ => Kind,
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
}

public enum MainWindowPage
{
    Today,
    Log,
    Rules,
    Pet,
    Settings,
}
