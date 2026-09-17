using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace IkuyoPet.Core.Reminders;

/// <summary>
/// Editable string form of a reminder rule for the settings UI. Parsing and
/// validation live here so the app can never persist a rule the schedulers
/// would reject (the schedulers throw on interval ranges below 1).
/// </summary>
public sealed class ReminderRuleDraft : INotifyPropertyChanged
{
    public const int IntervalLowerBound = 1;
    public const int IntervalUpperBound = 1440;
    public const int ActivityDurationUpperBound = 240;

    private static readonly string[] AcceptedTimeFormats = ["HH:mm"];

    private string message;
    private bool enabled;
    private string startLocal;
    private string endLocal;
    private string intervalMinMinutes;
    private string intervalMaxMinutes;
    private string activityDurationMinutes;
    private bool quietEnabled;
    private string quietStartLocal;
    private string quietEndLocal;

    public ReminderRuleDraft(Guid ruleId, string kind, ReminderRule? source = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        RuleId = ruleId;
        Kind = kind;
        source ??= CreateDefaultSource(ruleId, kind);
        message = source.Message;
        enabled = source.Enabled;
        startLocal = FormatTime(source.StartLocal);
        endLocal = FormatTime(source.EndLocal);
        intervalMinMinutes = source.IntervalMinMinutes.ToString(CultureInfo.InvariantCulture);
        intervalMaxMinutes = source.IntervalMaxMinutes.ToString(CultureInfo.InvariantCulture);
        activityDurationMinutes = ReminderKinds.IsWallClock(kind)
            ? string.Empty
            : source.ActivityDurationMinutes.ToString(CultureInfo.InvariantCulture);
        quietEnabled = source.QuietHours.Enabled;
        quietStartLocal = FormatTime(source.QuietHours.StartLocalTime);
        quietEndLocal = FormatTime(source.QuietHours.EndLocalTime);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid RuleId { get; }

    public string Kind { get; }

    public bool IsWallClock => ReminderKinds.IsWallClock(Kind);

    public string Message
    {
        get => message;
        set => SetField(ref message, value);
    }

    public bool Enabled
    {
        get => enabled;
        set => SetField(ref enabled, value);
    }

    public string StartLocal
    {
        get => startLocal;
        set => SetField(ref startLocal, value);
    }

    public string EndLocal
    {
        get => endLocal;
        set => SetField(ref endLocal, value);
    }

    public string IntervalMinMinutes
    {
        get => intervalMinMinutes;
        set => SetField(ref intervalMinMinutes, value);
    }

    public string IntervalMaxMinutes
    {
        get => intervalMaxMinutes;
        set => SetField(ref intervalMaxMinutes, value);
    }

    public string ActivityDurationMinutes
    {
        get => activityDurationMinutes;
        set => SetField(ref activityDurationMinutes, value);
    }

    public bool QuietEnabled
    {
        get => quietEnabled;
        set => SetField(ref quietEnabled, value);
    }

    public string QuietStartLocal
    {
        get => quietStartLocal;
        set => SetField(ref quietStartLocal, value);
    }

    public string QuietEndLocal
    {
        get => quietEndLocal;
        set => SetField(ref quietEndLocal, value);
    }

    /// <summary>
    /// Validates the draft. Returns human-readable problems prefixed by the
    /// given label; an empty list means Build will succeed.
    /// </summary>
    public IReadOnlyList<string> Validate(string label)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(message))
        {
            problems.Add($"{label}提醒文案不能为空");
        }

        if (!TryParseTime(startLocal, out _))
        {
            problems.Add($"{label}生效开始时间格式应为 HH:mm");
        }

        if (!TryParseTime(endLocal, out _))
        {
            problems.Add($"{label}生效结束时间格式应为 HH:mm");
        }

        if (TryParseTime(startLocal, out var start) &&
            TryParseTime(endLocal, out var end) &&
            start == end)
        {
            problems.Add($"{label}生效时段起止不能相同");
        }

        var intervalMin = ParseInterval(intervalMinMinutes);
        var intervalMax = ParseInterval(intervalMaxMinutes);
        if (intervalMin is null)
        {
            problems.Add($"{label}最小间隔应为 {IntervalLowerBound}–{IntervalUpperBound} 的整数分钟");
        }

        if (intervalMax is null)
        {
            problems.Add($"{label}最大间隔应为 {IntervalLowerBound}–{IntervalUpperBound} 的整数分钟");
        }

        if (intervalMin is int min && intervalMax is int max && min > max)
        {
            problems.Add($"{label}最小间隔不能大于最大间隔");
        }

        if (IsWallClock)
        {
            if (!string.IsNullOrWhiteSpace(activityDurationMinutes))
            {
                problems.Add($"{label}是喝水提醒，不需要活动时长");
            }
        }
        else
        {
            var duration = ParseActivityDuration(activityDurationMinutes);
            if (duration is null)
            {
                problems.Add($"{label}活动时长应为 {IntervalLowerBound}–{ActivityDurationUpperBound} 的整数分钟");
            }
        }

        if (quietEnabled)
        {
            var quietStartIsValid = TryParseTime(quietStartLocal, out var quietStart);
            var quietEndIsValid = TryParseTime(quietEndLocal, out var quietEnd);
            if (!quietStartIsValid)
            {
                problems.Add($"{label}免打扰开始时间格式应为 HH:mm");
            }

            if (!quietEndIsValid)
            {
                problems.Add($"{label}免打扰结束时间格式应为 HH:mm");
            }

            if (quietStartIsValid && quietEndIsValid && quietStart == quietEnd)
            {
                problems.Add($"{label}免打扰时段起止不能相同");
            }
        }

        return problems;
    }

    /// <summary>
    /// Produces the persisted rule. ParameterSource is set to "user" with a
    /// date version so the diagnostics panel can tell manual edits from the
    /// bundled general defaults.
    /// </summary>
    public ReminderRule Build(string parameterVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterVersion);
        var source = CreateDefaultSource(RuleId, Kind);
        var problems = Validate(Kind);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(string.Join("；", problems));
        }

        return new ReminderRule(
            RuleId,
            Kind,
            message.Trim(),
            ParseTimeStrict(startLocal),
            ParseTimeStrict(endLocal),
            ParseInterval(intervalMinMinutes)!.Value,
            enabled)
        {
            DailyGoal = source.DailyGoal,
            IntervalMinMinutes = ParseInterval(intervalMinMinutes)!.Value,
            IntervalMaxMinutes = ParseInterval(intervalMaxMinutes)!.Value,
            ActivityDurationMinutes = IsWallClock
                ? 0
                : ParseActivityDuration(activityDurationMinutes)!.Value,
            QuietHours = quietEnabled
                ? new QuietHours(
                    ParseTimeStrict(quietStartLocal),
                    ParseTimeStrict(quietEndLocal),
                    true)
                : QuietHours.Disabled,
            ParameterSource = "user",
            ParameterVersion = parameterVersion,
        };
    }

    public static bool TryParseTime(string? text, out TimeOnly value) =>
        TimeOnly.TryParseExact(
            text?.Trim(),
            AcceptedTimeFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out value);

    public static int? ParseInterval(string? text)
    {
        if (!int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        return value is >= IntervalLowerBound and <= IntervalUpperBound ? value : null;
    }

    public static int? ParseActivityDuration(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        return value is >= IntervalLowerBound and <= ActivityDurationUpperBound ? value : null;
    }

    private static TimeOnly ParseTimeStrict(string text) =>
        TryParseTime(text, out var value)
            ? value
            : throw new InvalidOperationException($"时间格式错误：{text}");

    private static string FormatTime(TimeOnly time) =>
        time.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static ReminderRule CreateDefaultSource(Guid ruleId, string kind) =>
        ReminderKinds.IsWallClock(kind)
            ? ReminderRule.CreateDefaultHydration(ruleId)
            : ReminderRule.CreateDefaultActiveWork(ruleId);

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
