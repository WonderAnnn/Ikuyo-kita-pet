using IkuyoPet.Core.Dashboard;
using IkuyoPet.Core.Reminders;
using IkuyoPet.Core.Storage;
using IkuyoPet.Core.WorkTracking;
using Xunit;

namespace IkuyoPet.Core.Tests.Dashboard;

public sealed class DashboardQueryServiceTests
{
    [Fact]
    public async Task BuildsOrderedDailySnapshotWithOutcomeCountsAndWorkTime()
    {
        var day = new DateOnly(2026, 9, 7);
        var rule = new ReminderRule(
            Guid.NewGuid(),
            "water",
            "喝点水吧",
            new TimeOnly(9, 0),
            new TimeOnly(18, 0),
            45,
            true);
        var later = CreateEvent(rule.Id, new DateTimeOffset(2026, 9, 7, 11, 0, 0, TimeSpan.FromHours(8)), ReminderOutcome.Snoozed, "pet", retryIndex: 1);
        var earlier = CreateEvent(rule.Id, new DateTimeOffset(2026, 9, 7, 9, 0, 0, TimeSpan.FromHours(8)), ReminderOutcome.Completed, "pet");
        var skipped = CreateEvent(rule.Id, new DateTimeOffset(2026, 9, 7, 10, 0, 0, TimeSpan.FromHours(8)), ReminderOutcome.Skipped, "notification");
        var unanswered = CreateEvent(rule.Id, new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.FromHours(8)), ReminderOutcome.Unanswered, "pet", retryIndex: 2);
        var repository = new StubEventRepository(
            rules: [rule],
            reminderEvents: [later, unanswered, skipped, earlier],
            workSessions:
            [
                CreateWorkSession(300),
                CreateWorkSession(420),
            ]);
        var service = new DashboardQueryService(repository);

        var snapshot = await service.GetAsync(day, TestContext.Current.CancellationToken);

        Assert.Equal(day, snapshot.Day);
        Assert.Equal([earlier.ScheduledAt, skipped.ScheduledAt, later.ScheduledAt, unanswered.ScheduledAt], snapshot.Timeline.Select(item => item.ScheduledAt));
        Assert.All(snapshot.Timeline, item => Assert.Equal("water", item.Kind));
        Assert.Equal(1, snapshot.CompletedCount);
        Assert.Equal(1, snapshot.SnoozedCount);
        Assert.Equal(1, snapshot.SkippedCount);
        Assert.Equal(1, snapshot.UnansweredCount);
        Assert.Equal(TimeSpan.FromSeconds(720), snapshot.WorkTime);
        Assert.Equal(day, repository.ReminderDayRead);
        Assert.Equal(day, repository.WorkSessionDayRead);
    }

    [Fact]
    public async Task UsesUnknownKindWhenEventHasNoMatchingRule()
    {
        var day = new DateOnly(2026, 9, 7);
        var item = CreateEvent(
            Guid.NewGuid(),
            new DateTimeOffset(2026, 9, 7, 9, 0, 0, TimeSpan.FromHours(8)),
            ReminderOutcome.Completed,
            "pet");
        var service = new DashboardQueryService(new StubEventRepository(reminderEvents: [item]));

        var snapshot = await service.GetAsync(day, TestContext.Current.CancellationToken);

        Assert.Equal("unknown", Assert.Single(snapshot.Timeline).Kind);
    }

    private static ReminderEvent CreateEvent(
        Guid ruleId,
        DateTimeOffset scheduledAt,
        ReminderOutcome outcome,
        string channel,
        int retryIndex = 0) => new(
            Guid.NewGuid(),
            ruleId,
            scheduledAt,
            scheduledAt,
            channel,
            outcome,
            scheduledAt.AddMinutes(1),
            retryIndex,
            null,
            scheduledAt);

    private static WorkSession CreateWorkSession(int seconds) => new(
        Guid.NewGuid(),
        "pycharm64",
        "PyCharm",
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow.AddSeconds(seconds),
        seconds,
        "foreground-changed");

    private sealed class StubEventRepository(
        IReadOnlyList<ReminderRule>? rules = null,
        IReadOnlyList<ReminderEvent>? reminderEvents = null,
        IReadOnlyList<WorkSession>? workSessions = null) : IEventRepository
    {
        public DateOnly? ReminderDayRead { get; private set; }

        public DateOnly? WorkSessionDayRead { get; private set; }

        public Task AppendReminderAsync(ReminderEvent item, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AppendWorkSessionAsync(WorkSession session, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<ReminderEvent>> ReadReminderEventsAsync(DateOnly day, CancellationToken cancellationToken)
        {
            ReminderDayRead = day;
            return Task.FromResult(reminderEvents ?? (IReadOnlyList<ReminderEvent>)[]);
        }

        public Task<IReadOnlyList<ReminderRule>> ReadReminderRulesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(rules ?? (IReadOnlyList<ReminderRule>)[]);

        public Task UpsertReminderRuleAsync(ReminderRule rule, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<WorkSession>> ReadWorkSessionsAsync(DateOnly day, CancellationToken cancellationToken)
        {
            WorkSessionDayRead = day;
            return Task.FromResult(workSessions ?? (IReadOnlyList<WorkSession>)[]);
        }

        public Task<IReadOnlyList<TrackedApplication>> ReadTrackedApplicationsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TrackedApplication>>([]);

        public Task UpsertTrackedApplicationAsync(TrackedApplication application, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
