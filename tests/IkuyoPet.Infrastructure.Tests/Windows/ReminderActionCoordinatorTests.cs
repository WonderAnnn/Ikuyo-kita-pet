using System.IO;
using IkuyoPet.Core.Reminders;
using IkuyoPet.Infrastructure.Windows;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.Windows;

public sealed class ReminderActionCoordinatorTests
{
    [Theory]
    [InlineData(ReminderAction.Complete, ReminderOutcome.Completed, false)]
    [InlineData(ReminderAction.Snooze, ReminderOutcome.Snoozed, true)]
    [InlineData(ReminderAction.Skip, ReminderOutcome.Skipped, false)]
    public async Task PersistsActionOutcomeAndRetryDecision(
        ReminderAction action,
        ReminderOutcome expectedOutcome,
        bool expectedRetry)
    {
        var (coordinator, repository, item, now) = CreateCoordinator(retryIndex: 0);

        var result = await coordinator.HandleAsync(item.Id, action, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result.Value.Applied);
        Assert.Equal(expectedOutcome, result.Value.Transition.Outcome);
        Assert.Equal(expectedRetry, result.Value.Transition.ShouldRetry);
        var stored = Assert.Single(repository.Events);
        Assert.Equal(item.Id, stored.Id);
        Assert.Equal(item.Channel, stored.Channel);
        Assert.Equal(expectedOutcome, stored.Outcome);
        Assert.Equal(now, stored.ActionAt);
        Assert.Equal(action == ReminderAction.Snooze ? 1 : 0, stored.RetryIndex);
    }

    [Fact]
    public async Task WaterActionStartsTheNextWallClockCycle()
    {
        var now = new DateTimeOffset(2026, 9, 11, 10, 0, 0, TimeSpan.FromHours(8));
        var rule = ReminderRule.CreateDefaultHydration(Guid.NewGuid()) with
        {
            IntervalMinMinutes = 15,
            IntervalMaxMinutes = 15,
        };
        var repository = new ReminderRepositoryStub([rule], supportsRuntimeState: true);
        var runtime = new ReminderRuntimeState(
            rule.Id,
            Guid.NewGuid(),
            900,
            900,
            ReminderRuntimeStatus.Due,
            1,
            null,
            now);
        await repository.UpsertReminderRuntimeStateAsync(runtime, TestContext.Current.CancellationToken);
        var item = new ReminderEvent(
            Guid.NewGuid(),
            rule.Id,
            now,
            now,
            "pet",
            ReminderOutcome.None,
            null,
            0,
            null,
            now);
        repository.Events.Add(item);
        var coordinator = new ReminderActionCoordinator(
            repository,
            new ReminderStateMachine(maxAttempts: 3),
            new FixedTimeProvider(now),
            new FixedIntervalRandom(15));

        var result = await coordinator.HandleAsync(
            item.Id,
            ReminderAction.Complete,
            TestContext.Current.CancellationToken);

        Assert.True(result!.Value.Applied);
        var next = await repository.ReadReminderRuntimeStateAsync(
            rule.Id,
            TestContext.Current.CancellationToken);
        Assert.NotNull(next);
        Assert.Equal(ReminderRuntimeStatus.Accumulating, next.Status);
        Assert.Equal(900, next.TargetActiveSeconds);
        Assert.Equal(now, next.UpdatedAt);
    }

    [Fact]
    public async Task ManualWaterActionLogsCompletionAndStartsANewWallClockCycle()
    {
        var now = new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.FromHours(8));
        var rule = ReminderRule.CreateDefaultHydration(Guid.NewGuid()) with
        {
            IntervalMinMinutes = 15,
            IntervalMaxMinutes = 20,
        };
        var repository = new ReminderRepositoryStub([rule], supportsRuntimeState: true);
        var previous = new ReminderRuntimeState(
            rule.Id,
            Guid.NewGuid(),
            900,
            900,
            ReminderRuntimeStatus.Due,
            1,
            null,
            now.AddMinutes(-1));
        await repository.UpsertReminderRuntimeStateAsync(previous, TestContext.Current.CancellationToken);
        var coordinator = new ReminderActionCoordinator(
            repository,
            new ReminderStateMachine(maxAttempts: 3),
            new FixedTimeProvider(now),
            new FixedIntervalRandom(18));

        var result = await coordinator.HandleManualAsync(
            "water",
            TestContext.Current.CancellationToken);

        Assert.True(result.Applied);
        Assert.Equal(ReminderOutcome.Completed, result.Transition.Outcome);
        var logged = Assert.Single(repository.Events);
        Assert.Equal(rule.Id, logged.RuleId);
        Assert.Equal("pet", logged.Channel);
        Assert.Equal(ReminderOutcome.Completed, logged.Outcome);
        Assert.Equal(now, logged.ActionAt);
        var next = await repository.ReadReminderRuntimeStateAsync(
            rule.Id,
            TestContext.Current.CancellationToken);
        Assert.NotNull(next);
        Assert.NotEqual(previous.CycleId, next.CycleId);
        Assert.Equal(ReminderRuntimeStatus.Accumulating, next.Status);
        Assert.Equal(18 * 60, next.TargetActiveSeconds);
        Assert.Equal(now, next.UpdatedAt);
    }

    [Fact]
    public async Task ManualActivityActionUsesTheActivityCompletionPath()
    {
        var now = new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.FromHours(8));
        var rule = ReminderRule.CreateDefaultActiveWork(Guid.NewGuid()) with
        {
            IntervalMinMinutes = 40,
            IntervalMaxMinutes = 50,
            ActivityDurationMinutes = 5,
        };
        var repository = new ReminderRepositoryStub([rule], supportsRuntimeState: true);
        var coordinator = new ReminderActionCoordinator(
            repository,
            new ReminderStateMachine(maxAttempts: 3),
            new FixedTimeProvider(now),
            new FixedIntervalRandom(42));

        var result = await coordinator.HandleManualAsync(
            "activity",
            TestContext.Current.CancellationToken);

        Assert.True(result.Applied);
        var logged = Assert.Single(repository.Events);
        Assert.Equal(rule.Id, logged.RuleId);
        Assert.Equal(rule.ActivityDurationMinutes, logged.ActivityDurationMinutes);
        var next = await repository.ReadReminderRuntimeStateAsync(
            rule.Id,
            TestContext.Current.CancellationToken);
        Assert.NotNull(next);
        Assert.Equal(42 * 60, next.TargetActiveSeconds);
    }

    [Fact]
    public async Task ManualActionRejectsUnknownReminderKind()
    {
        var repository = new ReminderRepositoryStub([]);
        var coordinator = new ReminderActionCoordinator(
            repository,
            new ReminderStateMachine(maxAttempts: 3),
            new FixedTimeProvider(DateTimeOffset.UtcNow));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            coordinator.HandleManualAsync(
                "unknown",
                TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task RepeatedFinalActionForSameEventIsAppliedOnlyOnce()
    {
        var (coordinator, repository, item, _) = CreateCoordinator(retryIndex: 0);

        var first = await coordinator.HandleAsync(item.Id, ReminderAction.Complete, TestContext.Current.CancellationToken);
        var duplicate = await coordinator.HandleAsync(item.Id, ReminderAction.Skip, TestContext.Current.CancellationToken);

        Assert.True(first!.Value.Applied);
        Assert.False(duplicate!.Value.Applied);
        Assert.Equal(ReminderOutcome.Completed, Assert.Single(repository.Events).Outcome);
    }

    [Fact]
    public async Task NoResponseAtRetryLimitBecomesUnansweredAndStops()
    {
        var (coordinator, repository, item, _) = CreateCoordinator(retryIndex: 2);

        var result = await coordinator.HandleAsync(item.Id, ReminderAction.NoResponse, TestContext.Current.CancellationToken);

        Assert.True(result!.Value.Applied);
        Assert.Equal(ReminderOutcome.Unanswered, result.Value.Transition.Outcome);
        Assert.False(result.Value.Transition.ShouldRetry);
        Assert.Equal(3, Assert.Single(repository.Events).RetryIndex);
    }

    [Fact]
    public async Task NoResponseBelowRetryLimitKeepsPendingAndRequestsRetry()
    {
        var (coordinator, repository, item, _) = CreateCoordinator(retryIndex: 0);

        var result = await coordinator.HandleAsync(item.Id, ReminderAction.NoResponse, TestContext.Current.CancellationToken);

        Assert.True(result!.Value.Applied);
        Assert.Equal(ReminderOutcome.None, result.Value.Transition.Outcome);
        Assert.True(result.Value.Transition.ShouldRetry);
        Assert.Equal(1, Assert.Single(repository.Events).RetryIndex);
    }

    [Fact]
    public async Task CancellationIsNotSwallowed()
    {
        var (coordinator, _, item, _) = CreateCoordinator(retryIndex: 0);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            coordinator.HandleAsync(item.Id, ReminderAction.Complete, cancellation.Token));
    }

    [Fact]
    public async Task FailedWriteCanBeRetriedAndIsNotMarkedProcessed()
    {
        var (coordinator, repository, item, _) = CreateCoordinator(retryIndex: 0);
        repository.UpdateException = new IOException("disk unavailable");

        await Assert.ThrowsAsync<IOException>(() =>
            coordinator.HandleAsync(item.Id, ReminderAction.Complete, TestContext.Current.CancellationToken));
        var retried = await coordinator.HandleAsync(item.Id, ReminderAction.Complete, TestContext.Current.CancellationToken);

        Assert.True(retried!.Value.Applied);
        Assert.Equal(ReminderOutcome.Completed, Assert.Single(repository.Events).Outcome);
    }

    private static (ReminderActionCoordinator Coordinator, ReminderRepositoryStub Repository, ReminderEvent Item, DateTimeOffset Now)
        CreateCoordinator(int retryIndex)
    {
        var displayedAt = new DateTimeOffset(2026, 9, 7, 10, 0, 0, TimeSpan.FromHours(8));
        var now = displayedAt.AddMinutes(1);
        var item = new ReminderEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            displayedAt,
            displayedAt,
            "pet",
            ReminderOutcome.None,
            null,
            retryIndex,
            null,
            displayedAt);
        var repository = new ReminderRepositoryStub([]);
        repository.Events.Add(item);
        var coordinator = new ReminderActionCoordinator(
            repository,
            new ReminderStateMachine(maxAttempts: 3),
            new FixedTimeProvider(now));
        return (coordinator, repository, item, now);
    }

    private sealed class FixedIntervalRandom(int value) : IReminderIntervalRandom
    {
        public int NextInclusive(int minimum, int maximum) => value;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
