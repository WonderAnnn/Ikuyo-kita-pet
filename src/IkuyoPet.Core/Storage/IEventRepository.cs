using IkuyoPet.Core.Reminders;
using IkuyoPet.Core.WorkTracking;

namespace IkuyoPet.Core.Storage;

public interface IEventRepository
{
    Task AppendReminderAsync(ReminderEvent item, CancellationToken cancellationToken);

    Task AppendWorkSessionAsync(WorkSession session, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReminderEvent>> ReadReminderEventsAsync(
        DateOnly day,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ReminderRule>> ReadReminderRulesAsync(CancellationToken cancellationToken);

    Task UpsertReminderRuleAsync(ReminderRule rule, CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkSession>> ReadWorkSessionsAsync(
        DateOnly day,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TrackedApplication>> ReadTrackedApplicationsAsync(
        CancellationToken cancellationToken);

    Task UpsertTrackedApplicationAsync(
        TrackedApplication application,
        CancellationToken cancellationToken);
}
