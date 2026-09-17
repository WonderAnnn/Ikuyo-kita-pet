using IkuyoPet.Core.Reminders;
using IkuyoPet.Core.Diagnostics;
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
    private readonly RuntimeHealthRegistry? healthRegistry;

    public ReminderActionCoordinator(
        IEventRepository repository,
        ReminderStateMachine stateMachine,
        TimeProvider timeProvider,
        IReminderIntervalRandom? intervalRandom = null,
        RuntimeHealthRegistry? healthRegistry = null)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.intervalRandom = intervalRandom ?? SharedIntervalRandom.Instance;
        this.healthRegistry = healthRegistry;
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
            healthRegistry?.RecordReminder(new RecentReminderSnapshot(
                item.Id,
                "reminder-action",
                item.Channel,
                transition.Outcome.ToString(),
                updated.ActionAt ?? timeProvider.GetUtcNow()));
        }

        return new ReminderActionResult(applied, transition);
    }

    public Task<ReminderActionResult> HandleManualAsync(
        string reminderKind,
        CancellationToken cancellationToken) =>
        HandleManualAsync(reminderKind, "pet", cancellationToken);

    public async Task<ReminderActionResult> HandleManualAsync(
        string reminderKind,
        string channel,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(reminderKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);

        var isWater = ReminderKinds.IsWallClock(reminderKind);
        var isActivity = ReminderKinds.IsActiveWork(reminderKind);
        if (!isWater && !isActivity)
        {
            throw new ArgumentException(
                $"Unsupported reminder kind: {reminderKind}",
                nameof(reminderKind));
        }

        var rules = await repository.ReadReminderRulesAsync(cancellationToken)
            .ConfigureAwait(false);
        var rule = rules.FirstOrDefault(candidate =>
            isWater
                ? ReminderKinds.IsWallClock(candidate.Kind)
                : ReminderKinds.IsActiveWork(candidate.Kind));
        if (rule is null)
        {
            throw new InvalidOperationException(
                $"No reminder rule is configured for kind '{reminderKind}'.");
        }

        var now = timeProvider.GetUtcNow();
        ReminderRuntimeState? nextRuntime = null;
        try
        {
            var currentRuntime = await repository.ReadReminderRuntimeStateAsync(
                rule.Id,
                cancellationToken).ConfigureAwait(false);
            nextRuntime = isWater
                ? new WallClockReminderScheduler(
                    rule,
                    intervalRandom,
                    currentRuntime,
                    new TimeProviderReminderClock(timeProvider)).StartNewCycle(now)
                : new ActiveWorkReminderScheduler(
                    rule,
                    intervalRandom,
                    currentRuntime,
                    new TimeProviderReminderClock(timeProvider)).StartNewCycle();
        }
        catch (NotSupportedException)
        {
            // Older lightweight repositories can still record the manual action.
        }

        var item = new ReminderEvent(
            Guid.NewGuid(),
            rule.Id,
            now,
            now,
            channel,
            ReminderOutcome.Completed,
            now,
            0,
            null,
            now)
        {
            ActivityDurationMinutes = rule.ActivityDurationMinutes,
            ParameterSource = rule.ParameterSource,
            ParameterVersion = rule.ParameterVersion,
        };
        await repository.AppendReminderAsync(item, cancellationToken).ConfigureAwait(false);

        if (nextRuntime is not null)
        {
            await repository.UpsertReminderRuntimeStateAsync(nextRuntime, cancellationToken)
                .ConfigureAwait(false);
        }

        healthRegistry?.RecordReminder(new RecentReminderSnapshot(
            item.Id,
            reminderKind,
            item.Channel,
            "manual-completed",
            now));
        return new ReminderActionResult(
            true,
            new ReminderTransition(ReminderOutcome.Completed, false));
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

            var now = timeProvider.GetUtcNow();
            if (IsWallClockRule(rule))
            {
                var wallClockScheduler = new WallClockReminderScheduler(
                    rule,
                    intervalRandom,
                    runtime,
                    new TimeProviderReminderClock(timeProvider));
                wallClockScheduler.Apply(action, now);
                if (wallClockScheduler.State.Status == ReminderRuntimeStatus.Unanswered)
                {
                    wallClockScheduler.StartNewCycle(now);
                }
                await repository.UpsertReminderRuntimeStateAsync(
                    wallClockScheduler.State,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var scheduler = new ActiveWorkReminderScheduler(
                    rule,
                    intervalRandom,
                    runtime,
                    new TimeProviderReminderClock(timeProvider));
                scheduler.Apply(action, now);
                if (scheduler.State.Status == ReminderRuntimeStatus.Unanswered)
                {
                    scheduler.StartNewCycle();
                }
                await repository.UpsertReminderRuntimeStateAsync(scheduler.State, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (NotSupportedException)
        {
            // Compatibility for repositories created before persisted reminder runtime state.
        }
    }

    private static bool IsWallClockRule(ReminderRule rule) =>
        string.Equals(rule.Kind, "water", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(rule.Kind, "hydration", StringComparison.OrdinalIgnoreCase);

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
