using IkuyoPet.Core.Presentation;

namespace IkuyoPet.Core.Reminders;

public static class ReminderScheduleCalculator
{
    public static ReminderDue? GetDue(
        ReminderRule rule,
        DateTimeOffset now,
        DateTimeOffset? lastDisplayedAt)
    {
        if (!rule.Enabled || rule.IntervalMinutes <= 0)
        {
            return null;
        }

        var localTime = TimeOnly.FromTimeSpan(now.TimeOfDay);
        if (!IsInsideWindow(localTime, rule.StartLocal, rule.EndLocal))
        {
            return null;
        }

        if (lastDisplayedAt is not null &&
            now - lastDisplayedAt.Value < TimeSpan.FromMinutes(rule.IntervalMinutes))
        {
            return null;
        }

        return new ReminderDue(
            Guid.NewGuid(),
            rule.Kind,
            rule.Message,
            [
                new ReminderActionOption(ReminderAction.Complete, "完成啦"),
                new ReminderActionOption(ReminderAction.Snooze, "稍后提醒我"),
                new ReminderActionOption(ReminderAction.Skip, "这次跳过"),
            ]);
    }

    private static bool IsInsideWindow(TimeOnly current, TimeOnly start, TimeOnly end) =>
        start <= end
            ? current >= start && current <= end
            : current >= start || current <= end;
}
