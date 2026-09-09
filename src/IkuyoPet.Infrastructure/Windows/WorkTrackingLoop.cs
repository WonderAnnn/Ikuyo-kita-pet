using IkuyoPet.Core.WorkTracking;

namespace IkuyoPet.Infrastructure.Windows;

public sealed class WorkTrackingLoop
{
    private readonly WorkTrackingService service;
    private readonly Func<ActiveWorkDelta, CancellationToken, Task> activeWorkConsumer;
    private readonly TimeSpan sampleInterval;
    private int stopped;

    public WorkTrackingLoop(WorkTrackingService service, TimeSpan? sampleInterval = null)
        : this(service, static (_, _) => Task.CompletedTask, sampleInterval)
    {
    }

    public WorkTrackingLoop(
        WorkTrackingService service,
        Func<ActiveWorkDelta, CancellationToken, Task> activeWorkConsumer,
        TimeSpan? sampleInterval = null)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        this.activeWorkConsumer = activeWorkConsumer ?? throw new ArgumentNullException(nameof(activeWorkConsumer));
        this.sampleInterval = sampleInterval ?? TimeSpan.FromSeconds(30);
        ArgumentOutOfRangeException.ThrowIfLessThan(this.sampleInterval, TimeSpan.Zero);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (Volatile.Read(ref stopped) == 0)
            {
                await RunOneCycleAsync(cancellationToken).ConfigureAwait(false);
                if (sampleInterval == TimeSpan.Zero)
                {
                    await Task.Yield();
                }
                else
                {
                    await Task.Delay(sampleInterval, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown is expected; the finally block flushes the last valid session.
        }
        finally
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public async Task RunOneCycleAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref stopped) != 0) return;
        var delta = await service.SampleOnceAsync(cancellationToken).ConfigureAwait(false);
        await activeWorkConsumer(delta, cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref stopped, 1) != 0)
        {
            return;
        }

        await service.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
