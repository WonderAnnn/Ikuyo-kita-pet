using System.Windows.Media.Imaging;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.Pet;

public sealed class BubbleAssetCacheTests
{
    [Fact]
    public void SamePathIsLoadedOnceAndReportedAsAHit()
    {
        var calls = 0;
        var cache = new IkuyoPet.Pet.BubbleAssetCache(_ =>
        {
            calls++;
            return null;
        });

        _ = cache.Get("C:\\publish\\assets\\bubbles\\cloud-chibi\\bubble-filled.png");
        _ = cache.Get("C:\\publish\\assets\\bubbles\\cloud-chibi\\bubble-filled.png");

        Assert.Equal(1, calls);
        Assert.Equal(new IkuyoPet.Pet.BubbleAssetCacheSnapshot(1, 1, 1, 1), cache.GetSnapshot());
    }

    [Fact]
    public void ClearRemovesCachedFailuresAndCounters()
    {
        var cache = new IkuyoPet.Pet.BubbleAssetCache(_ => null);

        _ = cache.Get("C:\\assets\\missing.png");
        cache.Clear();

        Assert.Equal(new IkuyoPet.Pet.BubbleAssetCacheSnapshot(0, 0, 0, 0), cache.GetSnapshot());
    }

    [Fact]
    public void LoaderExceptionIsRecordedAsAFailureWithoutBreakingRendering()
    {
        var cache = new IkuyoPet.Pet.BubbleAssetCache(_ => throw new InvalidOperationException("broken image"));

        var image = cache.Get("C:\\assets\\broken.png");

        Assert.Null(image);
        Assert.Equal(1, cache.GetSnapshot().LoadFailures);
    }

    [Fact]
    public async Task ConcurrentGetsUseOneLoaderAndKeepCountersConsistent()
    {
        var calls = 0;
        var cache = new IkuyoPet.Pet.BubbleAssetCache(_ =>
        {
            Interlocked.Increment(ref calls);
            Thread.Sleep(20);
            return null;
        });
        var path = "C:\\assets\\concurrent.png";

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => cache.Get(path))));

        Assert.Equal(1, calls);
        Assert.Equal(new IkuyoPet.Pet.BubbleAssetCacheSnapshot(1, 7, 1, 1), cache.GetSnapshot());
    }
}
