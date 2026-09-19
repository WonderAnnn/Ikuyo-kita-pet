using IkuyoPet.Core.WorkTracking;

namespace IkuyoPet.Infrastructure.Windows;

/// <summary>
/// Publishes fresh foreground samples whenever the active window changes.
/// Implementations must not reuse the cached activity probe so rapid switches
/// remain visible to the work tracker.
/// </summary>
public interface IForegroundActivityChangeSource : IDisposable
{
    event Action<ActivitySample>? SampleCaptured;

    void Start();

    void Shutdown();
}
