using IkuyoPet.Core.Storage;
using IkuyoPet.Core.WorkTracking;

namespace IkuyoPet.Infrastructure.Windows;

public sealed class WorkTrackingService
{
    private readonly IActivityProbe _probe;
    private readonly IEventRepository _repository;
    private readonly TimeSpan _idleLimit;
    private ActivitySample? _previous;
    private DateTimeOffset? _sessionStartedAt;
    private string? _processName;
    private int _activeSeconds;

    public WorkTrackingService(
        IActivityProbe probe,
        IEventRepository repository,
        TimeSpan? idleLimit = null)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(repository);
        _probe = probe;
        _repository = repository;
        _idleLimit = idleLimit ?? TimeSpan.FromMinutes(5);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_idleLimit, TimeSpan.Zero);
    }

    public async Task SampleOnceAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = _probe.Capture();

        if (_previous is { } previous && IsValidInterval(previous, current))
        {
            if (_sessionStartedAt is null)
            {
                _sessionStartedAt = previous.ObservedAt;
                _processName = previous.AppName;
            }

            var elapsed = current.ObservedAt - previous.ObservedAt;
            _activeSeconds = checked(_activeSeconds + (int)Math.Floor(elapsed.TotalSeconds));
        }
        else if (_sessionStartedAt is not null)
        {
            await FlushAsync(current.ObservedAt, GetEndReason(_previous, current), cancellationToken);
        }

        _previous = current;
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
        return previous.AppName == current.AppName &&
               previous.IsWhitelistedForeground &&
               current.IsWhitelistedForeground &&
               !previous.IsLocked &&
               !current.IsLocked &&
               previous.IdleTime < _idleLimit &&
               current.IdleTime < _idleLimit &&
               elapsed > TimeSpan.Zero;
    }

    private string GetEndReason(ActivitySample? previous, ActivitySample current)
    {
        if (current.IsLocked)
        {
            return "locked";
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
                    processName,
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