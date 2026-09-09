using System.IO;
using IkuyoPet.Core.Presentation;
using IkuyoPet.Core.Reminders;
using IkuyoPet.Core.WorkTracking;
using IkuyoPet.Infrastructure.Storage;
using IkuyoPet.Infrastructure.Windows;
using Microsoft.Data.Sqlite;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.EndToEnd;

public sealed class FirstClosedLoopTests
{
    [Fact]
    public async Task EmptyDatabaseAccumulatesActiveWorkAndPresentsCycleOnlyOnceAcrossRestart()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"ikuyo-closed-loop-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteEventRepository($"Data Source={databasePath}");
            var now = new DateTimeOffset(2026, 9, 9, 2, 0, 0, TimeSpan.Zero);
            var clock = new MutableTimeProvider(now);
            var presenter = new RecordingPresenter();

            var loop = CreateLoop(repository, presenter, clock);
            await loop.ProcessOnceAsync(TestContext.Current.CancellationToken);

            var rule = Assert.Single(await repository.ReadReminderRulesAsync(TestContext.Current.CancellationToken));
            Assert.Equal("activity", rule.Kind);
            Assert.Equal(40, rule.IntervalMinMinutes);
            Assert.Equal(50, rule.IntervalMaxMinutes);
            Assert.Equal(5, rule.ActivityDurationMinutes);

            await loop.ConsumeActiveWorkAsync(
                new ActiveWorkDelta(2_400, "pycharm64", now),
                TestContext.Current.CancellationToken);
            await loop.ProcessOnceAsync(TestContext.Current.CancellationToken);
            await loop.ProcessOnceAsync(TestContext.Current.CancellationToken);

            var restarted = CreateLoop(repository, presenter, clock);
            await restarted.ProcessOnceAsync(TestContext.Current.CancellationToken);

            Assert.Single(presenter.Items);
            Assert.Single(await repository.ReadReminderEventsAsync(
                DateOnly.FromDateTime(now.Date),
                TestContext.Current.CancellationToken));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task CompleteActionIsIdempotentAndStartsPersistedNextCycle()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"ikuyo-action-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteEventRepository($"Data Source={databasePath}");
            var now = new DateTimeOffset(2026, 9, 9, 3, 0, 0, TimeSpan.Zero);
            var rule = ReminderRule.CreateDefaultActiveWork(Guid.NewGuid());
            await repository.UpsertReminderRuleAsync(rule, TestContext.Current.CancellationToken);
            var state = new ReminderRuntimeState(
                rule.Id, Guid.NewGuid(), 2_400, 2_400,
                ReminderRuntimeStatus.Due, 1, null, now);
            await repository.UpsertReminderRuntimeStateAsync(state, TestContext.Current.CancellationToken);
            var item = new ReminderEvent(
                Guid.NewGuid(), rule.Id, now, now, "pet", ReminderOutcome.None,
                null, 0, null, now);
            await repository.AppendReminderAsync(item, TestContext.Current.CancellationToken);
            var coordinator = new ReminderActionCoordinator(
                repository,
                new ReminderStateMachine(3),
                new MutableTimeProvider(now.AddMinutes(1)),
                new FixedRandom(50));

            var first = await coordinator.HandleAsync(
                item.Id, ReminderAction.Complete, TestContext.Current.CancellationToken);
            var duplicate = await coordinator.HandleAsync(
                item.Id, ReminderAction.Complete, TestContext.Current.CancellationToken);

            Assert.True(first!.Value.Applied);
            Assert.False(duplicate!.Value.Applied);
            var next = await repository.ReadReminderRuntimeStateAsync(
                rule.Id, TestContext.Current.CancellationToken);
            Assert.NotNull(next);
            Assert.Equal(ReminderRuntimeStatus.Accumulating, next.Status);
            Assert.Equal(3_000, next.TargetActiveSeconds);
            Assert.NotEqual(state.CycleId, next.CycleId);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }
    [Fact]
    public async Task SnoozePersistsFiveMinuteRetryAndPresentsOneNewAttempt()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"ikuyo-snooze-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteEventRepository($"Data Source={databasePath}");
            var now = new DateTimeOffset(2026, 9, 9, 4, 0, 0, TimeSpan.Zero);
            var clock = new MutableTimeProvider(now);
            var rule = ReminderRule.CreateDefaultActiveWork(Guid.NewGuid());
            await repository.UpsertReminderRuleAsync(rule, TestContext.Current.CancellationToken);
            await repository.UpsertReminderRuntimeStateAsync(
                new ReminderRuntimeState(rule.Id, Guid.NewGuid(), 2_400, 2_400,
                    ReminderRuntimeStatus.Due, 1, null, now),
                TestContext.Current.CancellationToken);
            var original = new ReminderEvent(
                Guid.NewGuid(), rule.Id, now, now, "pet", ReminderOutcome.None,
                null, 0, null, now);
            await repository.AppendReminderAsync(original, TestContext.Current.CancellationToken);
            var coordinator = new ReminderActionCoordinator(
                repository, new ReminderStateMachine(3), clock, new FixedRandom(40));

            await coordinator.HandleAsync(
                original.Id, ReminderAction.Snooze, TestContext.Current.CancellationToken);
            var waiting = await repository.ReadReminderRuntimeStateAsync(
                rule.Id, TestContext.Current.CancellationToken);
            Assert.Equal(now.AddMinutes(5), waiting!.RetryDueAt);

            var presenter = new RecordingPresenter();
            var loop = CreateLoop(repository, presenter, clock);
            clock.UtcNow = now.AddMinutes(4).AddSeconds(59);
            await loop.ProcessOnceAsync(TestContext.Current.CancellationToken);
            Assert.Empty(presenter.Items);

            clock.UtcNow = now.AddMinutes(5);
            await loop.ProcessOnceAsync(TestContext.Current.CancellationToken);
            await loop.ProcessOnceAsync(TestContext.Current.CancellationToken);
            Assert.Single(presenter.Items);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }
    [Fact]
    public async Task SuppressedDueCycleWaitsForTwoStableMinutesAndBackfillsOnlyOnce()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"ikuyo-suppressed-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteEventRepository($"Data Source={databasePath}");
            var now = new DateTimeOffset(2026, 9, 9, 5, 0, 0, TimeSpan.Zero);
            var clock = new MutableTimeProvider(now);
            var rule = ReminderRule.CreateDefaultActiveWork(Guid.NewGuid());
            await repository.UpsertReminderRuleAsync(rule, TestContext.Current.CancellationToken);
            await repository.UpsertReminderRuntimeStateAsync(
                new ReminderRuntimeState(rule.Id, Guid.NewGuid(), 2_400, 2_400,
                    ReminderRuntimeStatus.Due, 1, null, now),
                TestContext.Current.CancellationToken);
            var suppressed = true;
            var presenter = new RecordingPresenter();
            var loop = new ReminderLoop(
                repository,
                new ReminderPresentationRouter(presenter, presenter),
                () => true,
                clock,
                TimeZoneInfo.Utc,
                TimeSpan.Zero,
                intervalRandom: new FixedRandom(40),
                isSuppressed: () => suppressed,
                suppressionRecoveryDelay: TimeSpan.FromMinutes(2));

            await loop.ProcessOnceAsync(TestContext.Current.CancellationToken);
            suppressed = false;
            await loop.ProcessOnceAsync(TestContext.Current.CancellationToken);
            clock.UtcNow = now.AddMinutes(1).AddSeconds(59);
            await loop.ProcessOnceAsync(TestContext.Current.CancellationToken);
            Assert.Empty(presenter.Items);

            clock.UtcNow = now.AddMinutes(2);
            await loop.ProcessOnceAsync(TestContext.Current.CancellationToken);
            await loop.ProcessOnceAsync(TestContext.Current.CancellationToken);
            Assert.Single(presenter.Items);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }
    [Fact]
    public async Task ThirdNoResponseLogsUnansweredAndStartsNextCycle()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"ikuyo-unanswered-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteEventRepository($"Data Source={databasePath}");
            var now = new DateTimeOffset(2026, 9, 9, 6, 0, 0, TimeSpan.Zero);
            var rule = ReminderRule.CreateDefaultActiveWork(Guid.NewGuid());
            await repository.UpsertReminderRuleAsync(rule, TestContext.Current.CancellationToken);
            var cycleId = Guid.NewGuid();
            await repository.UpsertReminderRuntimeStateAsync(
                new ReminderRuntimeState(rule.Id, cycleId, 2_400, 2_400,
                    ReminderRuntimeStatus.Due, 3, null, now),
                TestContext.Current.CancellationToken);
            var item = new ReminderEvent(
                Guid.NewGuid(), rule.Id, now, now, "pet", ReminderOutcome.None,
                null, 2, null, now);
            await repository.AppendReminderAsync(item, TestContext.Current.CancellationToken);
            var coordinator = new ReminderActionCoordinator(
                repository, new ReminderStateMachine(3),
                new MutableTimeProvider(now), new FixedRandom(50));

            await coordinator.HandleAsync(
                item.Id, ReminderAction.NoResponse, TestContext.Current.CancellationToken);

            Assert.Equal(ReminderOutcome.Unanswered,
                (await repository.ReadReminderEventAsync(item.Id, TestContext.Current.CancellationToken))!.Outcome);
            var next = await repository.ReadReminderRuntimeStateAsync(
                rule.Id, TestContext.Current.CancellationToken);
            Assert.Equal(ReminderRuntimeStatus.Accumulating, next!.Status);
            Assert.NotEqual(cycleId, next.CycleId);
            Assert.Equal(3_000, next.TargetActiveSeconds);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }
    [Fact]
    public async Task ExpiredPendingEventAutomaticallyRetriesOnceAndPersistsSuppressionReason()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"ikuyo-timeout-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteEventRepository($"Data Source={databasePath}");
            var now = new DateTimeOffset(2026, 9, 9, 7, 0, 0, TimeSpan.Zero);
            var clock = new MutableTimeProvider(now);
            var rule = ReminderRule.CreateDefaultActiveWork(Guid.NewGuid());
            await repository.UpsertReminderRuleAsync(rule, TestContext.Current.CancellationToken);
            await repository.UpsertReminderRuntimeStateAsync(
                new ReminderRuntimeState(rule.Id, Guid.NewGuid(), 2_400, 2_400,
                    ReminderRuntimeStatus.Due, 1, null, now),
                TestContext.Current.CancellationToken);
            var item = new ReminderEvent(
                Guid.NewGuid(), rule.Id, now.AddMinutes(-6), now.AddMinutes(-6), "pet",
                ReminderOutcome.None, null, 0, null, now.AddMinutes(-6));
            await repository.AppendReminderAsync(item, TestContext.Current.CancellationToken);
            var coordinator = new ReminderActionCoordinator(
                repository, new ReminderStateMachine(3), clock, new FixedRandom(40));
            var presenter = new RecordingPresenter();
            var loop = new ReminderLoop(
                repository,
                new ReminderPresentationRouter(presenter, presenter),
                () => true,
                clock,
                TimeZoneInfo.Utc,
                TimeSpan.Zero,
                intervalRandom: new FixedRandom(40),
                actionCoordinator: coordinator);

            await loop.ProcessOnceAsync(TestContext.Current.CancellationToken);
            var retried = await repository.ReadReminderEventAsync(
                item.Id, TestContext.Current.CancellationToken);
            Assert.NotNull(retried);
            Assert.Equal(ReminderOutcome.None, retried.Outcome);
            Assert.Equal(1, retried.RetryIndex);
            Assert.Equal("no-response", retried.SuppressedReason);

            await loop.ProcessOnceAsync(TestContext.Current.CancellationToken);
            var duplicate = await repository.ReadReminderEventAsync(
                item.Id, TestContext.Current.CancellationToken);
            Assert.NotNull(duplicate);
            Assert.Equal(1, duplicate.RetryIndex);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }
    private static ReminderLoop CreateLoop(
        SqliteEventRepository repository,
        IReminderPresenter presenter,
        TimeProvider clock) => new(
            repository,
            new ReminderPresentationRouter(presenter, presenter),
            () => true,
            clock,
            TimeZoneInfo.Utc,
            TimeSpan.Zero,
            intervalRandom: new FixedRandom(40));

    private sealed class FixedRandom(int value) : IReminderIntervalRandom
    {
        public int NextInclusive(int minimum, int maximum)
        {
            Assert.InRange(value, minimum, maximum);
            return value;
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class RecordingPresenter : IReminderPresenter
    {
        public string Channel => "pet";
        public List<ReminderDue> Items { get; } = [];

        public Task ShowAsync(ReminderDue due, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Items.Add(due);
            return Task.CompletedTask;
        }
    }
}
