using IkuyoPet.Core.Reminders;
using IkuyoPet.Core.Storage;
using IkuyoPet.Core.WorkTracking;

namespace IkuyoPet.Core.Diagnostics;

/// <summary>
/// Assembles the state diagnosis shown in the app: whether work time is
/// accumulating, how far each reminder cycle has progressed, when the next
/// reminder is expected, and concrete reasons when nothing is happening.
/// All inputs are injectable so tests can drive every branch.
/// </summary>
public sealed class StatusDiagnosticsService(
    IEventRepository repository,
    IActivityProbe probe,
    TimeZoneInfo? timeZone = null,
    TimeProvider? timeProvider = null,
    Func<bool>? isPetEnabled = null,
    Func<bool>? isPaused = null,
    Func<DateTimeOffset?>? getPausedUntil = null,
    TimeSpan? idleLimit = null)
{
    private readonly IEventRepository repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly IActivityProbe probe = probe ?? throw new ArgumentNullException(nameof(probe));
    private readonly TimeZoneInfo timeZone = timeZone ?? TimeZoneInfo.Local;
    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;
    private readonly Func<bool> isPetEnabled = isPetEnabled ?? (() => true);
    private readonly Func<bool> isPaused = isPaused ?? (() => false);
    private readonly Func<DateTimeOffset?> getPausedUntil = getPausedUntil ?? (() => null);
    private readonly TimeSpan idleLimit = idleLimit ?? TimeSpan.FromMinutes(5);

    public async Task<DiagnosticsReport> GetReportAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var sample = probe.Capture();
        var rules = await repository.ReadReminderRulesAsync(cancellationToken).ConfigureAwait(false);
        var applications = await repository.ReadTrackedApplicationsAsync(cancellationToken).ConfigureAwait(false);
        var whitelist = applications
            .Where(application => application.Enabled)
            .Select(application => application.ProcessName)
            .ToArray();

        var workTracking = DescribeWorkTracking(sample, whitelist);
        var ruleEntries = new List<RuleDiagnostics>();
        foreach (var rule in rules.OrderBy(static candidate => ReminderKinds.IsWallClock(candidate.Kind) ? 0 : 1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ruleEntries.Add(await DescribeRuleAsync(rule, now, sample, cancellationToken).ConfigureAwait(false));
        }

        return new DiagnosticsReport(
            now,
            isPetEnabled() ? "透明桌宠提醒" : "Windows 通知提醒",
            DescribePause(now),
            sample.IsFullScreen || sample.IsPresentationMode || sample.IsLocked
                ? "全屏 / 演示 / 锁屏中，提醒与计时暂缓"
                : "正常呈现",
            workTracking,
            ruleEntries);
    }

    private WorkTrackingStatus DescribeWorkTracking(
        ActivitySample sample,
        IReadOnlyList<string> whitelist)
    {
        var hasForeground = !string.IsNullOrWhiteSpace(sample.AppName);
        var reasons = new List<string>();
        if (!hasForeground)
        {
            reasons.Add("当前读不到前台窗口，暂不计时");
        }
        else if (sample.IsWhitelistedForeground)
        {
            reasons.Add($"前台是白名单应用 {sample.AppName}，满足计时条件时持续累计");
        }
        else
        {
            reasons.Add(
                $"当前前台是 {sample.AppName}，不在白名单（{DescribeWhitelist(whitelist)}），不会计时");
        }

        if (sample.IsLocked)
        {
            reasons.Add("电脑处于锁屏状态，锁屏时间不计时");
        }

        if (sample.IsFullScreen)
        {
            reasons.Add("前台应用处于全屏状态，不计时");
        }

        if (sample.IsPresentationMode)
        {
            reasons.Add("系统处于演示模式，不计时");
        }

        if (sample.IdleTime >= idleLimit)
        {
            reasons.Add($"已空闲 {FormatDuration(sample.IdleTime)}，超过 {FormatDuration(idleLimit)} 无操作不计时");
        }
        else if (hasForeground && !sample.IsLocked && sample.IsWhitelistedForeground)
        {
            reasons.Add("最近有键盘或鼠标操作");
        }

        var canAccumulate = hasForeground &&
            sample.IsWhitelistedForeground &&
            !sample.IsLocked &&
            !sample.IsFullScreen &&
            !sample.IsPresentationMode &&
            sample.IdleTime < idleLimit;
        return new WorkTrackingStatus(
            hasForeground ? sample.AppName : null,
            sample.IsWhitelistedForeground,
            sample.IsLocked,
            sample.IsFullScreen,
            sample.IsPresentationMode,
            sample.IdleTime == TimeSpan.MaxValue
                ? -1
                : (int)sample.IdleTime.TotalSeconds,
            canAccumulate,
            reasons);
    }

    private async Task<RuleDiagnostics> DescribeRuleAsync(
        ReminderRule rule,
        DateTimeOffset now,
        ActivitySample sample,
        CancellationToken cancellationToken)
    {
        var kindText = ReminderKinds.ToDisplayText(rule.Kind);
        var isWallClock = ReminderKinds.IsWallClock(rule.Kind);
        if (!rule.Enabled)
        {
            return new RuleDiagnostics(
                rule.Id,
                rule.Kind,
                kindText,
                false,
                isWallClock,
                "已停用",
                "停用期间不会提醒",
                ["规则已停用，重新启用后才会提醒"]);
        }

        var reasons = new List<string>();
        var localTime = TimeOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(now, timeZone).DateTime);
        if (!IsInsideWindow(localTime, rule.StartLocal, rule.EndLocal))
        {
            reasons.Add($"当前 {localTime:HH:mm} 不在生效时段 {rule.StartLocal:HH:mm}–{rule.EndLocal:HH:mm} 内");
        }

        if (rule.QuietHours.Enabled &&
            IsInsideWindow(localTime, rule.QuietHours.StartLocalTime, rule.QuietHours.EndLocalTime))
        {
            reasons.Add(
                $"处于免打扰时段 {rule.QuietHours.StartLocalTime:HH:mm}–{rule.QuietHours.EndLocalTime:HH:mm}，不会提醒");
        }

        ReminderRuntimeState? runtime = null;
        try
        {
            runtime = await repository.ReadReminderRuntimeStateAsync(rule.Id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (NotSupportedException)
        {
            // Legacy repositories do not persist runtime state.
        }

        var state = DescribeRuntime(rule, runtime, now, sample, reasons);
        return new RuleDiagnostics(
            rule.Id,
            rule.Kind,
            kindText,
            true,
            isWallClock,
            state.StateText,
            state.NextEstimateText,
            reasons);
    }

    private static (string StateText, string NextEstimateText) DescribeRuntime(
        ReminderRule rule,
        ReminderRuntimeState? runtime,
        DateTimeOffset now,
        ActivitySample sample,
        List<string> reasons)
    {
        if (runtime is null)
        {
            return ("等待下一轮计时开始", "开始计时后这里会显示预计时间");
        }

        switch (runtime.Status)
        {
            case ReminderRuntimeStatus.Due:
                reasons.Add("提醒已到时，若正在暂停或全屏抑制会稍后显示");
                return ("已到提醒时间", "随时会呈现提醒");
            case ReminderRuntimeStatus.WaitingRetry:
                if (runtime.RetryDueAt is { } retryDueAt)
                {
                    reasons.Add($"已选择稍后提醒，{retryDueAt.ToLocalTime():HH:mm} 再次呈现");
                    return ("已搁置（稍后提醒）", $"预计 {retryDueAt.ToLocalTime():HH:mm} 再次提醒");
                }

                reasons.Add("已选择稍后提醒，等待重试时间到达");
                return ("已搁置（稍后提醒）", "等待重试时间到达");
            case ReminderRuntimeStatus.Unanswered:
                reasons.Add("上一轮多次未响应已结束；满足条件后自动开始新一轮计时");
                return ("本轮已结束（未响应）", "满足条件后开始新一轮计时");
        }

        var remainingSeconds = Math.Max(0, runtime.TargetActiveSeconds - runtime.AccumulatedActiveSeconds);
        if (remainingSeconds == 0)
        {
            return ("即将提醒", "随时会呈现提醒");
        }

        if (ReminderKinds.IsWallClock(rule.Kind))
        {
            var elapsedText = FormatDuration(TimeSpan.FromSeconds(runtime.AccumulatedActiveSeconds));
            var remainingText = FormatDuration(TimeSpan.FromSeconds(remainingSeconds));
            var estimate = now.AddSeconds(remainingSeconds);
            reasons.Add($"按生效时段内的经过时间累计：本轮已计 {elapsedText} / {FormatDuration(TimeSpan.FromSeconds(runtime.TargetActiveSeconds))}");
            return (
                $"已计 {elapsedText}，还差 {remainingText}",
                $"预计 {estimate.ToLocalTime():HH:mm} 左右提醒（若保持在生效时段内）");
        }

        var accumulatedText = FormatDuration(TimeSpan.FromSeconds(runtime.AccumulatedActiveSeconds));
        var remainingWorkText = FormatDuration(TimeSpan.FromSeconds(remainingSeconds));
        var workEstimate = now.AddSeconds(remainingSeconds);
        if (!sample.IsWhitelistedForeground || sample.IsLocked)
        {
            reasons.Add("当前没有在白名单应用前台操作，活动提醒的计时已暂停");
        }

        reasons.Add($"本轮已累计有效工作 {accumulatedText}，目标 {FormatDuration(TimeSpan.FromSeconds(runtime.TargetActiveSeconds))}");
        return (
            $"本轮已累计有效工作 {accumulatedText}，还差 {remainingWorkText}",
            $"若保持有效工作，预计 {workEstimate.ToLocalTime():HH:mm} 前后提醒");
    }

    private string DescribePause(DateTimeOffset now)
    {
        if (!isPaused()) return "未暂停";
        var until = getPausedUntil();
        return until is { } value && value > now
            ? $"已暂停至 {value.ToLocalTime():HH:mm}"
            : "已暂停";
    }

    private static string DescribeWhitelist(IReadOnlyList<string> whitelist) =>
        whitelist.Count == 0 ? "白名单为空" : string.Join("、", whitelist.Take(6)) + (whitelist.Count > 6 ? " 等" : "");

    private static bool IsInsideWindow(TimeOnly current, TimeOnly start, TimeOnly end) =>
        start <= end
            ? current >= start && current <= end
            : current >= start || current <= end;

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration == TimeSpan.MaxValue) return "未知";
        var totalMinutes = (int)Math.Round(duration.TotalMinutes);
        if (totalMinutes >= 60)
        {
            return $"{totalMinutes / 60}小时{totalMinutes % 60}分钟";
        }

        return totalMinutes > 0 ? $"{totalMinutes}分钟" : "1分钟以内";
    }
}
