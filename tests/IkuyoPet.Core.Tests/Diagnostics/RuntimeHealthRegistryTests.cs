using IkuyoPet.Core.Diagnostics;
using Xunit;

namespace IkuyoPet.Core.Tests.Diagnostics;

public sealed class RuntimeHealthRegistryTests
{
    [Fact]
    public void NewerLoopUpdatesWinAndSnapshotIsImmutable()
    {
        var registry = new RuntimeHealthRegistry();
        var first = new DateTimeOffset(2026, 9, 14, 9, 0, 0, TimeSpan.Zero);
        var later = first.AddMinutes(1);

        registry.MarkLoopStarted("reminder", later);
        registry.MarkLoopSucceeded("reminder", later, later.AddSeconds(30));
        registry.MarkLoopFailed("reminder", "database-busy", first);

        var snapshot = registry.GetSnapshot();
        var loop = Assert.Single(snapshot.Loops);
        Assert.Equal("reminder", loop.Name);
        Assert.Equal(HealthLevel.Healthy, loop.Level);
        Assert.Equal(later, loop.LastSucceededAt);
        Assert.Null(loop.LastErrorCode);
        Assert.IsAssignableFrom<IList<LoopHealthSnapshot>>(snapshot.Loops);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<LoopHealthSnapshot>)snapshot.Loops).Add(loop));
    }

    [Fact]
    public async Task ParallelWritesKeepCompleteLoopSnapshotsAndLatestReminder()
    {
        var registry = new RuntimeHealthRegistry();
        var origin = new DateTimeOffset(2026, 9, 14, 9, 0, 0, TimeSpan.Zero);
        var jobs = Enumerable.Range(0, 100).Select(index => Task.Run(() =>
        {
            var at = origin.AddSeconds(index);
            var loop = index % 2 == 0 ? "reminder" : "work-tracking";
            registry.MarkLoopStarted(loop, at);
            registry.MarkLoopSucceeded(loop, at, at.AddSeconds(30));
            registry.RecordReminder(new RecentReminderSnapshot(
                Guid.Parse($"00000000-0000-0000-0000-{index + 1:000000000000}"),
                "water",
                "pet",
                "presented",
                at));
        }));

        await Task.WhenAll(jobs);

        var snapshot = registry.GetSnapshot();
        Assert.Equal(2, snapshot.Loops.Count);
        Assert.All(snapshot.Loops, loop =>
        {
            Assert.Equal(HealthLevel.Healthy, loop.Level);
            Assert.NotNull(loop.LastStartedAt);
            Assert.NotNull(loop.LastSucceededAt);
            Assert.NotNull(loop.NextWakeAt);
            Assert.True(loop.NextWakeAt >= loop.LastSucceededAt);
        });
        Assert.NotNull(snapshot.RecentReminder);
        Assert.Equal(origin.AddSeconds(99), snapshot.RecentReminder!.OccurredAt);
    }

    [Fact]
    public void EmptySnapshotIsSafeToDisplay()
    {
        var snapshot = new RuntimeHealthRegistry().GetSnapshot();

        Assert.Empty(snapshot.Loops);
        Assert.Null(snapshot.RecentReminder);
    }
}
