using IkuyoPet.Core.Presentation;
using IkuyoPet.Core.Reminders;
using Xunit;

namespace IkuyoPet.Core.Tests.Reminders;

public sealed class ReminderScheduleCalculatorTests
{
    private static TimeZoneInfo FixedPlusEightTimeZone { get; } =
        TimeZoneInfo.CreateCustomTimeZone(
            "IkuyoPet-Test-UTC+08",
            TimeSpan.FromHours(8),
            "IkuyoPet Test UTC+08",
            "IkuyoPet Test UTC+08");

    [Fact]
    public void ExposesReadableNamesAndKeepsLegacyNamesCompatible()
    {
        var rule = CreateRule(new TimeOnly(9, 0), new TimeOnly(18, 0), 45) with
        {
            DailyGoal = 8,
            QuietHours = new QuietHours(new TimeOnly(22, 0), new TimeOnly(7, 0), true),
        };

        Assert.Equal(rule.Kind, rule.Type);
        Assert.Equal(rule.StartLocal, rule.StartLocalTime);
        Assert.Equal(rule.EndLocal, rule.EndLocalTime);
        Assert.Equal(8, rule.DailyGoal);
        Assert.True(rule.QuietHours.Enabled);
    }

    [Fact]
    public void ReturnsNullInsideEnabledQuietHours()
    {
        var rule = CreateRule(TimeOnly.MinValue, TimeOnly.MaxValue, 45) with
        {
            QuietHours = new QuietHours(new TimeOnly(22, 0), new TimeOnly(7, 0), true),
        };
        var now = new DateTimeOffset(2026, 9, 7, 23, 0, 0, TimeSpan.FromHours(8));

        var due = ReminderScheduleCalculator.GetDue(rule, now, null, FixedPlusEightTimeZone);

        Assert.Null(due);
    }

    [Fact]
    public void ReturnsDueImmediatelyInsideEnabledWindowWhenNeverDisplayed()
    {
        var rule = CreateRule(
            startLocal: new TimeOnly(9, 0),
            endLocal: new TimeOnly(18, 0),
            intervalMinutes: 45);
        var now = new DateTimeOffset(2026, 9, 7, 10, 15, 0, TimeSpan.FromHours(8));

        var due = ReminderScheduleCalculator.GetDue(
            rule,
            now,
            lastDisplayedAt: null,
            FixedPlusEightTimeZone);

        Assert.NotNull(due);
        Assert.Equal(rule.Kind, due.Kind);
        Assert.Equal(rule.Message, due.Message);
        Assert.Equal(
            [ReminderAction.Complete, ReminderAction.Snooze, ReminderAction.Skip],
            due.Actions.Select(option => option.Action));
    }

    [Fact]
    public void ConvertsInstantToConfiguredLocalTimeZone()
    {
        var rule = CreateRule(
            startLocal: new TimeOnly(8, 30),
            endLocal: new TimeOnly(9, 30),
            intervalMinutes: 30);
        var now = new DateTimeOffset(2026, 9, 7, 0, 45, 0, TimeSpan.Zero);
        var due = ReminderScheduleCalculator.GetDue(
            rule,
            now,
            lastDisplayedAt: null,
            FixedPlusEightTimeZone);

        Assert.NotNull(due);
    }

    [Fact]
    public void UsesComputerLocalTimeZoneByDefault()
    {
        var now = new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
        var expectedLocal = TimeOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local).DateTime);
        var rule = CreateRule(
            startLocal: expectedLocal.AddMinutes(-1),
            endLocal: expectedLocal.AddMinutes(1),
            intervalMinutes: 30);

        var due = ReminderScheduleCalculator.GetDue(
            rule,
            now,
            lastDisplayedAt: null);

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

        var due = ReminderScheduleCalculator.GetDue(
            rule,
            now,
            lastDisplayedAt: null,
            FixedPlusEightTimeZone);

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
            lastDisplayedAt: now.AddMinutes(-44),
            FixedPlusEightTimeZone);

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
            lastDisplayedAt: now.AddMinutes(-45),
            FixedPlusEightTimeZone);

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

        var due = ReminderScheduleCalculator.GetDue(
            rule,
            now,
            lastDisplayedAt: null,
            FixedPlusEightTimeZone);

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
