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
            new ActivitySample("code", true, false, TimeSpan.FromMinutes(1), start.AddSeconds(10)));

        var session = Assert.Single(repository.Sessions);
        Assert.Equal("pycharm64", session.ProcessName);
        Assert.Equal("app-switched", session.EndReason);
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

        public Task AppendWorkSessionAsync(WorkSession session, CancellationToken cancellationToken)
        {
            Sessions.Add(session);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ReminderEvent>> ReadReminderEventsAsync(
            DateOnly day,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReminderEvent>>(ReminderEvents);
    }
}
