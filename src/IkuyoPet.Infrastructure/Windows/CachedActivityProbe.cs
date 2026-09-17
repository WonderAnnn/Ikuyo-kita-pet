using IkuyoPet.Core.WorkTracking;

namespace IkuyoPet.Infrastructure.Windows;

public sealed record CachedActivityProbeSnapshot(
    long SourceCaptures,
    long CacheHits,
    DateTimeOffset? LastCapturedAt);

/// <summary>
/// Shares one short-lived desktop observation between the work tracker,
/// reminder suppression and diagnostics consumers.
/// </summary>
public sealed class CachedActivityProbe : IActivityProbe
{
    private readonly IActivityProbe source;
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan cacheWindow;
    private readonly object gate = new();
    private ActivitySample? cached;
    private DateTimeOffset cacheExpiresAt;
    private DateTimeOffset? lastCapturedAt;
    private long sourceCaptures;
    private long cacheHits;

    public CachedActivityProbe(
        IActivityProbe source,
        TimeProvider? timeProvider = null,
        TimeSpan? cacheWindow = null)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.cacheWindow = cacheWindow ?? TimeSpan.FromSeconds(20);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(this.cacheWindow, TimeSpan.Zero);
    }

    public ActivitySample Capture() => CaptureCore(force: false);

    public ActivitySample ForceCapture() => CaptureCore(force: true);

    public bool IsReminderSuppressed()
    {
        var sample = ForceCapture();
        return sample.IsLocked || sample.IsFullScreen || sample.IsPresentationMode;
    }

    public CachedActivityProbeSnapshot GetSnapshot()
    {
        lock (gate)
        {
            return new(sourceCaptures, cacheHits, lastCapturedAt);
        }
    }

    private ActivitySample CaptureCore(bool force)
    {
        lock (gate)
        {
            var now = timeProvider.GetUtcNow();
            if (!force && cached is not null && now < cacheExpiresAt)
            {
                cacheHits++;
                return cached;
            }

            var sample = source.Capture();
            cached = sample;
            cacheExpiresAt = now + cacheWindow;
            lastCapturedAt = now;
            sourceCaptures++;
            return sample;
        }
    }
}
