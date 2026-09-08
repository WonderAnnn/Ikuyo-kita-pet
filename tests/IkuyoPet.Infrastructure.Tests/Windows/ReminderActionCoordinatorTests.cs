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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
