using IkuyoPet.Core.Reminders;

namespace IkuyoPet.Core.Dashboard;

public sealed record TimelineItem(
    DateTimeOffset ScheduledAt,
    string Kind,
    string Channel,
    ReminderOutcome Outcome,
    DateTimeOffset? ActionAt,
    int RetryIndex);

public sealed record DashboardSnapshot(
    DateOnly Day,
    IReadOnlyList<TimelineItem> Timeline,
    int CompletedCount,
    int SnoozedCount,
    int SkippedCount,
    int UnansweredCount,
    TimeSpan WorkTime);

public enum MainWindowPage
{
    Today,
    Log,
    Rules,
    Pet,
    Settings,
}
