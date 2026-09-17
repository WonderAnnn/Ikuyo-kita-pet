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

    Task UpsertReminderRulesAsync(
        IReadOnlyList<ReminderRule> rules,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    Task<bool> TryAddDefaultReminderRuleAsync(
        ReminderRule rule,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    Task<ReminderRuntimeState?> ReadReminderRuntimeStateAsync(
        Guid ruleId,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    Task UpsertReminderRuntimeStateAsync(
        ReminderRuntimeState state,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    Task<bool> TryUpdateReminderRuntimeStateAsync(
        ReminderRuntimeState expected,
        ReminderRuntimeState updated,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    Task<IReadOnlyList<WorkSession>> ReadWorkSessionsAsync(
        DateOnly day,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkSession>> ReadAllWorkSessionsAsync(
        CancellationToken cancellationToken) => throw new NotSupportedException();

    Task<IReadOnlyList<TrackedApplication>> ReadTrackedApplicationsAsync(
        CancellationToken cancellationToken);

    Task UpsertTrackedApplicationAsync(
        TrackedApplication application,
        CancellationToken cancellationToken);
}
