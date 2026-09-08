using IkuyoPet.Core.Reminders;
using IkuyoPet.Core.WorkTracking;

namespace IkuyoPet.Core.Storage;

public interface IEventRepository
{
    Task AppendReminderAsync(ReminderEvent item, CancellationToken cancellationToken);

    Task<ReminderEvent?> ReadReminderEventAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> TryUpdateReminderAsync(
        ReminderEvent item,
        ReminderOutcome expectedOutcome,
        int expectedRetryIndex,
        CancellationToken cancellationToken);

    Task<bool> UpdateReminderOutcomeAsync(
        ReminderEvent item,
        ReminderOutcome expectedOutcome,
        int expectedRetryIndex,
        CancellationToken cancellationToken) =>
        TryUpdateReminderAsync(
            item,
            expectedOutcome,
            expectedRetryIndex,
            cancellationToken);

    Task<bool> UpdateReminderChannelAsync(
        Guid id,
        string expectedChannel,
        string channel,
        CancellationToken cancellationToken);

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
