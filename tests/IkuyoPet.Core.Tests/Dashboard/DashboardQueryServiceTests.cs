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

    [Fact]
    public async Task CountsCompletedHydrationAndActivityKinds()
    {
        var rules = new[]
        {
            new ReminderRule(Guid.NewGuid(), "water", "water", new(9, 0), new(18, 0), 45, true),
            new ReminderRule(Guid.NewGuid(), "hydration", "hydration", new(9, 0), new(18, 0), 45, true),
            new ReminderRule(Guid.NewGuid(), "activity", "activity", new(9, 0), new(18, 0), 45, true),
            new ReminderRule(Guid.NewGuid(), "move", "move", new(9, 0), new(18, 0), 45, true),
        };
        var at = LocalAt(2026, 9, 7, 10, 0);
        var events = rules.Select(rule => CreateEvent(rule.Id, at, ReminderOutcome.Completed, "pet"))
            .Append(CreateEvent(rules[0].Id, at, ReminderOutcome.Skipped, "pet"))
            .ToArray();
        var service = new DashboardQueryService(new StubEventRepository(rules, events));

        var snapshot = await service.GetAsync(new DateOnly(2026, 9, 7), TestContext.Current.CancellationToken);

        Assert.Equal(2, snapshot.HydrationCount);
        Assert.Equal(2, snapshot.ActivityCount);
        Assert.Equal(snapshot.WorkTime, snapshot.WorkDuration);
        Assert.Equal(4, snapshot.CompletedCount);
    }

    [Fact]
    public async Task SplitsCrossMidnightWorkAcrossLocalDays()
    {
        var session = new WorkSession(
            Guid.NewGuid(),
            "pycharm64",
            "PyCharm",
            LocalAt(2026, 9, 6, 23, 59),
            LocalAt(2026, 9, 7, 0, 1),
            120,
            "stopped");
        var service = new DashboardQueryService(
            new StubEventRepository(workSessions: [session]));

        var firstDay = await service.GetAsync(
            new DateOnly(2026, 9, 6),
            TestContext.Current.CancellationToken);
        var secondDay = await service.GetAsync(
            new DateOnly(2026, 9, 7),
            TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.FromSeconds(60), firstDay.WorkTime);
        Assert.Equal(TimeSpan.FromSeconds(60), secondDay.WorkTime);
    }

    [Fact]
    public async Task IgnoresNegativeWorkAndClampsOverflowToTimeSpanMaximum()
    {
        var start = LocalAt(2026, 9, 7, 10, 0);
        var sessions = Enumerable.Range(0, 430)
            .Select(_ => new WorkSession(
                Guid.NewGuid(),
                "pycharm64",
                "PyCharm",
                start,
                start.AddSeconds(1),
                int.MaxValue,
                "stopped"))
            .Prepend(new WorkSession(
                Guid.NewGuid(),
                "pycharm64",
                "PyCharm",
                start,
                start.AddSeconds(1),
                -30,
                "stopped"))
            .ToArray();
        var service = new DashboardQueryService(
            new StubEventRepository(workSessions: sessions));

        var snapshot = await service.GetAsync(
            new DateOnly(2026, 9, 7),
            TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.MaxValue, snapshot.WorkTime);
    }

    [Fact]
    public async Task OrdersEventsAtSameTimeById()
    {
        var scheduledAt = LocalAt(2026, 9, 7, 10, 0);
        var first = CreateEvent(
            Guid.NewGuid(),
            scheduledAt,
            ReminderOutcome.Completed,
            "first",
            eventId: Guid.ParseExact("00000000000000000000000000000001", "N"));
        var second = CreateEvent(
            Guid.NewGuid(),
            scheduledAt,
            ReminderOutcome.Completed,
            "second",
            eventId: Guid.ParseExact("00000000000000000000000000000002", "N"));
        var service = new DashboardQueryService(
            new StubEventRepository(reminderEvents: [second, first]));

        var snapshot = await service.GetAsync(
            new DateOnly(2026, 9, 7),
            TestContext.Current.CancellationToken);

        Assert.Equal(["first", "second"], snapshot.Timeline.Select(item => item.Channel));
    }

    [Theory]
    [InlineData(2026, 3, 8, 23)]
    [InlineData(2026, 11, 1, 25)]
    public async Task UsesInjectedTimeZoneForDstDayLength(
        int year,
        int month,
        int dayOfMonth,
        int expectedHours)
    {
        var zone = CreateEasternTimeZone();
        var day = new DateOnly(year, month, dayOfMonth);
        var start = AtStartOfDay(day, zone);
        var end = AtStartOfDay(day.AddDays(1), zone);
        var session = new WorkSession(
            Guid.NewGuid(),
            "pycharm64",
            "PyCharm",
            start,
            end,
            expectedHours * 60 * 60,
            "stopped");
        var service = new DashboardQueryService(
            new StubEventRepository(workSessions: [session]),
            zone);

        var snapshot = await service.GetAsync(day, TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.FromHours(expectedHours), snapshot.WorkTime);
    }

    private static ReminderEvent CreateEvent(
        Guid ruleId,
        DateTimeOffset scheduledAt,
        ReminderOutcome outcome,
        string channel,
        int retryIndex = 0,
        Guid? eventId = null) => new(
            eventId ?? Guid.NewGuid(),
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
        LocalAt(2026, 9, 7, 10, 0),
        LocalAt(2026, 9, 7, 10, 0).AddSeconds(seconds),
        seconds,
        "foreground-changed");

    private static DateTimeOffset LocalAt(int year, int month, int day, int hour, int minute)
    {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private static DateTimeOffset AtStartOfDay(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private static TimeZoneInfo CreateEasternTimeZone()
    {
        var daylightStart = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0),
            3,
            2,
            DayOfWeek.Sunday);
        var daylightEnd = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0),
            11,
            1,
            DayOfWeek.Sunday);
        var adjustment = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2020, 1, 1),
            new DateTime(2030, 12, 31),
            TimeSpan.FromHours(1),
            daylightStart,
            daylightEnd);
        return TimeZoneInfo.CreateCustomTimeZone(
            "IkuyoPet-Test-Eastern",
            TimeSpan.FromHours(-5),
            "IkuyoPet Test Eastern",
            "IkuyoPet Test Eastern Standard",
            "IkuyoPet Test Eastern Daylight",
            [adjustment]);
    }

    private sealed class StubEventRepository(
        IReadOnlyList<ReminderRule>? rules = null,
        IReadOnlyList<ReminderEvent>? reminderEvents = null,
        IReadOnlyList<WorkSession>? workSessions = null) : IEventRepository
    {
        public DateOnly? ReminderDayRead { get; private set; }

        public DateOnly? WorkSessionDayRead { get; private set; }

        public Task AppendReminderAsync(ReminderEvent item, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<ReminderEvent?> ReadReminderEventAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult((reminderEvents ?? []).SingleOrDefault(item => item.Id == id));

        public Task<bool> TryUpdateReminderAsync(
            ReminderEvent item,
            ReminderOutcome expectedOutcome,
            int expectedRetryIndex,
            CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<bool> UpdateReminderChannelAsync(
            Guid id,
            string expectedChannel,
            string channel,
            CancellationToken cancellationToken) => Task.FromResult(false);

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
