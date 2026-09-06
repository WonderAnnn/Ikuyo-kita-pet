using IkuyoPet.Core.WorkTracking;
using Xunit;

namespace IkuyoPet.Core.Tests.WorkTracking;

public sealed class WorkSessionAccumulatorTests
{
    [Fact]
    public void CountsOnlyWhitelistedForegroundUnlockedAndRecentlyActiveSamples()
    {
        var accumulator = new WorkSessionAccumulator(TimeSpan.FromMinutes(5));
        var now = new DateTimeOffset(2026, 9, 6, 10, 0, 0, TimeSpan.FromHours(8));

        accumulator.Observe(new ActivitySample("PyCharm", true, false, TimeSpan.FromMinutes(1), now));
        accumulator.Observe(new ActivitySample("PyCharm", false, false, TimeSpan.Zero, now.AddSeconds(5)));

        Assert.Equal(TimeSpan.FromSeconds(5), accumulator.ActiveTime);
    }

    [Fact]
    public void DoesNotCountIntervalAfterLockedSample()
    {
        var accumulator = new WorkSessionAccumulator(TimeSpan.FromMinutes(5));
        var now = new DateTimeOffset(2026, 9, 6, 10, 0, 0, TimeSpan.FromHours(8));

        accumulator.Observe(new ActivitySample("PyCharm", true, true, TimeSpan.Zero, now));
        accumulator.Observe(new ActivitySample("PyCharm", true, false, TimeSpan.Zero, now.AddSeconds(5)));

        Assert.Equal(TimeSpan.Zero, accumulator.ActiveTime);
    }

    [Fact]
    public void DoesNotCountWhenPreviousSampleReachedIdleLimit()
    {
        var accumulator = new WorkSessionAccumulator(TimeSpan.FromMinutes(5));
        var now = new DateTimeOffset(2026, 9, 6, 10, 0, 0, TimeSpan.FromHours(8));

        accumulator.Observe(new ActivitySample("PyCharm", true, false, TimeSpan.FromMinutes(5), now));
        accumulator.Observe(new ActivitySample("PyCharm", true, false, TimeSpan.Zero, now.AddSeconds(5)));

        Assert.Equal(TimeSpan.Zero, accumulator.ActiveTime);
    }

    [Fact]
    public void DoesNotCountIntervalAcrossApplicationSwitch()
    {
        var accumulator = new WorkSessionAccumulator(TimeSpan.FromMinutes(5));
        var now = new DateTimeOffset(2026, 9, 6, 10, 0, 0, TimeSpan.FromHours(8));

        accumulator.Observe(new ActivitySample("PyCharm", true, false, TimeSpan.FromMinutes(1), now));
        accumulator.Observe(new ActivitySample("Code", true, false, TimeSpan.FromMinutes(1), now.AddSeconds(5)));

        Assert.Equal(TimeSpan.Zero, accumulator.ActiveTime);
    }

    [Fact]
    public void DoesNotCountWhenClockMovesBackwards()
    {
        var accumulator = new WorkSessionAccumulator(TimeSpan.FromMinutes(5));
        var now = new DateTimeOffset(2026, 9, 6, 10, 0, 0, TimeSpan.FromHours(8));

        accumulator.Observe(new ActivitySample("PyCharm", true, false, TimeSpan.FromMinutes(1), now));
        accumulator.Observe(new ActivitySample("PyCharm", true, false, TimeSpan.FromMinutes(1), now.AddSeconds(-1)));

        Assert.Equal(TimeSpan.Zero, accumulator.ActiveTime);
    }
}