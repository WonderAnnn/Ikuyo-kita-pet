using IkuyoPet.Core.Reminders;
using IkuyoPet.Core.Storage;
using IkuyoPet.Core.WorkTracking;
using IkuyoPet.Infrastructure.Windows;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.Windows;

public sealed class ActiveWorkSamplingTests
{
    [Theory]
    [InlineData("locked")]
    [InlineData("fullscreen")]
    [InlineData("presentation")]
    [InlineData("idle")]
    public async Task SuppressedSamplesReturnZeroAndRecoveryStartsFromANewBaseline(string reason)
    {
        var start = new DateTimeOffset(2026, 9, 9, 10, 0, 0, TimeSpan.FromHours(8));
        var repository = new RecordingRepository();
        var service = new WorkTrackingService(
            new SequenceProbe(
                Sample(start),
                Sample(start.AddSeconds(5)),
                SuppressedSample(reason, start.AddSeconds(10)),
                Sample(start.AddSeconds(15)),
                Sample(start.AddSeconds(20))),
            repository);

        var first = await service.SampleOnceAsync(TestContext.Current.CancellationToken);
        var counted = await service.SampleOnceAsync(TestContext.Current.CancellationToken);
        var suppressed = await service.SampleOnceAsync(TestContext.Current.CancellationToken);
        var resumedBaseline = await service.SampleOnceAsync(TestContext.Current.CancellationToken);
        var resumedCounted = await service.SampleOnceAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, first.ActiveSeconds);
        Assert.Equal(5, counted.ActiveSeconds);
        Assert.Equal(0, suppressed.ActiveSeconds);
        Assert.Equal(0, resumedBaseline.ActiveSeconds);
        Assert.Equal(5, resumedCounted.ActiveSeconds);
        Assert.Equal(10, repository.Sessions.Sum(item => item.ActiveSeconds));
    }

    [Fact]
    public async Task LoopCapturesOnceAndSharesEachDeltaWithItsConsumer()
    {
        var start = new DateTimeOffset(2026, 9, 9, 11, 0, 0, TimeSpan.FromHours(8));
        var probe = new SequenceProbe(Sample(start), Sample(start.AddSeconds(30)));
        var repository = new RecordingRepository();
        var received = new List<ActiveWorkDelta>();
        var loop = new WorkTrackingLoop(
            new WorkTrackingService(probe, repository),
            (delta, _) =>
            {
                received.Add(delta);
                return Task.CompletedTask;
            },
            TimeSpan.Zero);

        await loop.RunOneCycleAsync(TestContext.Current.CancellationToken);
        await loop.RunOneCycleAsync(TestContext.Current.CancellationToken);
        await loop.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, probe.CaptureCount);
        Assert.Collection(
            received,
            item => Assert.Equal(0, item.ActiveSeconds),
            item => Assert.Equal(30, item.ActiveSeconds));
        Assert.Equal(30, Assert.Single(repository.Sessions).ActiveSeconds);
    }

    private static ActivitySample Sample(DateTimeOffset observedAt) =>
        new("pycharm64", true, false, TimeSpan.FromMinutes(1), observedAt);

    private static ActivitySample SuppressedSample(string reason, DateTimeOffset observedAt) => reason switch
    {
        "locked" => Sample(observedAt) with { IsLocked = true },
        "fullscreen" => Sample(observedAt) with { IsFullScreen = true },
        "presentation" => Sample(observedAt) with { IsPresentationMode = true },
        "idle" => Sample(observedAt) with { IdleTime = TimeSpan.FromMinutes(5) },
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

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
        public List<WorkSession> Sessions { get; } = [];

        public Task AppendReminderAsync(ReminderEvent item, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ReminderEvent?> ReadReminderEventAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<ReminderEvent?>(null);
        public Task<bool> TryUpdateReminderAsync(ReminderEvent item, ReminderOutcome expectedOutcome, int expectedRetryIndex, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<bool> UpdateReminderChannelAsync(Guid id, string expectedChannel, string channel, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task AppendWorkSessionAsync(WorkSession session, CancellationToken cancellationToken) { Sessions.Add(session); return Task.CompletedTask; }
        public Task<IReadOnlyList<ReminderEvent>> ReadReminderEventsAsync(DateOnly day, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ReminderEvent>>([]);
        public Task<IReadOnlyList<ReminderRule>> ReadReminderRulesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ReminderRule>>([]);
        public Task UpsertReminderRuleAsync(ReminderRule rule, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<WorkSession>> ReadWorkSessionsAsync(DateOnly day, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<WorkSession>>(Sessions);
        public Task<IReadOnlyList<TrackedApplication>> ReadTrackedApplicationsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TrackedApplication>>([]);
        public Task UpsertTrackedApplicationAsync(TrackedApplication application, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
