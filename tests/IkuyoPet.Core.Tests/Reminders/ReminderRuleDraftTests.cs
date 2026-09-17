using IkuyoPet.Core.Reminders;
using Xunit;

namespace IkuyoPet.Core.Tests.Reminders;

public sealed class ReminderRuleDraftTests
{
    [Fact]
    public void BuildProducesUserSourcedRuleFromEditedValues()
    {
        var draft = new ReminderRuleDraft(DefaultReminderRuleIds.Activity, "activity");
        draft.Message = "医生建议活动一下";
        draft.IntervalMinMinutes = "30";
        draft.IntervalMaxMinutes = "40";
        draft.ActivityDurationMinutes = "10";
        draft.StartLocal = "09:30";
        draft.EndLocal = "22:00";
        draft.QuietEnabled = true;
        draft.QuietStartLocal = "13:00";
        draft.QuietEndLocal = "14:00";

        var rule = draft.Build("2026-09-11");

        Assert.Equal("医生建议活动一下", rule.Message);
        Assert.Equal(30, rule.IntervalMinMinutes);
        Assert.Equal(40, rule.IntervalMaxMinutes);
        Assert.Equal(30, rule.IntervalMinutes);
        Assert.Equal(10, rule.ActivityDurationMinutes);
        Assert.Equal(new TimeOnly(9, 30), rule.StartLocal);
        Assert.Equal(new TimeOnly(22, 0), rule.EndLocal);
        Assert.True(rule.QuietHours.Enabled);
        Assert.Equal(new TimeOnly(13, 0), rule.QuietHours.StartLocalTime);
        Assert.Equal(new TimeOnly(14, 0), rule.QuietHours.EndLocalTime);
        Assert.Equal("user", rule.ParameterSource);
        Assert.Equal("2026-09-11", rule.ParameterVersion);
        Assert.Equal(DefaultReminderRuleIds.Activity, rule.Id);
    }

    [Fact]
    public void BuildRejectsInvalidValuesWithHumanReadableProblems()
    {
        var draft = new ReminderRuleDraft(DefaultReminderRuleIds.Hydration, "water");
        draft.Message = " ";
        draft.StartLocal = "9点";
        draft.EndLocal = "23:00";
        draft.IntervalMinMinutes = "0";
        draft.IntervalMaxMinutes = "abc";
        draft.QuietEnabled = true;
        draft.QuietStartLocal = "22:00";
        draft.QuietEndLocal = "bad";

        var problems = draft.Validate("喝水");

        Assert.Contains(problems, problem => problem.Contains("提醒文案不能为空"));
        Assert.Contains(problems, problem => problem.Contains("生效开始时间格式"));
        Assert.Contains(problems, problem => problem.Contains("最小间隔"));
        Assert.Contains(problems, problem => problem.Contains("最大间隔"));
        Assert.Contains(problems, problem => problem.Contains("免打扰结束时间格式"));
        Assert.Throws<InvalidOperationException>(() => draft.Build("2026-09-11"));
    }

    [Fact]
    public void ValidateRejectsReversedIntervalRangeAndEqualWindowBounds()
    {
        var draft = new ReminderRuleDraft(DefaultReminderRuleIds.Activity, "activity");
        draft.IntervalMinMinutes = "45";
        draft.IntervalMaxMinutes = "30";
        draft.StartLocal = "08:00";
        draft.EndLocal = "08:00";

        var problems = draft.Validate("活动");

        Assert.Contains(problems, problem => problem.Contains("最小间隔不能大于最大间隔"));
        Assert.Contains(problems, problem => problem.Contains("生效时段起止不能相同"));
    }

    [Fact]
    public void WallClockDraftRejectsActivityDuration()
    {
        var draft = new ReminderRuleDraft(DefaultReminderRuleIds.Hydration, "water");
        draft.ActivityDurationMinutes = "5";

        var problems = draft.Validate("喝水");

        Assert.Contains(problems, problem => problem.Contains("不需要活动时长"));
    }

    [Fact]
    public void BuildStoresZeroActivityDurationForWallClockRules()
    {
        var draft = new ReminderRuleDraft(DefaultReminderRuleIds.Hydration, "water");
        draft.ActivityDurationMinutes = "";

        var rule = draft.Build("2026-09-11");

        Assert.Equal(0, rule.ActivityDurationMinutes);
    }

    [Theory]
    [InlineData("08:05", true)]
    [InlineData("8:05", false)]
    [InlineData("24:00", false)]
    [InlineData("08:5", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryParseTimeAcceptsOnlyHourTwoDigitMinute(string? text, bool expected)
    {
        Assert.Equal(expected, ReminderRuleDraft.TryParseTime(text, out _));
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("720", 720)]
    [InlineData("1440", 1440)]
    [InlineData("0", null)]
    [InlineData("1441", null)]
    [InlineData("-5", null)]
    [InlineData("15分钟", null)]
    public void ParseIntervalEnforcesBounds(string text, int? expected)
    {
        Assert.Equal(expected, ReminderRuleDraft.ParseInterval(text));
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("240", 240)]
    [InlineData("0", null)]
    [InlineData("241", null)]
    [InlineData("5.5", null)]
    public void ParseActivityDurationEnforcesBounds(string text, int? expected)
    {
        Assert.Equal(expected, ReminderRuleDraft.ParseActivityDuration(text));
    }

    [Theory]
    [InlineData("water", "0", "20", "最小间隔")]
    [InlineData("water", "15", "1441", "最大间隔")]
    [InlineData("activity", "0", "50", "最小间隔")]
    [InlineData("activity", "40", "1441", "最大间隔")]
    public void ValidateAppliesIntervalBoundsToBothRuleKinds(
        string kind,
        string minimum,
        string maximum,
        string expectedProblem)
    {
        var ruleId = kind == "water" ? DefaultReminderRuleIds.Hydration : DefaultReminderRuleIds.Activity;
        var draft = new ReminderRuleDraft(ruleId, kind)
        {
            IntervalMinMinutes = minimum,
            IntervalMaxMinutes = maximum,
        };

        var problems = draft.Validate(kind == "water" ? "喝水" : "活动");

        Assert.Contains(problems, problem => problem.Contains(expectedProblem));
    }

    [Fact]
    public void ValidateRejectsEqualQuietWindowButAllowsOvernightWindow()
    {
        var draft = new ReminderRuleDraft(DefaultReminderRuleIds.Hydration, "water")
        {
            QuietEnabled = true,
            QuietStartLocal = "22:00",
            QuietEndLocal = "22:00",
        };

        Assert.Contains(
            draft.Validate("喝水"),
            problem => problem.Contains("免打扰时段起止不能相同"));

        draft.QuietEndLocal = "07:00";
        Assert.DoesNotContain(
            draft.Validate("喝水"),
            problem => problem.Contains("免打扰时段"));
    }

    [Fact]
    public void BuildIgnoresQuietTimesWhenQuietHoursAreDisabled()
    {
        var draft = new ReminderRuleDraft(DefaultReminderRuleIds.Activity, "activity")
        {
            QuietEnabled = false,
            QuietStartLocal = "not-a-time",
            QuietEndLocal = "also-invalid",
        };

        var rule = draft.Build("2026-09-12");

        Assert.False(rule.QuietHours.Enabled);
    }

    [Fact]
    public void DraftRoundTripsPersistedRuleValues()
    {
        var source = ReminderRule.CreateDefaultHydration(DefaultReminderRuleIds.Hydration) with
        {
            Enabled = false,
            IntervalMinMinutes = 20,
            IntervalMaxMinutes = 25,
            QuietHours = new QuietHours(new TimeOnly(12, 0), new TimeOnly(13, 30), true),
        };

        var draft = new ReminderRuleDraft(source.Id, source.Kind, source);

        Assert.Equal(source.Message, draft.Message);
        Assert.False(draft.Enabled);
        Assert.Equal("20", draft.IntervalMinMinutes);
        Assert.Equal("25", draft.IntervalMaxMinutes);
        Assert.Equal("08:00", draft.StartLocal);
        Assert.Equal("23:00", draft.EndLocal);
        Assert.True(draft.QuietEnabled);
        Assert.Equal("12:00", draft.QuietStartLocal);
        Assert.Equal("13:30", draft.QuietEndLocal);
        Assert.True(draft.IsWallClock);
    }
}
