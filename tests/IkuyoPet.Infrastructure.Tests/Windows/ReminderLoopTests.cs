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

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class RecordingPresenter(
        string channel,
        ReminderRepositoryStub repository) : IReminderPresenter
    {
        public string Channel { get; } = channel;

        public List<ReminderDue> DueItems { get; } = [];

        public bool RepositoryContainedEventWhenShown { get; private set; }

        public Task ShowAsync(ReminderDue due, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RepositoryContainedEventWhenShown = repository.Events.Any(item => item.Id == due.EventId);
            DueItems.Add(due);
            return Task.CompletedTask;
        }
    }
}

internal sealed class ReminderRepositoryStub(IReadOnlyList<ReminderRule> rules) : IEventRepository
{
    public IReadOnlyList<ReminderRule> Rules { get; } = rules;
    public List<ReminderEvent> Events { get; } = [];
    public TaskCompletionSource RulesRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Exception? UpdateException { get; set; }

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
