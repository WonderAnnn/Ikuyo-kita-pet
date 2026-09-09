using IkuyoPet.Core.Reminders;
using IkuyoPet.Core.Storage;

namespace IkuyoPet.Infrastructure.Windows;

public readonly record struct ReminderActionResult(
    bool Applied,
    ReminderTransition Transition);

public sealed class ReminderActionCoordinator
{
    private readonly IEventRepository repository;
    private readonly ReminderStateMachine stateMachine;
    private readonly TimeProvider timeProvider;
    private readonly IReminderIntervalRandom intervalRandom;

    public ReminderActionCoordinator(
        IEventRepository repository,
        ReminderStateMachine stateMachine,
        TimeProvider timeProvider,
        IReminderIntervalRandom? intervalRandom = null)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.intervalRandom = intervalRandom ?? SharedIntervalRandom.Instance;
    }

    public async Task<ReminderActionResult?> HandleAsync(
        Guid eventId,
        ReminderAction action,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var item = await repository.ReadReminderEventAsync(eventId, cancellationToken).ConfigureAwait(false);
        if (item is null) return null;

        if (item.Outcome != ReminderOutcome.None ||
            string.Equals(item.SuppressedReason, "no-response", StringComparison.Ordinal))
        {
            return new ReminderActionResult(false, new ReminderTransition(item.Outcome, false));
        }

        var transition = stateMachine.Apply(ReminderState.Pending(item.RetryIndex + 1), action);
        var retryIndex = action is ReminderAction.Snooze or ReminderAction.NoResponse
            ? checked(item.RetryIndex + 1)
            : item.RetryIndex;
        var updated = item with
        {
            Outcome = transition.Outcome,
            ActionAt = timeProvider.GetUtcNow(),
            RetryIndex = retryIndex,
            SuppressedReason = action == ReminderAction.NoResponse && transition.ShouldRetry
                ? "no-response"
                : item.SuppressedReason,
        };
        var applied = await repository.UpdateReminderOutcomeAsync(
            updated, item.Outcome, item.RetryIndex, cancellationToken).ConfigureAwait(false);
        if (applied)
        {
            await PersistRuntimeTransitionAsync(item, action, cancellationToken).ConfigureAwait(false);
        }

        return new ReminderActionResult(applied, transition);
    }

    private async Task PersistRuntimeTransitionAsync(
        ReminderEvent item,
        ReminderAction action,
        CancellationToken cancellationToken)
    {
        if (item.RuleId is not { } ruleId) return;
        try
        {
            var runtime = await repository.ReadReminderRuntimeStateAsync(ruleId, cancellationToken)
                .ConfigureAwait(false);
            if (runtime is null || runtime.Status != ReminderRuntimeStatus.Due) return;
            var rule = (await repository.ReadReminderRulesAsync(cancellationToken).ConfigureAwait(false))
                .SingleOrDefault(candidate => candidate.Id == ruleId);
            if (rule is null) return;

            var scheduler = new ActiveWorkReminderScheduler(
                rule,
                intervalRandom,
                runtime,
                new TimeProviderReminderClock(timeProvider));
            scheduler.Apply(action, timeProvider.GetUtcNow());
            if (scheduler.State.Status == ReminderRuntimeStatus.Unanswered)
            {
                scheduler.StartNewCycle();
            }
            await repository.UpsertReminderRuntimeStateAsync(scheduler.State, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (NotSupportedException)
        {
            // Compatibility for repositories created before persisted reminder runtime state.
        }
    }

    private sealed class TimeProviderReminderClock(TimeProvider provider) : IReminderClock
    {
        public DateTimeOffset UtcNow => provider.GetUtcNow();
    }

    private sealed class SharedIntervalRandom : IReminderIntervalRandom
    {
        public static SharedIntervalRandom Instance { get; } = new();
        public int NextInclusive(int minimum, int maximum) => Random.Shared.Next(minimum, maximum + 1);
    }
}