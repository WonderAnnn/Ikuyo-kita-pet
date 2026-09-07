using IkuyoPet.Core.Reminders;
using IkuyoPet.Core.Storage;

namespace IkuyoPet.Core.Dashboard;

public sealed class DashboardQueryService(IEventRepository repository)
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
            TimeSpan.FromSeconds(workSessions.Sum(session => (long)session.ActiveSeconds)));
    }

    private static int Count(
        IReadOnlyList<ReminderEvent> reminderEvents,
        ReminderOutcome outcome) => reminderEvents.Count(item => item.Outcome == outcome);

    private static string ResolveKind(
        Guid? ruleId,
        Dictionary<Guid, string> kindsByRuleId) =>
        ruleId is Guid id && kindsByRuleId.TryGetValue(id, out var kind)
            ? kind
            : "unknown";
}
