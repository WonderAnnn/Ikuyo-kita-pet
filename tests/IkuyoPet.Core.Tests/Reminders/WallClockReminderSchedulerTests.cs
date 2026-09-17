using IkuyoPet.Core.Reminders;
using Xunit;

namespace IkuyoPet.Core.Tests.Reminders;

public sealed class WallClockReminderSchedulerTests
{
    private static readonly TimeZoneInfo ChinaStandardTime = TimeZoneInfo.CreateCustomTimeZone(
        "IkuyoPet-China-UTC+08",
        TimeSpan.FromHours(8),
        "Ikuyo Pet China Standard Time",
        "Ikuyo Pet China Standard Time");

    [Fact]
    public void DefaultHydrationRuleUsesTheIndependentWaterDefaults()
    {
        var rule = ReminderRule.CreateDefaultHydration(Guid.NewGuid());

        Assert.Equal("water", rule.Kind);
        Assert.Equal(new TimeOnly(8, 0), rule.StartLocal);
        Assert.Equal(new TimeOnly(23, 0), rule.EndLocal);
        Assert.Equal(15, rule.IntervalMinMinutes);
        Assert.Equal(20, rule.IntervalMaxMinutes);
        Assert.Equal(0, rule.ActivityDurationMinutes);
        Assert.Contains("喝口水", rule.Message);
    }    [Fact]
    public void StartNewCycleSamplesTheConfiguredWaterInterval()
    {
        var now = At(9, 0);
        var scheduler = new WallClockReminderScheduler(
            CreateWaterRule(),
            new FixedRandom(15),
            state: null,
            new FixedClock(now));

        var state = scheduler.StartNewCycle();

        Assert.Equal(15 * 60, state.TargetActiveSeconds);
        Assert.Equal(0, state.AccumulatedActiveSeconds);
        Assert.Equal(ReminderRuntimeStatus.Accumulating, state.Status);
        Assert.Equal(now, state.UpdatedAt);
    }

    [Fact]
    public void ElapsedWallClockReachesDueWithoutActiveWorkSamples()
    {
        var started = At(9, 0);
        var clock = new FixedClock(started);
        var scheduler = new WallClockReminderScheduler(
            CreateWaterRule(),
            new FixedRandom(15),
            state: null,
            clock);
        scheduler.StartNewCycle();

        clock.UtcNow = started.AddMinutes(15);

        Assert.True(scheduler.Observe(clock.UtcNow, ChinaStandardTime));
        Assert.Equal(ReminderRuntimeStatus.Due, scheduler.State.Status);
        Assert.Equal(15 * 60, scheduler.State.AccumulatedActiveSeconds);
    }

    [Fact]
    public void TimeOutsideTheDailyWindowDoesNotCountAsWaterTime()
    {
        var started = At(22, 50);
        var clock = new FixedClock(started);
        var scheduler = new WallClockReminderScheduler(
            CreateWaterRule(intervalMin: 20, intervalMax: 20),
            new FixedRandom(20),
            state: null,
            clock);
        scheduler.StartNewCycle();

        clock.UtcNow = AtNextDay(8, 5);

        Assert.False(scheduler.Observe(clock.UtcNow, ChinaStandardTime));
        Assert.Equal(ReminderRuntimeStatus.Accumulating, scheduler.State.Status);
        Assert.Equal(15 * 60, scheduler.State.AccumulatedActiveSeconds);
    }

    private static ReminderRule CreateWaterRule(int intervalMin = 15, int intervalMax = 20) => new(
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        "water",
        "喝口水嘛～ദ്ദി˶>𖥦<)✧",
        new TimeOnly(8, 0),
        new TimeOnly(23, 0),
        intervalMin,
        true)
    {
        IntervalMinMinutes = intervalMin,
        IntervalMaxMinutes = intervalMax,
        ActivityDurationMinutes = 0,
        ParameterSource = "general-default",
        ParameterVersion = "2026-09-11",
    };

    private static DateTimeOffset At(int hour, int minute) =>
        new(2026, 9, 11, hour, minute, 0, TimeSpan.FromHours(8));

    private static DateTimeOffset AtNextDay(int hour, int minute) =>
        At(hour, minute).AddDays(1);

    private sealed class FixedRandom(int value) : IReminderIntervalRandom
    {
        public int NextInclusive(int minimum, int maximum) => value;
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IReminderClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
