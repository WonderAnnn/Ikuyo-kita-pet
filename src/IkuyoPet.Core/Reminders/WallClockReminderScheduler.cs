namespace IkuyoPet.Core.Reminders;

/// <summary>
/// Schedules reminders by elapsed wall-clock time rather than foreground work time.
/// The persisted runtime state intentionally reuses the existing second counters so
/// older SQLite databases can be upgraded without a schema change.
/// </summary>
public sealed class WallClockReminderScheduler
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(5);
    private const int MaximumAttempts = 3;
    private readonly ReminderRule rule;
    private readonly IReminderIntervalRandom random;
    private readonly IReminderClock clock;
    private ReminderRuntimeState? state;

    public WallClockReminderScheduler(
        ReminderRule rule,
        IReminderIntervalRandom random,
        ReminderRuntimeState? state,
        IReminderClock clock)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(clock);
        ValidateRule(rule);
        if (state is not null && state.RuleId != rule.Id)
        {
            throw new ArgumentException("Runtime state belongs to another rule.", nameof(state));
        }

        this.rule = rule;
        this.random = random;
        this.clock = clock;
        this.state = state;
    }

    public ReminderRuntimeState State => state ??
        throw new InvalidOperationException("No reminder cycle has been started.");

    public ReminderRuntimeState StartNewCycle() => StartNewCycle(clock.UtcNow);

    /// <summary>
    /// Advances the current cycle by the portion of elapsed time that falls inside
    /// the rule's daily window. Suppression/lock handling is intentionally owned by
    /// the caller: when the loop resumes, the persisted UpdatedAt lets this method
    /// catch up the elapsed window time in one pass.
    /// </summary>
    public bool Observe(DateTimeOffset now, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        var current = State;
        if (current.Status == ReminderRuntimeStatus.WaitingRetry)
        {
            if (current.RetryDueAt is null || now < current.RetryDueAt.Value ||
                !IsPresentableNow(now, timeZone))
            {
                return false;
            }

            state = current with
            {
                Status = ReminderRuntimeStatus.Due,
                RetryDueAt = null,
                UpdatedAt = now,
            };
            return true;
        }

        if (current.Status != ReminderRuntimeStatus.Accumulating || now <= current.UpdatedAt)
        {
            return false;
        }

        var elapsedSeconds = CalculateWindowSeconds(
            current.UpdatedAt,
            now,
            rule,
            timeZone);
        var accumulated = Math.Min(
            current.TargetActiveSeconds,
            checked(current.AccumulatedActiveSeconds + elapsedSeconds));
        var status = accumulated >= current.TargetActiveSeconds
            ? ReminderRuntimeStatus.Due
            : ReminderRuntimeStatus.Accumulating;
        state = current with
        {
            AccumulatedActiveSeconds = accumulated,
            Status = status,
            Attempt = status == ReminderRuntimeStatus.Due ? 1 : 0,
            UpdatedAt = now,
        };
        return status == ReminderRuntimeStatus.Due && IsPresentableNow(now, timeZone);
    }

    public ReminderRuntimeState Apply(ReminderAction action, DateTimeOffset now)
    {
        var current = State;
        if (current.Status != ReminderRuntimeStatus.Due)
        {
            throw new InvalidOperationException("An action can only be applied to a due reminder.");
        }

        switch (action)
        {
            case ReminderAction.Complete:
            case ReminderAction.Skip:
                return StartNewCycle(now);
            case ReminderAction.Snooze:
                state = current with
                {
                    Status = ReminderRuntimeStatus.WaitingRetry,
                    Attempt = current.Attempt + 1,
                    RetryDueAt = now.Add(RetryDelay),
                    UpdatedAt = now,
                };
                return state;
            case ReminderAction.NoResponse when current.Attempt >= MaximumAttempts:
                state = current with
                {
                    Status = ReminderRuntimeStatus.Unanswered,
                    RetryDueAt = null,
                    UpdatedAt = now,
                };
                return state;
            case ReminderAction.NoResponse:
                state = current with
                {
                    Status = ReminderRuntimeStatus.WaitingRetry,
                    Attempt = current.Attempt + 1,
                    RetryDueAt = now.Add(RetryDelay),
                    UpdatedAt = now,
                };
                return state;
            default:
                throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    public bool IsPresentableNow(DateTimeOffset now, TimeZoneInfo timeZone) =>
        IsPresentableNow(now, timeZone, rule);

    internal static int CalculateWindowSeconds(
        DateTimeOffset from,
        DateTimeOffset to,
        ReminderRule rule,
        TimeZoneInfo timeZone)
    {
        if (to <= from) return 0;

        var utcFrom = from.ToUniversalTime();
        var utcTo = to.ToUniversalTime();
        var localFrom = TimeZoneInfo.ConvertTime(utcFrom, timeZone).Date;
        var localTo = TimeZoneInfo.ConvertTime(utcTo, timeZone).Date;
        long totalSeconds = 0;

        for (var date = localFrom; date <= localTo; date = date.AddDays(1))
        {
            var startLocal = date.Add(rule.StartLocal.ToTimeSpan());
            var endLocal = date.Add(rule.EndLocal.ToTimeSpan());
            if (endLocal <= startLocal)
            {
                endLocal = endLocal.AddDays(1);
            }

            var start = ToUtc(startLocal, timeZone);
            var end = ToUtc(endLocal, timeZone);
            var overlapStart = start > utcFrom ? start : utcFrom;
            var overlapEnd = end < utcTo ? end : utcTo;
            if (overlapEnd > overlapStart)
            {
                totalSeconds += (long)(overlapEnd - overlapStart).TotalSeconds;
                if (totalSeconds >= int.MaxValue) return int.MaxValue;
            }
        }

        return (int)totalSeconds;
    }

    private static bool IsPresentableNow(
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        ReminderRule rule)
    {
        var localNow = TimeZoneInfo.ConvertTime(now, timeZone);
        var localTime = TimeOnly.FromDateTime(localNow.DateTime);
        return IsInsideWindow(localTime, rule.StartLocal, rule.EndLocal) &&
            (!rule.QuietHours.Enabled || !IsInsideWindow(
                localTime,
                rule.QuietHours.StartLocalTime,
                rule.QuietHours.EndLocalTime));
    }

    private static bool IsInsideWindow(TimeOnly current, TimeOnly start, TimeOnly end) =>
        start <= end
            ? current >= start && current <= end
            : current >= start || current <= end;

    public ReminderRuntimeState StartNewCycle(DateTimeOffset now)
    {
        var sampledMinutes = random.NextInclusive(
            rule.IntervalMinMinutes,
            rule.IntervalMaxMinutes);
        if (sampledMinutes < rule.IntervalMinMinutes || sampledMinutes > rule.IntervalMaxMinutes)
        {
            throw new InvalidOperationException(
                $"Random interval {sampledMinutes} is outside the configured inclusive range.");
        }

        state = new ReminderRuntimeState(
            rule.Id,
            Guid.NewGuid(),
            checked(sampledMinutes * 60),
            0,
            ReminderRuntimeStatus.Accumulating,
            0,
            null,
            now);
        return state;
    }

    private static DateTimeOffset ToUtc(DateTime local, TimeZoneInfo timeZone) =>
        new DateTimeOffset(local, timeZone.GetUtcOffset(local)).ToUniversalTime();

    private static void ValidateRule(ReminderRule rule)
    {
        if (rule.IntervalMinMinutes <= 0 ||
            rule.IntervalMaxMinutes < rule.IntervalMinMinutes)
        {
            throw new ArgumentException("Reminder interval range is invalid.", nameof(rule));
        }
    }
}
