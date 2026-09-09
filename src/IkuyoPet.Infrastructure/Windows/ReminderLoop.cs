using System.Security.Cryptography;
using IkuyoPet.Core.Presentation;
using IkuyoPet.Core.Reminders;
using IkuyoPet.Core.Storage;
using IkuyoPet.Core.WorkTracking;

namespace IkuyoPet.Infrastructure.Windows;

public sealed class ReminderLoop
{
    private static readonly Guid DefaultRuleId = Guid.Parse("76b178f2-1700-4f17-b72a-f4b3e9c52d1f");
    private readonly IEventRepository repository;
    private readonly ReminderPresentationRouter router;
    private readonly Func<bool> isPetEnabled;
    private readonly TimeProvider timeProvider;
    private readonly TimeZoneInfo timeZone;
    private readonly TimeSpan tickInterval;
    private readonly Func<bool> isPaused;
    private readonly IReminderIntervalRandom intervalRandom;
    private readonly Func<bool> isSuppressed;
    private readonly TimeSpan suppressionRecoveryDelay;
    private readonly ReminderActionCoordinator? actionCoordinator;
    private bool suppressionObserved;
    private DateTimeOffset? recoveryStartedAt;
    private readonly Dictionary<Guid, DateTimeOffset> legacyLastDisplayedAt = [];

    public ReminderLoop(
        IEventRepository repository,
        ReminderPresentationRouter router,
        Func<bool> isPetEnabled,
        TimeProvider timeProvider,
        TimeZoneInfo timeZone,
        TimeSpan? tickInterval = null,
        Func<bool>? isPaused = null,
        IReminderIntervalRandom? intervalRandom = null,
        Func<bool>? isSuppressed = null,
        TimeSpan? suppressionRecoveryDelay = null,
        ReminderActionCoordinator? actionCoordinator = null)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.router = router ?? throw new ArgumentNullException(nameof(router));
        this.isPetEnabled = isPetEnabled ?? throw new ArgumentNullException(nameof(isPetEnabled));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.timeZone = timeZone ?? throw new ArgumentNullException(nameof(timeZone));
        this.tickInterval = tickInterval ?? TimeSpan.FromSeconds(30);
        this.isPaused = isPaused ?? (() => false);
        this.intervalRandom = intervalRandom ?? SharedIntervalRandom.Instance;
        this.isSuppressed = isSuppressed ?? (() => false);
        this.suppressionRecoveryDelay = suppressionRecoveryDelay ?? TimeSpan.FromMinutes(2);
        this.actionCoordinator = actionCoordinator;
        ArgumentOutOfRangeException.ThrowIfLessThan(this.tickInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(this.suppressionRecoveryDelay, TimeSpan.Zero);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ProcessOnceAsync(cancellationToken).ConfigureAwait(false);
            if (tickInterval == TimeSpan.Zero) await Task.Yield();
            else await Task.Delay(tickInterval, timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task ConsumeActiveWorkAsync(
        ActiveWorkDelta delta,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delta);
        cancellationToken.ThrowIfCancellationRequested();
        if (delta.ActiveSeconds <= 0) return;

        var rules = await ReadRulesEnsuringDefaultAsync(cancellationToken).ConfigureAwait(false);
        foreach (var rule in rules.Where(static candidate => candidate.Enabled))
        {
            var previous = await repository.ReadReminderRuntimeStateAsync(rule.Id, cancellationToken)
                .ConfigureAwait(false);
            var scheduler = new ActiveWorkReminderScheduler(
                rule,
                intervalRandom,
                previous,
                new TimeProviderReminderClock(timeProvider));
            if (previous is null) scheduler.StartNewCycle();
            scheduler.ObserveActiveSeconds(delta.ActiveSeconds);
            if (previous is null)
            {
                await repository.UpsertReminderRuntimeStateAsync(scheduler.State, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (scheduler.State != previous)
            {
                try
                {
                    await repository.TryUpdateReminderRuntimeStateAsync(
                        previous,
                        scheduler.State,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (NotSupportedException)
                {
                    await repository.UpsertReminderRuntimeStateAsync(scheduler.State, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
    }

    public async Task ProcessOnceAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = timeProvider.GetUtcNow();
        var rules = await ReadRulesEnsuringDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (isPaused()) return;
        if (!CanPresentAfterSuppression(now)) return;
        await HandleExpiredPendingAsync(now, cancellationToken).ConfigureAwait(false);

        foreach (var rule in rules.Where(static candidate => candidate.Enabled))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReminderRuntimeState? runtime;
            try
            {
                runtime = await repository.ReadReminderRuntimeStateAsync(rule.Id, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (NotSupportedException)
            {
                await ProcessLegacyRuleAsync(rule, now, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var scheduler = new ActiveWorkReminderScheduler(
                rule,
                intervalRandom,
                runtime,
                new TimeProviderReminderClock(timeProvider));
            if (runtime is null)
            {
                scheduler.StartNewCycle();
                await repository.UpsertReminderRuntimeStateAsync(scheduler.State, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            if (scheduler.State.Status == ReminderRuntimeStatus.WaitingRetry)
            {
                if (!scheduler.ObserveActiveSeconds(0)) continue;
                await repository.UpsertReminderRuntimeStateAsync(scheduler.State, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (scheduler.State.Status != ReminderRuntimeStatus.Due) continue;
            await PresentDueAsync(rule, scheduler.State, now, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleExpiredPendingAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (actionCoordinator is null) return;

        var localDate = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(now, timeZone).DateTime);
        IReadOnlyList<ReminderEvent> events;
        try
        {
            events = await repository.ReadReminderEventsAsync(localDate, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (NotSupportedException)
        {
            return;
        }

        var cutoff = now - TimeSpan.FromMinutes(5);
        foreach (var item in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.Outcome != ReminderOutcome.None) continue;
            var displayedAt = item.DisplayedAt ?? item.CreatedAt;
            if (displayedAt > cutoff) continue;

            await actionCoordinator.HandleAsync(
                item.Id,
                ReminderAction.NoResponse,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private bool CanPresentAfterSuppression(DateTimeOffset now)
    {
        if (isSuppressed())
        {
            suppressionObserved = true;
            recoveryStartedAt = null;
            return false;
        }

        if (!suppressionObserved) return true;
        recoveryStartedAt ??= now;
        if (now - recoveryStartedAt.Value < suppressionRecoveryDelay) return false;
        suppressionObserved = false;
        recoveryStartedAt = null;
        return true;
    }
    private async Task<IReadOnlyList<ReminderRule>> ReadRulesEnsuringDefaultAsync(
        CancellationToken cancellationToken)
    {
        var rules = await repository.ReadReminderRulesAsync(cancellationToken).ConfigureAwait(false);
        if (rules.Count != 0) return rules;

        try
        {
            await repository.TryAddDefaultReminderRuleAsync(
                ReminderRule.CreateDefaultActiveWork(DefaultRuleId),
                cancellationToken).ConfigureAwait(false);
            return await repository.ReadReminderRulesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (NotSupportedException)
        {
            return rules;
        }
    }


    private async Task PresentDueAsync(
        ReminderRule rule,
        ReminderRuntimeState state,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var eventId = CreateEventId(state.CycleId, state.Attempt);
        if (await repository.ReadReminderEventAsync(eventId, cancellationToken).ConfigureAwait(false) is not null)
        {
            return;
        }

        var due = new ReminderDue(
            eventId,
            rule.Kind,
            rule.Message,
            [
                new ReminderActionOption(ReminderAction.Complete, "好呀，我现在去"),
                new ReminderActionOption(ReminderAction.Snooze, "再等我五分钟"),
                new ReminderActionOption(ReminderAction.Skip, "这次先算啦"),
            ]);
        var petEnabled = isPetEnabled();
        var item = new ReminderEvent(
            eventId,
            rule.Id,
            now,
            now,
            router.GetChannel(petEnabled),
            ReminderOutcome.None,
            null,
            Math.Max(0, state.Attempt - 1),
            null,
            now)
        {
            ActivityDurationMinutes = rule.ActivityDurationMinutes,
            ParameterSource = rule.ParameterSource,
            ParameterVersion = rule.ParameterVersion,
        };
        await repository.AppendReminderAsync(item, cancellationToken).ConfigureAwait(false);
        var channel = await router.ShowAsync(due, petEnabled, cancellationToken).ConfigureAwait(false);
        if (channel != item.Channel &&
            !await repository.UpdateReminderChannelAsync(
                item.Id, item.Channel, channel, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"Reminder event '{item.Id}' channel could not be updated.");
        }
    }

    private async Task ProcessLegacyRuleAsync(
        ReminderRule rule,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var previous = legacyLastDisplayedAt.TryGetValue(rule.Id, out var displayedAt)
            ? displayedAt
            : (DateTimeOffset?)null;
        var due = ReminderScheduleCalculator.GetDue(rule, now, previous, timeZone);
        if (due is null) return;

        var petEnabled = isPetEnabled();
        var item = new ReminderEvent(
            due.EventId, rule.Id, now, now, router.GetChannel(petEnabled),
            ReminderOutcome.None, null, 0, null, now);
        await repository.AppendReminderAsync(item, cancellationToken).ConfigureAwait(false);
        var channel = await router.ShowAsync(due, petEnabled, cancellationToken).ConfigureAwait(false);
        if (channel != item.Channel &&
            !await repository.UpdateReminderChannelAsync(
                item.Id, item.Channel, channel, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"Reminder event '{item.Id}' channel could not be updated.");
        }

        legacyLastDisplayedAt[rule.Id] = now;
    }

    private static Guid CreateEventId(Guid cycleId, int attempt)
    {
        Span<byte> input = stackalloc byte[20];
        cycleId.TryWriteBytes(input);
        BitConverter.TryWriteBytes(input[16..], attempt);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        return new Guid(hash[..16]);
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