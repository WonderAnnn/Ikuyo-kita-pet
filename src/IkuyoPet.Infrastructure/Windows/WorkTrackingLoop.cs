using System.Globalization;
using IkuyoPet.Core.Diagnostics;
using IkuyoPet.Core.WorkTracking;

namespace IkuyoPet.Infrastructure.Windows;

public sealed class WorkTrackingLoop
{
    private readonly WorkTrackingService service;
    private readonly Func<ActiveWorkDelta, CancellationToken, Task> activeWorkConsumer;
    private readonly TimeSpan sampleInterval;
    private readonly Func<TimeSpan>? sampleIntervalProvider;
    private readonly RuntimeHealthRegistry? healthRegistry;
    private readonly TimeProvider timeProvider;
    private int stopped;

    public WorkTrackingLoop(WorkTrackingService service, TimeSpan? sampleInterval = null)
        : this(service, static (_, _) => Task.CompletedTask, sampleInterval)
    {
    }

    public WorkTrackingLoop(
        WorkTrackingService service,
        Func<ActiveWorkDelta, CancellationToken, Task> activeWorkConsumer,
        TimeSpan? sampleInterval = null,
        Func<TimeSpan>? sampleIntervalProvider = null,
        RuntimeHealthRegistry? healthRegistry = null,
        TimeProvider? timeProvider = null)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        this.activeWorkConsumer = activeWorkConsumer ?? throw new ArgumentNullException(nameof(activeWorkConsumer));
        this.sampleInterval = sampleInterval ?? TimeSpan.FromSeconds(30);
        this.sampleIntervalProvider = sampleIntervalProvider;
        this.healthRegistry = healthRegistry;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        ArgumentOutOfRangeException.ThrowIfLessThan(this.sampleInterval, TimeSpan.Zero);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (Volatile.Read(ref stopped) == 0)
            {
                try
                {
                    healthRegistry?.MarkLoopStarted("work-tracking", timeProvider.GetUtcNow());
                    await RunOneCycleAsync(cancellationToken).ConfigureAwait(false);
                    var interval = GetSampleInterval();
                    var completedAt = timeProvider.GetUtcNow();
                    healthRegistry?.MarkLoopSucceeded("work-tracking", completedAt, completedAt.Add(interval));
                    if (interval == TimeSpan.Zero)
                    {
                        await Task.Yield();
                    }
                    else
                    {
                        await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    LogLoopError(exception);
                    healthRegistry?.MarkLoopFailed("work-tracking", "cycle-failed", timeProvider.GetUtcNow());
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

    /// <summary>
    /// A crashed loop silently stops all work tracking, so unexpected cycle errors are
    /// recorded next to the sample log instead of vanishing into an unobserved task.
    /// </summary>
    private static void LogLoopError(Exception exception)
    {
        try
        {
            var line = string.Create(CultureInfo.InvariantCulture,
                $"{DateTimeOffset.UtcNow:O} | work tracking cycle failed: {exception}");
            File.AppendAllText(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "IkuyoPet",
                    "debug-loop.log"),
                line + Environment.NewLine);
        }
        catch
        {
            // Diagnostics must never break the loop.
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

    private TimeSpan GetSampleInterval()
    {
        var interval = sampleIntervalProvider?.Invoke() ?? sampleInterval;
        ArgumentOutOfRangeException.ThrowIfLessThan(interval, TimeSpan.Zero);
        return interval;
    }
}
