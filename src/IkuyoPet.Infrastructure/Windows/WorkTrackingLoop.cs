using System.Globalization;
using System.Threading.Channels;
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
    private readonly IForegroundActivityChangeSource? activityChangeSource;
    private readonly Channel<ActivitySample> activitySamples =
        Channel.CreateUnbounded<ActivitySample>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
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
        TimeProvider? timeProvider = null,
        IForegroundActivityChangeSource? activityChangeSource = null)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        this.activeWorkConsumer = activeWorkConsumer ?? throw new ArgumentNullException(nameof(activeWorkConsumer));
        this.sampleInterval = sampleInterval ?? TimeSpan.FromSeconds(30);
        this.sampleIntervalProvider = sampleIntervalProvider;
        this.healthRegistry = healthRegistry;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.activityChangeSource = activityChangeSource;
        if (activityChangeSource is not null)
        {
            activityChangeSource.SampleCaptured += OnActivitySampleCaptured;
        }
        ArgumentOutOfRangeException.ThrowIfLessThan(this.sampleInterval, TimeSpan.Zero);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            TryStartActivityChangeSource();
            while (Volatile.Read(ref stopped) == 0)
            {
                try
                {
                    healthRegistry?.MarkLoopStarted("work-tracking", timeProvider.GetUtcNow());
                    await RunOneCycleAsync(cancellationToken).ConfigureAwait(false);
                    var interval = GetSampleInterval();
                    var completedAt = timeProvider.GetUtcNow();
                    healthRegistry?.MarkLoopSucceeded("work-tracking", completedAt, completedAt.Add(interval));
                    await WaitForNextCycleOrActivityAsync(interval, cancellationToken)
                        .ConfigureAwait(false);
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

    private async Task WaitForNextCycleOrActivityAsync(
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        var delay = interval == TimeSpan.Zero
            ? Task.CompletedTask
            : Task.Delay(interval, cancellationToken);
        var read = activitySamples.Reader.WaitToReadAsync(cancellationToken).AsTask();

        while (true)
        {
            await Task.WhenAny(delay, read).ConfigureAwait(false);
            while (activitySamples.Reader.TryRead(out var sample))
            {
                await ProcessActivitySampleAsync(sample, cancellationToken).ConfigureAwait(false);
            }

            if (delay.IsCompleted)
            {
                return;
            }

            read = activitySamples.Reader.WaitToReadAsync(cancellationToken).AsTask();
        }
    }

    private async Task ProcessActivitySampleAsync(
        ActivitySample sample,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref stopped) != 0) return;
        var delta = await service.ObserveAsync(sample, cancellationToken).ConfigureAwait(false);
        await activeWorkConsumer(delta, cancellationToken).ConfigureAwait(false);
    }

    private void OnActivitySampleCaptured(ActivitySample sample)
    {
        if (Volatile.Read(ref stopped) == 0)
        {
            activitySamples.Writer.TryWrite(sample);
        }
    }

    private void TryStartActivityChangeSource()
    {
        if (activityChangeSource is null) return;
        try
        {
            activityChangeSource.Start();
        }
        catch (Exception exception)
        {
            LogLoopError(new InvalidOperationException(
                "Foreground activity event source failed to start; polling remains active.",
                exception));
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref stopped, 1) != 0)
        {
            return;
        }

        if (activityChangeSource is not null)
        {
            activityChangeSource.SampleCaptured -= OnActivitySampleCaptured;
            try
            {
                activityChangeSource.Shutdown();
            }
            finally
            {
                activityChangeSource.Dispose();
            }
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
