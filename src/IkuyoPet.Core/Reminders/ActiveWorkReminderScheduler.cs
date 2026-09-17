namespace IkuyoPet.Core.Reminders;

public interface IReminderIntervalRandom
{
    int NextInclusive(int minimum, int maximum);
}

public interface IReminderClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class ActiveWorkReminderScheduler
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(5);
    private const int MaximumAttempts = 3;
    private readonly ReminderRule rule;
    private readonly IReminderIntervalRandom random;
    private readonly IReminderClock clock;
    private ReminderRuntimeState? state;

    public ActiveWorkReminderScheduler(
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

    public bool ObserveActiveSeconds(int seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seconds);

        var current = State;
        if (current.Status == ReminderRuntimeStatus.WaitingRetry)
        {
            if (current.RetryDueAt is null || clock.UtcNow < current.RetryDueAt.Value)
            {
                return false;
            }

            state = current with
            {
                Status = ReminderRuntimeStatus.Due,
                RetryDueAt = null,
                UpdatedAt = clock.UtcNow,
            };
            return true;
        }

        if (current.Status != ReminderRuntimeStatus.Accumulating)
        {
            return false;
        }

        var accumulated = checked(current.AccumulatedActiveSeconds + seconds);
        accumulated = Math.Min(accumulated, current.TargetActiveSeconds);
        var status = accumulated >= current.TargetActiveSeconds
            ? ReminderRuntimeStatus.Due
            : ReminderRuntimeStatus.Accumulating;
        state = current with
        {
            AccumulatedActiveSeconds = accumulated,
            Status = status,
            Attempt = status == ReminderRuntimeStatus.Due ? 1 : 0,
            UpdatedAt = clock.UtcNow,
        };
        return status == ReminderRuntimeStatus.Due;
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

    private ReminderRuntimeState StartNewCycle(DateTimeOffset now)
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

    private static void ValidateRule(ReminderRule rule)
    {
        if (rule.IntervalMinMinutes <= 0 ||
            rule.IntervalMaxMinutes < rule.IntervalMinMinutes)
        {
            throw new ArgumentException("Reminder interval range is invalid.", nameof(rule));
        }

        if (rule.ActivityDurationMinutes <= 0)
        {
            throw new ArgumentException("Activity duration must be positive.", nameof(rule));
        }
    }
}
