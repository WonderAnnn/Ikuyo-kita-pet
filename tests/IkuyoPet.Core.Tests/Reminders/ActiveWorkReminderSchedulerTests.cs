using IkuyoPet.Core.Reminders;
using Xunit;

namespace IkuyoPet.Core.Tests.Reminders;

public sealed class ActiveWorkReminderSchedulerTests
{
    private static readonly Guid RuleId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset InitialNow = new(2026, 9, 9, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(40)]
    [InlineData(45)]
    [InlineData(50)]
    public void StartNewCycleAcceptsEveryInclusiveBoundaryAndSamplesOnce(int sampledMinutes)
    {
        var random = new StubIntervalRandom(sampledMinutes);
        var clock = new StubClock(InitialNow);
        var scheduler = new ActiveWorkReminderScheduler(CreateRule(), random, null, clock);

        var state = scheduler.StartNewCycle();
        scheduler.ObserveActiveSeconds(30);
        scheduler.ObserveActiveSeconds(30);

        Assert.Equal(sampledMinutes * 60, state.TargetActiveSeconds);
        Assert.Equal(1, random.CallCount);
        Assert.Equal(ReminderRuntimeStatus.Accumulating, scheduler.State.Status);
    }

    [Theory]
    [InlineData(39)]
    [InlineData(51)]
    public void StartNewCycleRejectsRandomValuesOutsideConfiguredRange(int sampledMinutes)
    {
        var scheduler = new ActiveWorkReminderScheduler(
            CreateRule(),
            new StubIntervalRandom(sampledMinutes),
            null,
            new StubClock(InitialNow));

        Assert.Throws<InvalidOperationException>(() => scheduler.StartNewCycle());
    }

    [Fact]
    public void ObserveActiveSecondsBecomesDueOnlyAtPersistedTarget()
    {
        var scheduler = new ActiveWorkReminderScheduler(
            CreateRule(),
            new StubIntervalRandom(40),
            null,
            new StubClock(InitialNow));
        scheduler.StartNewCycle();

        Assert.False(scheduler.ObserveActiveSeconds(2_399));
        Assert.True(scheduler.ObserveActiveSeconds(1));
        Assert.Equal(2_400, scheduler.State.AccumulatedActiveSeconds);
        Assert.Equal(ReminderRuntimeStatus.Due, scheduler.State.Status);
    }

    [Fact]
    public void RestoredStateContinuesWithoutResampling()
    {
        var restored = new ReminderRuntimeState(
            RuleId,
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            targetActiveSeconds: 2_700,
            accumulatedActiveSeconds: 1_200,
            ReminderRuntimeStatus.Accumulating,
            attempt: 0,
            retryDueAt: null,
            InitialNow);
        var random = new StubIntervalRandom(50);
        var scheduler = new ActiveWorkReminderScheduler(
            CreateRule(),
            random,
            restored,
            new StubClock(InitialNow.AddMinutes(10)));

        Assert.True(scheduler.ObserveActiveSeconds(1_500));
        Assert.Equal(0, random.CallCount);
        Assert.Equal(restored.CycleId, scheduler.State.CycleId);
        Assert.Equal(2_700, scheduler.State.TargetActiveSeconds);
    }

    [Theory]
    [InlineData(ReminderAction.Complete)]
    [InlineData(ReminderAction.Skip)]
    public void CompleteOrSkipStartsExactlyOneFreshCycle(ReminderAction action)
    {
        var random = new StubIntervalRandom(40, 50);
        var scheduler = new ActiveWorkReminderScheduler(
            CreateRule(),
            random,
            null,
            new StubClock(InitialNow));
        var first = scheduler.StartNewCycle();
        scheduler.ObserveActiveSeconds(first.TargetActiveSeconds);

        var next = scheduler.Apply(action, InitialNow.AddMinutes(40));

        Assert.NotEqual(first.CycleId, next.CycleId);
        Assert.Equal(3_000, next.TargetActiveSeconds);
        Assert.Equal(0, next.AccumulatedActiveSeconds);
        Assert.Equal(ReminderRuntimeStatus.Accumulating, next.Status);
        Assert.Equal(2, random.CallCount);
    }

    [Fact]
    public void SnoozeSchedulesFiveMinuteRetry()
    {
        var scheduler = CreateDueScheduler(attempt: 1);
        var actionAt = InitialNow.AddMinutes(40);

        var state = scheduler.Apply(ReminderAction.Snooze, actionAt);

        Assert.Equal(ReminderRuntimeStatus.WaitingRetry, state.Status);
        Assert.Equal(2, state.Attempt);
        Assert.Equal(actionAt.AddMinutes(5), state.RetryDueAt);
    }

    [Fact]
    public void ThirdNoResponseEndsAsUnanswered()
    {
        var scheduler = CreateDueScheduler(attempt: 3);
        var actionAt = InitialNow.AddMinutes(50);

        var state = scheduler.Apply(ReminderAction.NoResponse, actionAt);

        Assert.Equal(ReminderRuntimeStatus.Unanswered, state.Status);
        Assert.Equal(3, state.Attempt);
        Assert.Null(state.RetryDueAt);
    }

    private static ActiveWorkReminderScheduler CreateDueScheduler(int attempt)
    {
        var state = new ReminderRuntimeState(
            RuleId,
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            2_400,
            2_400,
            ReminderRuntimeStatus.Due,
            attempt,
            null,
            InitialNow);
        return new ActiveWorkReminderScheduler(
            CreateRule(),
            new StubIntervalRandom(45),
            state,
            new StubClock(InitialNow));
    }

    private static ReminderRule CreateRule() => new(
        RuleId,
        "activity",
        "离屏活动一下吧",
        new TimeOnly(8, 0),
        new TimeOnly(23, 0),
        45,
        true)
    {
        IntervalMinMinutes = 40,
        IntervalMaxMinutes = 50,
        ActivityDurationMinutes = 5,
        ParameterSource = "general-default",
        ParameterVersion = "2026-09-09",
    };

    private sealed class StubIntervalRandom(params int[] values) : IReminderIntervalRandom
    {
        private readonly Queue<int> values = new(values);

        public int CallCount { get; private set; }

        public int NextInclusive(int minimum, int maximum)
        {
            CallCount++;
            return values.Dequeue();
        }
    }

    private sealed class StubClock(DateTimeOffset now) : IReminderClock
    {
        public DateTimeOffset UtcNow { get; set; } = now;
    }
}
