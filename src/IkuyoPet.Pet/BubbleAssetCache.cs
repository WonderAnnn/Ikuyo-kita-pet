using System.IO;
using System.Collections.Generic;
using System.Windows.Media.Imaging;

namespace IkuyoPet.Pet;

public sealed record BubbleAssetCacheSnapshot(
    int EntryCount,
    long Hits,
    long Misses,
    long LoadFailures);

/// <summary>
/// Keeps decoded bubble images alive for the lifetime of the desktop pet.
/// Release assets are immutable, so a normalized absolute path is a stable key.
/// </summary>
public sealed class BubbleAssetCache
{
    private readonly Func<string, BitmapSource?> loader;
    private readonly object gate = new();
    private readonly Dictionary<string, Lazy<BitmapSource?>> entries = new(StringComparer.OrdinalIgnoreCase);
    private long hits;
    private long misses;
    private long loadFailures;

    public BubbleAssetCache(Func<string, BitmapSource?> loader)
    {
        this.loader = loader ?? throw new ArgumentNullException(nameof(loader));
    }

    public BitmapSource? Get(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var key = Normalize(path);
        lock (gate)
        {
            if (entries.TryGetValue(key, out var cached))
            {
                hits++;
                return cached.Value;
            }

            var candidate = new Lazy<BitmapSource?>(() => Load(key), LazyThreadSafetyMode.ExecutionAndPublication);
            entries.Add(key, candidate);
            misses++;
            return candidate.Value;
        }
    }

    public Task PreloadAsync(IEnumerable<string> paths, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return Task.Run(() =>
        {
            foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                _ = Get(path);
            }
        }, cancellationToken);
    }

    public void Clear()
    {
        lock (gate)
        {
            entries.Clear();
            hits = 0;
            misses = 0;
            loadFailures = 0;
        }
    }

    public BubbleAssetCacheSnapshot GetSnapshot()
    {
        lock (gate)
        {
            return new(entries.Count, hits, misses, loadFailures);
        }
    }

    private BitmapSource? Load(string path)
    {
        try
        {
            var image = loader(path);
            if (image is null) loadFailures++;
            return image;
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            loadFailures++;
            return null;
        }
    }

    private static string Normalize(string path) => Path.GetFullPath(path);
}
