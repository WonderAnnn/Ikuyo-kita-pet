using IkuyoPet.Core.Reminders;
using IkuyoPet.Core.Storage;

namespace IkuyoPet.Core.Dashboard;

public sealed class DashboardQueryService(
    IEventRepository repository,
    TimeZoneInfo? timeZone = null)
{
    public async Task<DashboardSnapshot> GetAsync(
        DateOnly day,
        CancellationToken cancellationToken)
    {
        var reminderEvents = await repository.ReadReminderEventsAsync(day, cancellationToken);
        var workSessions = await repository.ReadWorkSessionsAsync(day, cancellationToken);
        var rules = await repository.ReadReminderRulesAsync(cancellationToken);
        var kindsByRuleId = rules
            .GroupBy(rule => rule.Id)
            .ToDictionary(group => group.Key, group => group.First().Kind);

        var timeline = reminderEvents
            .OrderBy(item => item.ScheduledAt)
            .ThenBy(item => item.Id)
            .Select(item => new TimelineItem(
                item.ScheduledAt,
                ResolveKind(item.RuleId, kindsByRuleId),
                item.Channel,
                item.Outcome,
                item.ActionAt,
                item.RetryIndex))
            .ToArray();

        return new DashboardSnapshot(
            day,
            timeline,
            Count(reminderEvents, ReminderOutcome.Completed),
            Count(reminderEvents, ReminderOutcome.Snoozed),
            Count(reminderEvents, ReminderOutcome.Skipped),
            Count(reminderEvents, ReminderOutcome.Unanswered),
            CalculateWorkTime(day, workSessions, timeZone ?? TimeZoneInfo.Local))
        {
            HydrationCount = CountCompletedByType(
                reminderEvents,
                kindsByRuleId,
                "water",
                "hydration"),
            ActivityCount = CountCompletedByType(
                reminderEvents,
                kindsByRuleId,
                "activity",
                "move"),
        };
    }

    private static int Count(
        IReadOnlyList<ReminderEvent> reminderEvents,
        ReminderOutcome outcome) => reminderEvents.Count(item => item.Outcome == outcome);

    private static int CountCompletedByType(
        IReadOnlyList<ReminderEvent> reminderEvents,
        Dictionary<Guid, string> typesByRuleId,
        params string[] acceptedTypes) => reminderEvents.Count(item =>
        item.Outcome == ReminderOutcome.Completed &&
        item.RuleId is Guid ruleId &&
        typesByRuleId.TryGetValue(ruleId, out var type) &&
        acceptedTypes.Contains(type, StringComparer.OrdinalIgnoreCase));

    private static string ResolveKind(
        Guid? ruleId,
        Dictionary<Guid, string> kindsByRuleId) =>
        ruleId is Guid id && kindsByRuleId.TryGetValue(id, out var kind)
            ? kind
            : "unknown";

    private static TimeSpan CalculateWorkTime(
        DateOnly day,
        IReadOnlyList<WorkTracking.WorkSession> workSessions,
        TimeZoneInfo timeZone)
    {
        var localStart = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var localEnd = day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var dayStart = new DateTimeOffset(
            localStart,
            timeZone.GetUtcOffset(localStart)).ToUniversalTime();
        var dayEnd = new DateTimeOffset(
            localEnd,
            timeZone.GetUtcOffset(localEnd)).ToUniversalTime();
        decimal totalTicks = 0;

        foreach (var session in workSessions)
        {
            if (session.ActiveSeconds <= 0 || session.EndedAt <= session.StartedAt)
            {
                continue;
            }

            var sessionStart = session.StartedAt.ToUniversalTime();
            var sessionEnd = session.EndedAt.ToUniversalTime();
            var overlapStart = sessionStart > dayStart ? sessionStart : dayStart;
            var overlapEnd = sessionEnd < dayEnd ? sessionEnd : dayEnd;
            if (overlapEnd <= overlapStart)
            {
                continue;
            }

            var overlapTicks = (decimal)(overlapEnd - overlapStart).Ticks;
            var sessionTicks = (decimal)(sessionEnd - sessionStart).Ticks;
            var activeTicks = (decimal)session.ActiveSeconds * TimeSpan.TicksPerSecond;
            var contribution = decimal.Truncate(activeTicks * overlapTicks / sessionTicks);
            var remaining = (decimal)TimeSpan.MaxValue.Ticks - totalTicks;
            if (contribution >= remaining)
            {
                return TimeSpan.MaxValue;
            }

            totalTicks += contribution;
        }

        return TimeSpan.FromTicks((long)totalTicks);
    }
}
