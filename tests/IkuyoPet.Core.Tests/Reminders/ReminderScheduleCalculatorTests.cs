using IkuyoPet.Core.Presentation;
using IkuyoPet.Core.Reminders;
using Xunit;

namespace IkuyoPet.Core.Tests.Reminders;

public sealed class ReminderScheduleCalculatorTests
{
    [Fact]
    public void ReturnsDueImmediatelyInsideEnabledWindowWhenNeverDisplayed()
    {
        var rule = CreateRule(
            startLocal: new TimeOnly(9, 0),
            endLocal: new TimeOnly(18, 0),
            intervalMinutes: 45);
        var now = new DateTimeOffset(2026, 9, 7, 10, 15, 0, TimeSpan.FromHours(8));

        var due = ReminderScheduleCalculator.GetDue(rule, now, lastDisplayedAt: null);

        Assert.NotNull(due);
        Assert.Equal(rule.Kind, due.Kind);
        Assert.Equal(rule.Message, due.Message);
        Assert.Equal(
            [ReminderAction.Complete, ReminderAction.Snooze, ReminderAction.Skip],
            due.Actions.Select(option => option.Action));
    }

    [Fact]
    public void UsesOffsetClockTimeInsteadOfComputerTimeZone()
    {
        var rule = CreateRule(
            startLocal: new TimeOnly(8, 30),
            endLocal: new TimeOnly(9, 30),
            intervalMinutes: 30);
        var now = new DateTimeOffset(2026, 9, 7, 9, 0, 0, TimeSpan.FromHours(-7));

        var due = ReminderScheduleCalculator.GetDue(rule, now, lastDisplayedAt: null);

        Assert.NotNull(due);
    }

    [Theory]
    [InlineData(23, 30, true)]
    [InlineData(1, 30, true)]
    [InlineData(12, 0, false)]
    public void SupportsWindowsThatCrossMidnight(int hour, int minute, bool expectedDue)
    {
        var rule = CreateRule(
            startLocal: new TimeOnly(22, 0),
            endLocal: new TimeOnly(2, 0),
            intervalMinutes: 30);
        var now = new DateTimeOffset(2026, 9, 7, hour, minute, 0, TimeSpan.FromHours(8));

        var due = ReminderScheduleCalculator.GetDue(rule, now, lastDisplayedAt: null);

        Assert.Equal(expectedDue, due is not null);
    }

    [Fact]
    public void ReturnsNullBeforeIntervalHasElapsed()
    {
        var rule = CreateRule(
            startLocal: new TimeOnly(9, 0),
            endLocal: new TimeOnly(18, 0),
            intervalMinutes: 45);
        var now = new DateTimeOffset(2026, 9, 7, 10, 15, 0, TimeSpan.FromHours(8));

        var due = ReminderScheduleCalculator.GetDue(
            rule,
            now,
            lastDisplayedAt: now.AddMinutes(-44));

        Assert.Null(due);
    }

    [Fact]
    public void ReturnsDueWhenIntervalHasElapsed()
    {
        var rule = CreateRule(
            startLocal: new TimeOnly(9, 0),
            endLocal: new TimeOnly(18, 0),
            intervalMinutes: 45);
        var now = new DateTimeOffset(2026, 9, 7, 10, 15, 0, TimeSpan.FromHours(8));

        var due = ReminderScheduleCalculator.GetDue(
            rule,
            now,
            lastDisplayedAt: now.AddMinutes(-45));

        Assert.NotNull(due);
    }

    [Theory]
    [InlineData(false, 30)]
    [InlineData(true, 0)]
    [InlineData(true, -1)]
    public void ReturnsNullForDisabledOrInvalidRules(bool enabled, int intervalMinutes)
    {
        var rule = CreateRule(
            startLocal: new TimeOnly(9, 0),
            endLocal: new TimeOnly(18, 0),
            intervalMinutes: intervalMinutes,
            enabled: enabled);
        var now = new DateTimeOffset(2026, 9, 7, 10, 15, 0, TimeSpan.FromHours(8));

        var due = ReminderScheduleCalculator.GetDue(rule, now, lastDisplayedAt: null);

        Assert.Null(due);
    }

    private static ReminderRule CreateRule(
        TimeOnly startLocal,
        TimeOnly endLocal,
        int intervalMinutes,
        bool enabled = true) => new(
            Guid.NewGuid(),
            "activity",
            "起来走一走吧",
            startLocal,
            endLocal,
            intervalMinutes,
            enabled);
}
