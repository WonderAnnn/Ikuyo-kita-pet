using IkuyoPet.Core.Dashboard;
using IkuyoPet.Core.Reminders;
using IkuyoPet.Core.WorkTracking;
using IkuyoPet.Core.Storage;
using IkuyoPet.Infrastructure.Windows;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.Windows;

public sealed class WorkTrackingServiceTests
{
    [Fact]
    public async Task WritesFiveSecondSessionForTwoValidForegroundSamples()
    {
        var start = new DateTimeOffset(2026, 9, 6, 10, 0, 0, TimeSpan.FromHours(8));
        var probe = new SequenceProbe(
            new ActivitySample("pycharm64", true, false, TimeSpan.FromMinutes(1), start),
            new ActivitySample("pycharm64", true, false, TimeSpan.FromMinutes(1), start.AddSeconds(5)));
        var repository = new RecordingRepository();
        var service = new WorkTrackingService(probe, repository);

        await service.SampleOnceAsync(TestContext.Current.CancellationToken);
        await service.SampleOnceAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        var session = Assert.Single(repository.Sessions);
        Assert.Equal("pycharm64", session.ProcessName);
        Assert.Equal(5, session.ActiveSeconds);
        Assert.Equal("stopped", session.EndReason);
    }

    [Fact]
    public async Task DoesNotCountLongSamplingGapAsWorkOrSleep()
    {
        var start = new DateTimeOffset(2026, 9, 8, 10, 0, 0, TimeSpan.FromHours(8));
        var repository = await RunAsync(
            new ActivitySample("pycharm64", true, false, TimeSpan.FromMinutes(1), start),
            new ActivitySample("pycharm64", true, false, TimeSpan.FromMinutes(1), start.AddSeconds(30)),
            new ActivitySample("pycharm64", true, false, TimeSpan.FromMinutes(1), start.AddHours(8)));

        var session = Assert.Single(repository.Sessions);
        Assert.Equal(30, session.ActiveSeconds);
        Assert.Equal("sampling-gap", session.EndReason);
    }

    [Fact]
    public async Task EndsSessionWhenWorkstationBecomesLocked()
    {
        var start = new DateTimeOffset(2026, 9, 6, 10, 0, 0, TimeSpan.FromHours(8));
        var repository = await RunAsync(
            new ActivitySample("pycharm64", true, false, TimeSpan.FromMinutes(1), start),
            new ActivitySample("pycharm64", true, false, TimeSpan.FromMinutes(1), start.AddSeconds(5)),
            new ActivitySample("pycharm64", true, true, TimeSpan.Zero, start.AddSeconds(10)));

        var session = Assert.Single(repository.Sessions);
        Assert.Equal(5, session.ActiveSeconds);
        Assert.Equal("locked", session.EndReason);
    }

    [Fact]
    public async Task EndsSessionWhenForegroundApplicationChanges()
    {
        var start = new DateTimeOffset(2026, 9, 6, 10, 0, 0, TimeSpan.FromHours(8));
        var repository = await RunAsync(
            new ActivitySample("pycharm64", true, false, TimeSpan.FromMinutes(1), start),
            new ActivitySample("pycharm64", true, false, TimeSpan.FromMinutes(1), start.AddSeconds(5)),
            new ActivitySample("code", false, false, TimeSpan.FromMinutes(1), start.AddSeconds(10)));

        var session = Assert.Single(repository.Sessions);
        Assert.Equal("pycharm64", session.ProcessName);
        Assert.Equal("app-switched", session.EndReason);
    }

    [Fact]
    public async Task CountsActiveIntervalWhenSwitchingBetweenWhitelistedApplications()
    {
        var start = new DateTimeOffset(2026, 9, 6, 10, 0, 0, TimeSpan.FromHours(8));
        var probe = new SequenceProbe(
            new ActivitySample("pycharm64", true, false, TimeSpan.FromMinutes(1), start),
            new ActivitySample("Code", true, false, TimeSpan.FromMinutes(1), start.AddSeconds(5)),
            new ActivitySample("Code", true, false, TimeSpan.FromMinutes(1), start.AddSeconds(10)));
        var repository = new RecordingRepository();
        var service = new WorkTrackingService(probe, repository);

        var first = await service.SampleOnceAsync(TestContext.Current.CancellationToken);
        var second = await service.SampleOnceAsync(TestContext.Current.CancellationToken);
        var third = await service.SampleOnceAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, first.ActiveSeconds);
        Assert.Equal(5, second.ActiveSeconds);
        Assert.Equal(5, third.ActiveSeconds);
        Assert.Equal(10, repository.Sessions.Sum(session => session.ActiveSeconds));
        Assert.Collection(
            repository.Sessions,
            session =>
            {
                Assert.Equal("pycharm64", session.ProcessName);
                Assert.Equal(5, session.ActiveSeconds);
            },
            session =>
            {
                Assert.Equal("Code", session.ProcessName);
                Assert.Equal(5, session.ActiveSeconds);
            });
    }

    [Fact]
    public async Task EndsSessionWhenIdleTimeReachesFiveMinutes()
    {
        var start = new DateTimeOffset(2026, 9, 6, 10, 0, 0, TimeSpan.FromHours(8));
        var repository = await RunAsync(
            new ActivitySample("pycharm64", true, false, TimeSpan.FromMinutes(1), start),
            new ActivitySample("pycharm64", true, false, TimeSpan.FromMinutes(1), start.AddSeconds(5)),
            new ActivitySample("pycharm64", true, false, TimeSpan.FromMinutes(5), start.AddSeconds(10)));

        var session = Assert.Single(repository.Sessions);
        Assert.Equal(5, session.ActiveSeconds);
        Assert.Equal("idle", session.EndReason);
    }

    [Fact]
    public async Task InvalidTailEndsAtLastCountedSampleAndAddsNothingToNextDay()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone(
            "IkuyoPet-Test-UTC+08-WorkTracking",
            TimeSpan.FromHours(8),
            "IkuyoPet Test UTC+08",
            "IkuyoPet Test UTC+08");
        var repository = await RunAsync(
            new ActivitySample(
                "pycharm64",
                true,
                false,
                TimeSpan.FromMinutes(1),
                new DateTimeOffset(2026, 9, 6, 23, 59, 45, TimeSpan.FromHours(8))),
            new ActivitySample(
                "pycharm64",
                true,
                false,
                TimeSpan.FromMinutes(1),
                new DateTimeOffset(2026, 9, 6, 23, 59, 50, TimeSpan.FromHours(8))),
            new ActivitySample(
                "pycharm64",
                true,
                true,
                TimeSpan.Zero,
                new DateTimeOffset(2026, 9, 7, 0, 0, 5, TimeSpan.FromHours(8))));
        var session = Assert.Single(repository.Sessions);
        var dashboard = new DashboardQueryService(repository, zone);

        var nextDay = await dashboard.GetAsync(
            new DateOnly(2026, 9, 7),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            new DateTimeOffset(2026, 9, 6, 23, 59, 50, TimeSpan.FromHours(8)),
            session.EndedAt);
        Assert.Equal(TimeSpan.Zero, nextDay.WorkTime);
    }

    private static async Task<RecordingRepository> RunAsync(params ActivitySample[] samples)
    {
        var repository = new RecordingRepository();
        var service = new WorkTrackingService(new SequenceProbe(samples), repository);
        foreach (var sample in samples)
        {
            await service.SampleOnceAsync(TestContext.Current.CancellationToken);
        }

        await service.StopAsync(TestContext.Current.CancellationToken);
        return repository;
    }
    private sealed class SequenceProbe(params ActivitySample[] samples) : IActivityProbe
    {
        private int _index;

        public ActivitySample Capture()
        {
            var sample = samples[Math.Min(_index, samples.Length - 1)];
            _index++;
            return sample;
        }
    }

    private sealed class RecordingRepository : IEventRepository
    {
        public List<ReminderEvent> ReminderEvents { get; } = [];

        public List<WorkSession> Sessions { get; } = [];

        public Task AppendReminderAsync(ReminderEvent item, CancellationToken cancellationToken)
        {
            ReminderEvents.Add(item);
            return Task.CompletedTask;
        }

        public Task<ReminderEvent?> ReadReminderEventAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(ReminderEvents.SingleOrDefault(item => item.Id == id));

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

        public Task AppendWorkSessionAsync(WorkSession session, CancellationToken cancellationToken)
        {
            Sessions.Add(session);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ReminderEvent>> ReadReminderEventsAsync(
            DateOnly day,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReminderEvent>>(ReminderEvents);

        public Task<IReadOnlyList<ReminderRule>> ReadReminderRulesAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReminderRule>>([]);

        public Task UpsertReminderRuleAsync(
            ReminderRule rule,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<WorkSession>> ReadWorkSessionsAsync(
            DateOnly day,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WorkSession>>(Sessions);

        public Task<IReadOnlyList<TrackedApplication>> ReadTrackedApplicationsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TrackedApplication>>([]);

        public Task UpsertTrackedApplicationAsync(
            TrackedApplication application,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
