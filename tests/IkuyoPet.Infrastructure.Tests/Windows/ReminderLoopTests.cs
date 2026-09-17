using IkuyoPet.Core.Presentation;
using IkuyoPet.Core.Reminders;
using IkuyoPet.Core.Storage;
using IkuyoPet.Core.WorkTracking;
using IkuyoPet.Infrastructure.Windows;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.Windows;

public sealed class ReminderLoopTests
{
    private static readonly TimeZoneInfo FixedZone = TimeZoneInfo.CreateCustomTimeZone(
        "IkuyoPet-ReminderLoop-UTC+08",
        TimeSpan.FromHours(8),
        "IkuyoPet ReminderLoop UTC+08",
        "IkuyoPet ReminderLoop UTC+08");

    [Fact]
    public async Task DueEnabledRuleIsPersistedThenPresented()
    {
        var now = new DateTimeOffset(2026, 9, 7, 10, 0, 0, TimeSpan.FromHours(8));
        var repository = new ReminderRepositoryStub([CreateRule(enabled: true)]);
        var pet = new RecordingPresenter("pet", repository);
        var notification = new RecordingPresenter("notification", repository);
        var loop = CreateLoop(repository, pet, notification, now, petEnabled: true);

        await loop.ProcessOnceAsync(TestContext.Current.CancellationToken);

        var item = Assert.Single(repository.Events);
        var due = Assert.Single(pet.DueItems);
        Assert.Equal(item.Id, due.EventId);
        Assert.Equal(item.RuleId, repository.Rules[0].Id);
        Assert.Equal(ReminderOutcome.None, item.Outcome);
        Assert.Equal(now, item.ScheduledAt);
        Assert.Equal(now, item.DisplayedAt);
        Assert.Equal("pet", item.Channel);
        Assert.True(pet.RepositoryContainedEventWhenShown);
        Assert.Empty(notification.DueItems);
    }

    [Theory]
    [InlineData(false, 10)]
    [InlineData(true, 20)]
    public async Task DisabledOrOutsideWindowRuleIsNotPresented(bool enabled, int hour)
    {
        var repository = new ReminderRepositoryStub([CreateRule(enabled)]);
        var pet = new RecordingPresenter("pet", repository);
        var loop = CreateLoop(
            repository,
            pet,
            new RecordingPresenter("notification", repository),
            new DateTimeOffset(2026, 9, 7, hour, 0, 0, TimeSpan.FromHours(8)),
            petEnabled: true);

        await loop.ProcessOnceAsync(TestContext.Current.CancellationToken);

        Assert.Empty(repository.Events);
        Assert.Empty(pet.DueItems);
    }

    [Fact]
    public async Task IntervalPreventsDuplicatePresentationOfSameRule()
    {
        var now = new DateTimeOffset(2026, 9, 7, 10, 0, 0, TimeSpan.FromHours(8));
        var clock = new MutableTimeProvider(now);
        var repository = new ReminderRepositoryStub([CreateRule(enabled: true)]);
        var pet = new RecordingPresenter("pet", repository);
        var loop = new ReminderLoop(
            repository,
            new ReminderPresentationRouter(pet, new RecordingPresenter("notification", repository)),
            () => true,
            clock,
            FixedZone,
            TimeSpan.Zero);

        await loop.ProcessOnceAsync(TestContext.Current.CancellationToken);
        clock.UtcNow = now.AddMinutes(29);
        await loop.ProcessOnceAsync(TestContext.Current.CancellationToken);

        Assert.Single(repository.Events);
        Assert.Single(pet.DueItems);
    }

    [Fact]
    public async Task WaterRuleUsesWallClockAndIgnoresActiveWorkSamples()
    {
        var now = new DateTimeOffset(2026, 9, 7, 10, 0, 0, TimeSpan.FromHours(8));
        var clock = new MutableTimeProvider(now);
        var rule = new ReminderRule(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "water",
            "喝口水嘛～去接一杯，顺便走两步回来，好不好？ദ്ദി˶>𖥦<)✧",
            new TimeOnly(8, 0),
            new TimeOnly(23, 0),
            15,
            true)
        {
            IntervalMinMinutes = 15,
            IntervalMaxMinutes = 15,
            ActivityDurationMinutes = 0,
        };
        var repository = new ReminderRepositoryStub([rule], supportsRuntimeState: true);
        var pet = new RecordingPresenter("pet", repository);
        var loop = new ReminderLoop(
            repository,
            new ReminderPresentationRouter(pet, new RecordingPresenter("notification", repository)),
            () => true,
            clock,
            FixedZone,
            TimeSpan.Zero,
            intervalRandom: new FixedIntervalRandom(15));

        await loop.ProcessOnceAsync(TestContext.Current.CancellationToken);
        await loop.ConsumeActiveWorkAsync(
            new ActiveWorkDelta(3_600, "pycharm64", now.AddMinutes(1)),
            TestContext.Current.CancellationToken);
        clock.UtcNow = now.AddMinutes(16);
        await loop.ProcessOnceAsync(TestContext.Current.CancellationToken);

        var due = Assert.Single(pet.DueItems);
        Assert.Equal("water", due.Kind);
        Assert.Contains("喝口水", due.Message);
    }

    [Fact]
    public async Task RestartRestoresDueCycleAndDoesNotPresentTheSameEventTwice()
    {
        var started = new DateTimeOffset(2026, 9, 7, 9, 0, 0, TimeSpan.FromHours(8));
        var clock = new MutableTimeProvider(started);
        var repository = new ReminderRepositoryStub([CreateRule(enabled: true)], supportsRuntimeState: true);
        var pet = new RecordingPresenter("pet", repository);
        var notification = new RecordingPresenter("notification", repository);
        var first = new ReminderLoop(repository, new ReminderPresentationRouter(pet, notification), () => true,
            clock, FixedZone, TimeSpan.Zero, intervalRandom: new FixedIntervalRandom(30));
        await first.ProcessOnceAsync(TestContext.Current.CancellationToken);
        clock.UtcNow = started.AddMinutes(31);
        await first.ProcessOnceAsync(TestContext.Current.CancellationToken);

        var restarted = new ReminderLoop(repository, new ReminderPresentationRouter(pet, notification), () => true,
            clock, FixedZone, TimeSpan.Zero, intervalRandom: new FixedIntervalRandom(30));
        await restarted.ProcessOnceAsync(TestContext.Current.CancellationToken);

        Assert.Single(repository.Events);
        Assert.Single(pet.DueItems);
    }

    [Fact]
    public async Task WallClockRuntimeAccumulatesAcrossMidnightWindowAfterRestart()
    {
        var rule = CreateRule(enabled: true) with
        {
            StartLocal = new TimeOnly(22, 0),
            EndLocal = new TimeOnly(2, 0),
            IntervalMinMinutes = 30,
            IntervalMaxMinutes = 30,
        };
        var started = new DateTimeOffset(2026, 9, 7, 23, 45, 0, TimeSpan.FromHours(8));
        var clock = new MutableTimeProvider(started);
        var repository = new ReminderRepositoryStub([rule], supportsRuntimeState: true);
        var pet = new RecordingPresenter("pet", repository);
        var router = new ReminderPresentationRouter(pet, new RecordingPresenter("notification", repository));
        await new ReminderLoop(repository, router, () => true, clock, FixedZone, TimeSpan.Zero,
            intervalRandom: new FixedIntervalRandom(30)).ProcessOnceAsync(TestContext.Current.CancellationToken);
        clock.UtcNow = started.AddMinutes(35);

        await new ReminderLoop(repository, router, () => true, clock, FixedZone, TimeSpan.Zero,
            intervalRandom: new FixedIntervalRandom(30)).ProcessOnceAsync(TestContext.Current.CancellationToken);

        Assert.Single(repository.Events);
        Assert.Single(pet.DueItems);
    }

    [Fact]
    public async Task RunAsyncPropagatesCancellationWhileWaitingForNextTick()
    {
        var repository = new ReminderRepositoryStub([]);
        var presenter = new RecordingPresenter("pet", repository);
        var loop = CreateLoop(
            repository,
            presenter,
            new RecordingPresenter("notification", repository),
            new DateTimeOffset(2026, 9, 7, 10, 0, 0, TimeSpan.FromHours(8)),
            petEnabled: true);
        using var cancellation = new CancellationTokenSource();
        var running = loop.RunAsync(cancellation.Token);
        await repository.RulesRead.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }

    [Fact]
    public async Task PetEnabledSelectionRoutesEachDueItemToCurrentPresenter()
    {
        var now = new DateTimeOffset(2026, 9, 7, 10, 0, 0, TimeSpan.FromHours(8));
        var clock = new MutableTimeProvider(now);
        var repository = new ReminderRepositoryStub([CreateRule(enabled: true)]);
        var pet = new RecordingPresenter("pet", repository);
        var notification = new RecordingPresenter("notification", repository);
        var petEnabled = true;
        var loop = new ReminderLoop(
            repository,
            new ReminderPresentationRouter(pet, notification),
            () => petEnabled,
            clock,
            FixedZone,
            TimeSpan.Zero);

        await loop.ProcessOnceAsync(TestContext.Current.CancellationToken);
        petEnabled = false;
        clock.UtcNow = now.AddMinutes(30);
        await loop.ProcessOnceAsync(TestContext.Current.CancellationToken);

        Assert.Single(pet.DueItems);
        Assert.Single(notification.DueItems);
        Assert.Equal(["pet", "notification"], repository.Events.Select(item => item.Channel));
    }

    [Fact]
    public async Task FailedPresentationDoesNotAdvanceIntervalAndCanBeRetried()
    {
        var now = new DateTimeOffset(2026, 9, 7, 10, 0, 0, TimeSpan.FromHours(8));
        var repository = new ReminderRepositoryStub([CreateRule(enabled: true)]);
        var pet = new RecordingPresenter("pet", repository, shouldThrow: true);
        var notification = new RecordingPresenter("notification", repository, shouldThrow: true);
        var loop = CreateLoop(repository, pet, notification, now, petEnabled: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            loop.ProcessOnceAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            loop.ProcessOnceAsync(TestContext.Current.CancellationToken));

        Assert.Equal(2, pet.Attempts);
        Assert.Equal(2, notification.Attempts);
        Assert.Equal(2, repository.Events.Count);
    }

    [Fact]
    public async Task PetFallbackUpdatesPersistedEventToNotificationChannel()
    {
        var now = new DateTimeOffset(2026, 9, 7, 10, 0, 0, TimeSpan.FromHours(8));
        var repository = new ReminderRepositoryStub([CreateRule(enabled: true)]);
        var pet = new RecordingPresenter("pet", repository, shouldThrow: true);
        var notification = new RecordingPresenter("notification", repository);
        var loop = CreateLoop(repository, pet, notification, now, petEnabled: true);

        await loop.ProcessOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal("notification", Assert.Single(repository.Events).Channel);
        Assert.True(notification.RepositoryContainedEventWhenShown);
    }

    private static ReminderLoop CreateLoop(
        ReminderRepositoryStub repository,
        IReminderPresenter pet,
        IReminderPresenter notification,
        DateTimeOffset now,
        bool petEnabled) => new(
            repository,
            new ReminderPresentationRouter(pet, notification),
            () => petEnabled,
            new MutableTimeProvider(now),
            FixedZone,
            TimeSpan.Zero);

    private static ReminderRule CreateRule(bool enabled) => new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        "water",
        "喝口水再继续吧～",
        new TimeOnly(9, 0),
        new TimeOnly(18, 0),
        30,
        enabled);

    private sealed class FixedIntervalRandom(int value) : IReminderIntervalRandom
    {
        public int NextInclusive(int minimum, int maximum) => value;
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class RecordingPresenter(
        string channel,
        ReminderRepositoryStub repository,
        bool shouldThrow = false) : IReminderPresenter
    {
        public string Channel { get; } = channel;

        public List<ReminderDue> DueItems { get; } = [];

        public bool RepositoryContainedEventWhenShown { get; private set; }

        public int Attempts { get; private set; }

        public Task ShowAsync(ReminderDue due, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Attempts++;
            RepositoryContainedEventWhenShown = repository.Events.Any(item => item.Id == due.EventId);
            DueItems.Add(due);
            if (shouldThrow)
            {
                throw new InvalidOperationException($"{Channel} unavailable");
            }

            return Task.CompletedTask;
        }
    }
}

internal sealed class ReminderRepositoryStub(IReadOnlyList<ReminderRule> rules, bool supportsRuntimeState = false) : IEventRepository
{
    public IReadOnlyList<ReminderRule> Rules { get; } = rules;
    public List<ReminderEvent> Events { get; } = [];
    public TaskCompletionSource RulesRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Exception? UpdateException { get; set; }
    private readonly bool supportsRuntimeState = supportsRuntimeState;
    private readonly Dictionary<Guid, ReminderRuntimeState> runtimeStates = [];

    public Task AppendReminderAsync(ReminderEvent item, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Events.Add(item);
        return Task.CompletedTask;
    }

    public Task<ReminderEvent?> ReadReminderEventAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Events.SingleOrDefault(item => item.Id == id));

    public Task<bool> TryUpdateReminderAsync(
        ReminderEvent item,
        ReminderOutcome expectedOutcome,
        int expectedRetryIndex,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (UpdateException is not null)
        {
            var exception = UpdateException;
            UpdateException = null;
            return Task.FromException<bool>(exception);
        }

        var index = Events.FindIndex(candidate =>
            candidate.Id == item.Id &&
            candidate.Outcome == expectedOutcome &&
            candidate.RetryIndex == expectedRetryIndex);
        if (index < 0)
        {
            return Task.FromResult(false);
        }

        Events[index] = item;
        return Task.FromResult(true);
    }

    public Task<bool> UpdateReminderChannelAsync(
        Guid id,
        string expectedChannel,
        string channel,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var index = Events.FindIndex(candidate =>
            candidate.Id == id &&
            candidate.Channel == expectedChannel);
        if (index < 0)
        {
            return Task.FromResult(false);
        }

        Events[index] = Events[index] with { Channel = channel };
        return Task.FromResult(true);
    }

    public Task<ReminderRuntimeState?> ReadReminderRuntimeStateAsync(
        Guid ruleId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!supportsRuntimeState) throw new NotSupportedException();
        return Task.FromResult(runtimeStates.TryGetValue(ruleId, out var state) ? state : null);
    }

    public Task UpsertReminderRuntimeStateAsync(
        ReminderRuntimeState state,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!supportsRuntimeState) throw new NotSupportedException();
        runtimeStates[state.RuleId] = state;
        return Task.CompletedTask;
    }

    public Task AppendWorkSessionAsync(WorkSession session, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<IReadOnlyList<ReminderEvent>> ReadReminderEventsAsync(DateOnly day, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ReminderEvent>>(Events);
    public Task<IReadOnlyList<ReminderRule>> ReadReminderRulesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RulesRead.TrySetResult();
        return Task.FromResult(Rules);
    }

    public Task UpsertReminderRuleAsync(ReminderRule rule, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<IReadOnlyList<WorkSession>> ReadWorkSessionsAsync(DateOnly day, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<WorkSession>>([]);
    public Task<IReadOnlyList<TrackedApplication>> ReadTrackedApplicationsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TrackedApplication>>([]);
    public Task UpsertTrackedApplicationAsync(TrackedApplication application, CancellationToken cancellationToken) => Task.CompletedTask;
}
