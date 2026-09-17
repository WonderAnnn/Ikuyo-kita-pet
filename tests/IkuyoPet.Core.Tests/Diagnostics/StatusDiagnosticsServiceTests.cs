using IkuyoPet.Core.Diagnostics;
using IkuyoPet.Core.Reminders;
using IkuyoPet.Core.Storage;
using IkuyoPet.Core.WorkTracking;
using Xunit;

namespace IkuyoPet.Core.Tests.Diagnostics;

public sealed class StatusDiagnosticsServiceTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.Utc;

    [Fact]
    public async Task WhitelistedActiveForegroundReportsAccumulatingWork()
    {
        var rule = ReminderRule.CreateDefaultActiveWork(DefaultReminderRuleIds.Activity);
        var runtime = new ReminderRuntimeState(
            rule.Id,
            Guid.NewGuid(),
            targetActiveSeconds: 2_700,
            accumulatedActiveSeconds: 900,
            ReminderRuntimeStatus.Accumulating,
            attempt: 0,
            retryDueAt: null,
            updatedAt: DateTimeOffset.UtcNow);
        var service = CreateService(
            rule,
            runtime,
            sample: new ActivitySample("pycharm64", true, false, TimeSpan.FromSeconds(30), DateTimeOffset.UtcNow),
            applications: [new TrackedApplication("pycharm64", "PyCharm", true)]);

        var report = await service.GetReportAsync(TestContext.Current.CancellationToken);

        Assert.True(report.WorkTracking.CanAccumulate);
        Assert.Equal("pycharm64", report.WorkTracking.ForegroundProcessName);
        Assert.Contains(report.WorkTracking.Reasons, reason => reason.Contains("计时中") || reason.Contains("白名单"));
        var activity = Assert.Single(report.Rules);
        Assert.Equal("活动", activity.KindText);
        Assert.Contains("本轮已累计有效工作", activity.StateText);
        Assert.Contains("预计", activity.NextEstimateText);
    }

    [Fact]
    public async Task NonWhitelistedForegroundExplainsWhyWorkDoesNotAccumulate()
    {
        var rule = ReminderRule.CreateDefaultActiveWork(DefaultReminderRuleIds.Activity);
        var service = CreateService(
            rule,
            runtime: null,
            sample: new ActivitySample("chrome", false, false, TimeSpan.FromSeconds(10), DateTimeOffset.UtcNow),
            applications: [new TrackedApplication("pycharm64", "PyCharm", true)]);

        var report = await service.GetReportAsync(TestContext.Current.CancellationToken);

        Assert.False(report.WorkTracking.CanAccumulate);
        Assert.Contains(report.WorkTracking.Reasons, reason =>
            reason.Contains("前台是 chrome") && reason.Contains("不在白名单"));
    }

    [Fact]
    public async Task LockedOrIdleSamplesReportReasons()
    {
        var rule = ReminderRule.CreateDefaultActiveWork(DefaultReminderRuleIds.Activity);
        var locked = CreateService(
            rule,
            null,
            new ActivitySample("pycharm64", true, true, TimeSpan.FromSeconds(1), DateTimeOffset.UtcNow));
        var lockedReport = await locked.GetReportAsync(TestContext.Current.CancellationToken);
        Assert.Contains(lockedReport.WorkTracking.Reasons, reason => reason.Contains("锁屏"));

        var idle = CreateService(
            rule,
            null,
            new ActivitySample("pycharm64", true, false, TimeSpan.FromMinutes(7), DateTimeOffset.UtcNow));
        var idleReport = await idle.GetReportAsync(TestContext.Current.CancellationToken);
        Assert.Contains(idleReport.WorkTracking.Reasons, reason => reason.Contains("无操作不计时"));
        Assert.False(idleReport.WorkTracking.CanAccumulate);
    }

    [Fact]
    public async Task DisabledRuleReportsStoppedWithoutEstimate()
    {
        var rule = ReminderRule.CreateDefaultHydration(DefaultReminderRuleIds.Hydration) with
        {
            Enabled = false,
        };
        var service = CreateService(
            rule,
            null,
            new ActivitySample("pycharm64", true, false, TimeSpan.FromSeconds(5), DateTimeOffset.UtcNow));

        var report = await service.GetReportAsync(TestContext.Current.CancellationToken);

        var hydration = Assert.Single(report.Rules);
        Assert.Equal("已停用", hydration.StateText);
        Assert.Contains(hydration.Reasons, reason => reason.Contains("停用"));
    }

    [Fact]
    public async Task OutsideActiveWindowExplainsNoReminder()
    {
        // Fixed clock at 06:00 UTC, rule window is 08:00–23:00 local (UTC here).
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 9, 11, 6, 0, 0, TimeSpan.Zero));
        var rule = ReminderRule.CreateDefaultHydration(DefaultReminderRuleIds.Hydration);
        var service = CreateService(
            rule,
            null,
            new ActivitySample("pycharm64", true, false, TimeSpan.FromSeconds(5), DateTimeOffset.UtcNow),
            clock);

        var report = await service.GetReportAsync(TestContext.Current.CancellationToken);

        var hydration = Assert.Single(report.Rules);
        Assert.Contains(hydration.Reasons, reason => reason.Contains("不在生效时段"));
    }

    [Fact]
    public async Task QuietHoursExplainsSuppressedReminder()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 9, 11, 13, 30, 0, TimeSpan.Zero));
        var rule = ReminderRule.CreateDefaultHydration(DefaultReminderRuleIds.Hydration) with
        {
            QuietHours = new QuietHours(new TimeOnly(13, 0), new TimeOnly(14, 0), true),
        };
        var service = CreateService(
            rule,
            null,
            new ActivitySample("pycharm64", true, false, TimeSpan.FromSeconds(5), DateTimeOffset.UtcNow),
            clock);

        var report = await service.GetReportAsync(TestContext.Current.CancellationToken);

        var hydration = Assert.Single(report.Rules);
        Assert.Contains(hydration.Reasons, reason => reason.Contains("免打扰时段"));
    }

    [Fact]
    public async Task WaitingRetryAndDueStatesAreDescribed()
    {
        var rule = ReminderRule.CreateDefaultHydration(DefaultReminderRuleIds.Hydration);
        var retryAt = new DateTimeOffset(2026, 9, 11, 10, 5, 0, TimeSpan.Zero);
        var waiting = new ReminderRuntimeState(
            rule.Id,
            Guid.NewGuid(),
            900,
            900,
            ReminderRuntimeStatus.WaitingRetry,
            1,
            retryAt,
            DateTimeOffset.UtcNow);
        var service = CreateService(rule, waiting, WhitelistedSample());
        var report = await service.GetReportAsync(TestContext.Current.CancellationToken);
        var hydration = Assert.Single(report.Rules);
        Assert.Contains("搁置", hydration.StateText);
        Assert.Contains("再次提醒", hydration.NextEstimateText);

        var due = waiting with { Status = ReminderRuntimeStatus.Due, RetryDueAt = null };
        service = CreateService(rule, due, WhitelistedSample());
        report = await service.GetReportAsync(TestContext.Current.CancellationToken);
        hydration = Assert.Single(report.Rules);
        Assert.Equal("已到提醒时间", hydration.StateText);
    }

    [Fact]
    public async Task PauseAndChannelTextsReflectInjectedState()
    {
        var rule = ReminderRule.CreateDefaultHydration(DefaultReminderRuleIds.Hydration);
        var pausedUntil = new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
        var service = new StatusDiagnosticsService(
            new StubRepository([rule], []),
            new StubProbe(WhitelistedSample()),
            Zone,
            new FixedTimeProvider(new DateTimeOffset(2026, 9, 11, 11, 0, 0, TimeSpan.Zero)),
            isPetEnabled: () => false,
            isPaused: () => true,
            getPausedUntil: () => pausedUntil);

        var report = await service.GetReportAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Windows 通知提醒", report.ReminderChannelText);
        Assert.Contains("已暂停", report.PauseText);
    }

    private static ActivitySample WhitelistedSample() =>
        new("pycharm64", true, false, TimeSpan.FromSeconds(10), DateTimeOffset.UtcNow);

    private static StatusDiagnosticsService CreateService(
        ReminderRule rule,
        ReminderRuntimeState? runtime,
        ActivitySample sample,
        TimeProvider? timeProvider = null,
        IReadOnlyList<TrackedApplication>? applications = null) =>
        new(
            new StubRepository([rule], applications ?? [], runtime),
            new StubProbe(sample),
            Zone,
            timeProvider ?? TimeProvider.System);

    private sealed class StubProbe(ActivitySample sample) : IActivityProbe
    {
        public ActivitySample Capture() => sample;
    }

    private sealed class StubRepository(
        IReadOnlyList<ReminderRule> rules,
        IReadOnlyList<TrackedApplication> applications,
        ReminderRuntimeState? runtime = null) : IEventRepository
    {
        public Task AppendReminderAsync(ReminderEvent item, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ReminderEvent?> ReadReminderEventAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> TryUpdateReminderAsync(
            ReminderEvent item,
            ReminderOutcome expectedOutcome,
            int expectedRetryIndex,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> UpdateReminderChannelAsync(
            Guid id,
            string expectedChannel,
            string channel,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task AppendWorkSessionAsync(WorkSession session, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ReminderEvent>> ReadReminderEventsAsync(
            DateOnly day,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ReminderRule>> ReadReminderRulesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(rules);

        public Task UpsertReminderRuleAsync(ReminderRule rule, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ReminderRuntimeState?> ReadReminderRuntimeStateAsync(
            Guid ruleId,
            CancellationToken cancellationToken) =>
            runtime is not null && runtime.RuleId == ruleId
                ? Task.FromResult<ReminderRuntimeState?>(runtime)
                : Task.FromResult<ReminderRuntimeState?>(null);

        public Task UpsertReminderRuntimeStateAsync(
            ReminderRuntimeState state,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<WorkSession>> ReadWorkSessionsAsync(
            DateOnly day,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<TrackedApplication>> ReadTrackedApplicationsAsync(
            CancellationToken cancellationToken) => Task.FromResult(applications);

        public Task UpsertTrackedApplicationAsync(
            TrackedApplication application,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
