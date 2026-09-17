using IkuyoPet.Core.Dashboard;
using IkuyoPet.Core.Reminders;
using IkuyoPet.Core.Storage;
using IkuyoPet.Core.WorkTracking;
using IkuyoPet.Infrastructure.Windows;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.Windows;

public sealed class WorkTrackingLoopTests
{
    [Fact]
    public async Task StopFlushesOnlySamplesAcceptedByExistingWorkTrackingService()
    {
        var start = new DateTimeOffset(2026, 9, 8, 10, 0, 0, TimeSpan.FromHours(8));
        var repository = new RecordingRepository();
        var service = new WorkTrackingService(
            new SequenceProbe(
                new ActivitySample("pycharm64", true, false, TimeSpan.FromMinutes(1), start),
                new ActivitySample("pycharm64", true, false, TimeSpan.FromMinutes(1), start.AddSeconds(30))),
            repository);
        var loop = new WorkTrackingLoop(service, TimeSpan.Zero);

        await loop.RunOneCycleAsync(TestContext.Current.CancellationToken);
        await loop.RunOneCycleAsync(TestContext.Current.CancellationToken);
        await loop.StopAsync(TestContext.Current.CancellationToken);

        var session = Assert.Single(repository.Sessions);
        Assert.Equal(30, session.ActiveSeconds);
    }

    [Fact]
    public async Task StopPreventsFurtherSampling()
    {
        var start = new DateTimeOffset(2026, 9, 8, 10, 0, 0, TimeSpan.FromHours(8));
        var probe = new SequenceProbe(
            new ActivitySample("pycharm64", true, false, TimeSpan.FromMinutes(1), start),
            new ActivitySample("pycharm64", true, false, TimeSpan.FromMinutes(1), start.AddSeconds(30)));
        var repository = new RecordingRepository();
        var loop = new WorkTrackingLoop(new WorkTrackingService(probe, repository), TimeSpan.Zero);

        await loop.RunOneCycleAsync(TestContext.Current.CancellationToken);
        await loop.StopAsync(TestContext.Current.CancellationToken);
        await loop.RunOneCycleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, probe.CaptureCount);
    }

    private sealed class SequenceProbe(params ActivitySample[] samples) : IActivityProbe
    {
        private int index;

        public int CaptureCount { get; private set; }

        public ActivitySample Capture()
        {
            CaptureCount++;
            return samples[Math.Min(index++, samples.Length - 1)];
        }
    }

    private sealed class RecordingRepository : IEventRepository
    {
        public List<ReminderEvent> ReminderEvents { get; } = [];
        public List<WorkSession> Sessions { get; } = [];

        public Task AppendReminderAsync(ReminderEvent item, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ReminderEvent?> ReadReminderEventAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<ReminderEvent?>(null);
        public Task<bool> TryUpdateReminderAsync(ReminderEvent item, ReminderOutcome expectedOutcome, int expectedRetryIndex, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<bool> UpdateReminderChannelAsync(Guid id, string expectedChannel, string channel, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task AppendWorkSessionAsync(WorkSession session, CancellationToken cancellationToken) { Sessions.Add(session); return Task.CompletedTask; }
        public Task<IReadOnlyList<ReminderEvent>> ReadReminderEventsAsync(DateOnly day, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ReminderEvent>>(ReminderEvents);
        public Task<IReadOnlyList<ReminderRule>> ReadReminderRulesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ReminderRule>>([]);
        public Task UpsertReminderRuleAsync(ReminderRule rule, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<WorkSession>> ReadWorkSessionsAsync(DateOnly day, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<WorkSession>>(Sessions);
        public Task<IReadOnlyList<TrackedApplication>> ReadTrackedApplicationsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TrackedApplication>>([]);
        public Task UpsertTrackedApplicationAsync(TrackedApplication application, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
