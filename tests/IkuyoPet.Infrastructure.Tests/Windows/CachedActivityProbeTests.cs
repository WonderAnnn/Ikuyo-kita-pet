using IkuyoPet.Core.WorkTracking;
using IkuyoPet.Infrastructure.Windows;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.Windows;

public sealed class CachedActivityProbeTests
{
    [Fact]
    public void TwoConsumersShareOneCaptureInsideTheCacheWindow()
    {
        var sample = new ActivitySample(
            "chrome", true, false, TimeSpan.Zero, DateTimeOffset.UtcNow);
        var source = new CountingActivityProbe(sample);
        var cache = new CachedActivityProbe(source, TimeProvider.System, TimeSpan.FromSeconds(20));

        _ = cache.Capture();
        _ = cache.Capture();

        Assert.Equal(1, source.CaptureCount);
        Assert.Equal(1, cache.GetSnapshot().CacheHits);
    }

    [Fact]
    public void ForceCaptureBypassesTheCache()
    {
        var sample = new ActivitySample(
            "chrome", true, false, TimeSpan.Zero, DateTimeOffset.UtcNow);
        var source = new CountingActivityProbe(sample);
        var cache = new CachedActivityProbe(source, TimeProvider.System, TimeSpan.FromMinutes(1));

        _ = cache.Capture();
        _ = cache.ForceCapture();

        Assert.Equal(2, source.CaptureCount);
    }

    private sealed class CountingActivityProbe(ActivitySample sample) : IActivityProbe
    {
        public int CaptureCount { get; private set; }

        public ActivitySample Capture()
        {
            CaptureCount++;
            return sample;
        }
    }
}
