using IkuyoPet.Core.Reminders;
using IkuyoPet.Core.Storage;

namespace IkuyoPet.Infrastructure.Windows;

public readonly record struct ReminderActionResult(
    bool Applied,
    ReminderTransition Transition);

public sealed class ReminderActionCoordinator(
    IEventRepository repository,
    ReminderStateMachine stateMachine,
    TimeProvider timeProvider)
{
    public async Task<ReminderActionResult?> HandleAsync(
        Guid eventId,
        ReminderAction action,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var item = await repository.ReadReminderEventAsync(eventId, cancellationToken);
        if (item is null)
        {
            return null;
        }

        if (item.Outcome != ReminderOutcome.None)
        {
            return new ReminderActionResult(
                false,
                new ReminderTransition(item.Outcome, false));
        }

        var transition = stateMachine.Apply(
            ReminderState.Pending(item.RetryIndex + 1),
            action);
        var retryIndex = action is ReminderAction.Snooze or ReminderAction.NoResponse
            ? checked(item.RetryIndex + 1)
            : item.RetryIndex;
        var updated = item with
        {
            Outcome = transition.Outcome,
            ActionAt = timeProvider.GetUtcNow(),
            RetryIndex = retryIndex,
        };
        var applied = await repository.UpdateReminderOutcomeAsync(
            updated,
            item.Outcome,
            item.RetryIndex,
            cancellationToken);
        return new ReminderActionResult(applied, transition);
    }
}
