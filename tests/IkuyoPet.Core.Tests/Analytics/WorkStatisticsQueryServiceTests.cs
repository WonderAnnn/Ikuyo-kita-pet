using IkuyoPet.Core.Analytics;
using IkuyoPet.Core.Reminders;
using IkuyoPet.Core.Storage;
using IkuyoPet.Core.WorkTracking;
using Xunit;

namespace IkuyoPet.Core.Tests.Analytics;

public sealed class WorkStatisticsQueryServiceTests
{
    [Fact]
    public async Task AggregatesWeekByApplicationAndKeepsOnlyTopFive()
    {
        var sessions = new[]
        {
            CreateSession("word", "Word", 300, new DateTime(2026, 9, 7, 9, 0, 0)),
            CreateSession("pycharm64", "PyCharm", 240, new DateTime(2026, 9, 8, 9, 0, 0)),
            CreateSession("code", "VS Code", 180, new DateTime(2026, 9, 9, 9, 0, 0)),
            CreateSession("zcode", "ZCode", 120, new DateTime(2026, 9, 10, 9, 0, 0)),
            CreateSession("edge", "Edge", 60, new DateTime(2026, 9, 11, 9, 0, 0)),
            CreateSession("qq", "QQ", 30, new DateTime(2026, 9, 12, 9, 0, 0)),
        };
        var service = new WorkStatisticsQueryService(new StubRepository(sessions));

        var result = await service.GetAsync(
            new DateOnly(2026, 9, 10),
            WorkStatisticsPeriod.Week,
            TestContext.Current.CancellationToken);

        Assert.Equal(new DateOnly(2026, 9, 7), result.StartDate);
        Assert.Equal(new DateOnly(2026, 9, 14), result.EndDateExclusive);
        Assert.Equal(TimeSpan.FromSeconds(930), result.TotalWorkTime);
        Assert.Equal(5, result.TopApplications.Count);
        Assert.Equal(
            ["Word", "PyCharm", "VS Code", "ZCode", "Edge"],
            result.TopApplications.Select(item => item.DisplayName));
    }

    [Fact]
    public async Task SplitsCrossMidnightSessionBySelectedLocalDay()
    {
        var session = new WorkSession(
            Guid.NewGuid(),
            "word",
            "Word",
            LocalAt(2026, 9, 6, 23, 30),
            LocalAt(2026, 9, 7, 0, 30),
            3_600,
            "stopped");
        var service = new WorkStatisticsQueryService(new StubRepository([session]));

        var result = await service.GetAsync(
            new DateOnly(2026, 9, 7),
            WorkStatisticsPeriod.Day,
            TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.FromMinutes(30), result.TotalWorkTime);
        Assert.Equal(1_800, Assert.Single(result.TopApplications).ActiveSeconds);
    }

    private static WorkSession CreateSession(
        string processName,
        string displayName,
        int activeSeconds,
        DateTime startedAt) => new(
        Guid.NewGuid(),
        processName,
        displayName,
        LocalAt(startedAt.Year, startedAt.Month, startedAt.Day, startedAt.Hour, startedAt.Minute),
        LocalAt(startedAt.Year, startedAt.Month, startedAt.Day, startedAt.Hour, startedAt.Minute).AddSeconds(activeSeconds),
        activeSeconds,
        "stopped");

    private static DateTimeOffset LocalAt(int year, int month, int day, int hour, int minute)
    {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private sealed class StubRepository(IReadOnlyList<WorkSession> sessions) : IEventRepository
    {
        public Task AppendReminderAsync(ReminderEvent item, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<ReminderEvent?> ReadReminderEventAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<ReminderEvent?>(null);

        public Task<bool> TryUpdateReminderAsync(ReminderEvent item, ReminderOutcome expectedOutcome, int expectedRetryIndex, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> UpdateReminderChannelAsync(Guid id, string expectedChannel, string channel, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task AppendWorkSessionAsync(WorkSession session, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<ReminderEvent>> ReadReminderEventsAsync(DateOnly day, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReminderEvent>>([]);

        public Task<IReadOnlyList<ReminderRule>> ReadReminderRulesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReminderRule>>([]);

        public Task UpsertReminderRuleAsync(ReminderRule rule, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<WorkSession>> ReadWorkSessionsAsync(DateOnly day, CancellationToken cancellationToken) =>
            Task.FromResult(sessions);

        public Task<IReadOnlyList<TrackedApplication>> ReadTrackedApplicationsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TrackedApplication>>([]);

        public Task UpsertTrackedApplicationAsync(TrackedApplication application, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
