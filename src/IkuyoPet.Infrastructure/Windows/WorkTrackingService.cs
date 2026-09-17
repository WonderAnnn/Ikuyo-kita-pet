using System.Globalization;
using IkuyoPet.Core.Storage;
using IkuyoPet.Core.WorkTracking;

namespace IkuyoPet.Infrastructure.Windows;

public sealed class WorkTrackingService
{
    private readonly IActivityProbe _probe;
    private readonly IEventRepository _repository;
    private readonly TimeSpan _idleLimit;
    private readonly Func<string, string?> _displayNameResolver;
    private readonly TimeSpan _maxSampleGap;
    private ActivitySample? _previous;
    private DateTimeOffset? _sessionStartedAt;
    private string? _processName;
    private int _activeSeconds;

    public WorkTrackingService(
        IActivityProbe probe,
        IEventRepository repository,
        TimeSpan? idleLimit = null,
        Func<string, string?>? displayNameResolver = null,
        TimeSpan? maxSampleGap = null)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(repository);
        _probe = probe;
        _repository = repository;
        _idleLimit = idleLimit ?? TimeSpan.FromMinutes(5);
        _displayNameResolver = displayNameResolver ?? (processName => processName);
        _maxSampleGap = maxSampleGap ?? TimeSpan.FromMinutes(2);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_idleLimit, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_maxSampleGap, TimeSpan.Zero);
    }

    public ActivitySample? LastSample => _previous;

    public async Task<ActiveWorkDelta> SampleOnceAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = _probe.Capture();
        var activeSeconds = 0;
        LogSample(current, 0);

        if (_previous is { } previous && IsValidInterval(previous, current))
        {
            if (_sessionStartedAt is null)
            {
                _sessionStartedAt = previous.ObservedAt;
                _processName = previous.AppName;
            }

            var elapsed = current.ObservedAt - previous.ObservedAt;
            activeSeconds = (int)Math.Floor(elapsed.TotalSeconds);
            _activeSeconds = checked(_activeSeconds + activeSeconds);
            LogSample(current, activeSeconds);

            if (!string.Equals(previous.AppName, current.AppName, StringComparison.OrdinalIgnoreCase))
            {
                await FlushAsync(current.ObservedAt, "app-switched", cancellationToken);
                _sessionStartedAt = current.ObservedAt;
                _processName = current.AppName;
            }
        }
        else if (_sessionStartedAt is not null && _previous is { } lastCounted)
        {
            await FlushAsync(
                lastCounted.ObservedAt,
                GetEndReason(lastCounted, current),
                cancellationToken);
        }

        _previous = current;
        return new ActiveWorkDelta(
            activeSeconds,
            activeSeconds > 0 ? current.AppName : null,
            current.ObservedAt);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_sessionStartedAt is not null && _previous is { } previous)
        {
            await FlushAsync(previous.ObservedAt, "stopped", cancellationToken);
        }

        _previous = null;
    }

    private bool IsValidInterval(ActivitySample previous, ActivitySample current)
    {
        var elapsed = current.ObservedAt - previous.ObservedAt;
        return previous.IsWhitelistedForeground &&
               current.IsWhitelistedForeground &&
               !previous.IsLocked &&
               !current.IsLocked &&
               !previous.IsFullScreen &&
               !current.IsFullScreen &&
               !previous.IsPresentationMode &&
               !current.IsPresentationMode &&
               previous.IdleTime < _idleLimit &&
               current.IdleTime < _idleLimit &&
               elapsed > TimeSpan.Zero &&
               elapsed <= _maxSampleGap;
    }

    private string GetEndReason(ActivitySample? previous, ActivitySample current)
    {
        if (previous is not null && current.ObservedAt - previous.ObservedAt > _maxSampleGap)
        {
            return "sampling-gap";
        }

        if (current.IsLocked)
        {
            return "locked";
        }

        if (current.IsPresentationMode)
        {
            return "presentation";
        }

        if (current.IsFullScreen)
        {
            return "fullscreen";
        }

        if (current.IdleTime >= _idleLimit)
        {
            return "idle";
        }

        if (previous is not null && previous.AppName != current.AppName)
        {
            return "app-switched";
        }

        return "invalid-sample";
    }

    /// <summary>
    /// Optional per-sample diagnostics: set IKUYO_PET_SAMPLE_LOG=1 before launch to
    /// record every foreground sample under %LOCALAPPDATA%/IkuyoPet/debug-samples.log.
    /// </summary>
    private void LogSample(ActivitySample sample, int activeSeconds)
    {
        if (Environment.GetEnvironmentVariable("IKUYO_PET_SAMPLE_LOG") != "1") return;
        try
        {
            var line = string.Create(CultureInfo.InvariantCulture,
                $"{sample.ObservedAt:O} | app={sample.AppName} | wl={sample.IsWhitelistedForeground} | locked={sample.IsLocked} | fs={sample.IsFullScreen} | pres={sample.IsPresentationMode} | idleS={(int)sample.IdleTime.TotalSeconds} | deltaS={activeSeconds} | sessionS={_activeSeconds}");
            File.AppendAllText(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "IkuyoPet",
                    "debug-samples.log"),
                line + Environment.NewLine);
        }
        catch (IOException)
        {
            // Diagnostics must never break sampling.
        }
        catch (UnauthorizedAccessException)
        {
            // Diagnostics must never break sampling.
        }
    }

    private async Task FlushAsync(
        DateTimeOffset endedAt,
        string endReason,
        CancellationToken cancellationToken)
    {
        if (_sessionStartedAt is not { } startedAt || _processName is not { } processName)
        {
            return;
        }

        if (_activeSeconds > 0)
        {
            await _repository.AppendWorkSessionAsync(
                new WorkSession(
                    Guid.NewGuid(),
                    processName,
                    _displayNameResolver(processName) ?? processName,
                    startedAt,
                    endedAt,
                    _activeSeconds,
                    endReason),
                cancellationToken);
        }

        _sessionStartedAt = null;
        _processName = null;
        _activeSeconds = 0;
    }
}
