using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using IkuyoPet.Core.Analytics;
using IkuyoPet.Core.Dashboard;
using IkuyoPet.Core.Reminders;
using IkuyoPet.Core.Storage;
using IkuyoPet.Core.WorkTracking;
using IkuyoPet.Infrastructure.Storage;
using IkuyoPet.Infrastructure.Windows;

namespace IkuyoPet.App;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly IDashboardQueryService dashboard;
    private readonly IEventRepository? repository;
    private readonly WindowsStartupManager? startupManager;
    private readonly AppSettingsStore? appSettingsStore;
    private readonly IWorkStatisticsQueryService? workStatisticsQuery;
    private readonly MainWindowState navigation = new();
    private DashboardSnapshot? todaySnapshot;
    private bool petEnabled = true;
    private string kindFilter = "全部";
    private string outcomeFilter = "全部结果";
    private DateTime selectedDate = DateTime.Today;
    private IReadOnlyList<TrackedApplication> trackedApplications = [];
    private string newProcessName = string.Empty;
    private string newDisplayName = string.Empty;
    private bool startupEnabled;
    private string startupStatus = string.Empty;
    private string settingsStatus = string.Empty;
    private string currentSkinText = "默认占位皮肤";
    private string ruleSummaryText = "有效工作 40–50 分钟后提醒，建议离屏轻缓活动 5 分钟。";
    private DateOnly? liveWorkDay;
    private string? liveWorkProcessName;
    private int liveWorkSeconds;
    private int statisticsPeriodIndex;
    private WorkStatistics? workStatistics;
    private int allTimeWorkSeconds;
    private readonly string versionText = $"版本 {AppVersion.Text}";

    private static readonly IReadOnlyList<TrackedApplication> DefaultTrackedApplications =
    [
        new("pycharm64", "PyCharm", true),
        new("WINWORD", "Microsoft Word", true),
    ];

    public MainWindowViewModel(
        IDashboardQueryService dashboard,
        IEventRepository? repository = null,
        WindowsStartupManager? startupManager = null,
        AppSettingsStore? appSettingsStore = null,
        IWorkStatisticsQueryService? workStatisticsQuery = null)
    {
        this.dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
        this.repository = repository;
        this.startupManager = startupManager;
        this.appSettingsStore = appSettingsStore;
        this.workStatisticsQuery = workStatisticsQuery;
        NavigateCommand = new RelayCommand(parameter =>
        {
            if (parameter is string page && Enum.TryParse<MainWindowPage>(page, out var selected)) Navigate(selected);
        });
        AddTrackedApplicationCommand = new AsyncRelayCommand(AddTrackedApplicationAsync);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public MainWindowPage CurrentPage => navigation.CurrentPage;
    public ICommand NavigateCommand { get; }
    public ICommand AddTrackedApplicationCommand { get; }
    public DashboardSnapshot? TodaySnapshot
    {
        get => todaySnapshot;
        private set { todaySnapshot = value; OnPropertyChanged(); OnPropertyChanged(nameof(TimelineItems)); }
    }
    public int StatisticsPeriodIndex
    {
        get => statisticsPeriodIndex;
        set
        {
            var normalized = Math.Clamp(value, 0, 2);
            if (statisticsPeriodIndex == normalized) return;
            statisticsPeriodIndex = normalized;
            OnPropertyChanged();
            _ = RefreshAsync(DateOnly.FromDateTime(selectedDate), CancellationToken.None);
        }
    }
    public WorkStatistics? WorkStatistics
    {
        get => workStatistics;
        private set
        {
            workStatistics = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(WorkStatisticsTotalText));
            OnPropertyChanged(nameof(WorkStatisticsRangeText));
            OnPropertyChanged(nameof(TopApplicationStats));
        }
    }
    public string WorkStatisticsTotalText => FormatDuration(GetStatisticsDuration());
    public string WorkStatisticsRangeText => FormatStatisticsRange();
    public string TotalWorkDurationText => FormatDuration(TimeSpan.FromSeconds((long)allTimeWorkSeconds + GetLiveWorkSeconds()));
    public string TodayWorkDurationText => FormatTodayWorkDuration();
    public string WorkDurationText => TodayWorkDurationText;
    public string VersionText => versionText;
    public IReadOnlyList<WorkApplicationUsage> TopApplicationStats => GetStatisticsApplications();
    public IReadOnlyList<TimelineItem> TimelineItems
    {
        get
        {
            var items = TodaySnapshot?.Timeline ?? Array.Empty<TimelineItem>();
            if (kindFilter != "全部") items = items.Where(item => item.KindText == kindFilter).ToArray();
            if (outcomeFilter != "全部结果") items = items.Where(item => item.OutcomeText == outcomeFilter).ToArray();
            return items.OrderByDescending(item => item.ScheduledAt).ToArray();
        }
    }
    public string KindFilter
    {
        get => kindFilter;
        set { if (kindFilter == value) return; kindFilter = value; OnPropertyChanged(nameof(TimelineItems)); }
    }
    public DateTime SelectedDate
    {
        get => selectedDate;
        set
        {
            var normalized = value.Date;
            if (selectedDate == normalized) return;
            selectedDate = normalized;
            OnPropertyChanged();
            _ = LoadTodayAsync(DateOnly.FromDateTime(normalized));
        }
    }
    public string OutcomeFilter
    {
        get => outcomeFilter;
        set { if (outcomeFilter == value) return; outcomeFilter = value; OnPropertyChanged(nameof(TimelineItems)); }
    }
    public bool PetEnabled
    {
        get => petEnabled;
        set
        {
            if (petEnabled == value) return;
            petEnabled = value;
            OnPropertyChanged();
            PersistPetEnabled();
            OnPropertyChanged(nameof(ReminderChannelText));
        }
    }

    public string ReminderChannelText => PetEnabled ? "透明桌宠提醒" : "Windows 通知提醒";

    public string CurrentSkinText
    {
        get => currentSkinText;
        private set { if (currentSkinText == value) return; currentSkinText = value; OnPropertyChanged(); }
    }

    public string RuleSummaryText
    {
        get => ruleSummaryText;
        private set { if (ruleSummaryText == value) return; ruleSummaryText = value; OnPropertyChanged(); }
    }
    public IReadOnlyList<TrackedApplication> TrackedApplications
    {
        get => trackedApplications;
        private set { trackedApplications = value; OnPropertyChanged(); }
    }

    public string NewProcessName
    {
        get => newProcessName;
        set { if (newProcessName == value) return; newProcessName = value; OnPropertyChanged(); }
    }

    public string NewDisplayName
    {
        get => newDisplayName;
        set { if (newDisplayName == value) return; newDisplayName = value; OnPropertyChanged(); }
    }

    public bool StartupEnabled
    {
        get => startupEnabled;
        set
        {
            if (startupEnabled == value) return;
            startupEnabled = value;
            OnPropertyChanged();
            if (startupManager is null) return;
            var result = startupManager.SetEnabled(value);
            if (!result.Success)
            {
                startupEnabled = !value;
                OnPropertyChanged(nameof(StartupEnabled));
            }
            StartupStatus = result.Success
                ? (value ? "已启用当前用户开机启动。" : "已关闭当前用户开机启动。")
                : $"开机启动设置失败：{result.ErrorMessage}";
        }
    }

    public string SettingsStatus
    {
        get => settingsStatus;
        private set { if (settingsStatus == value) return; settingsStatus = value; OnPropertyChanged(); }
    }

    public string StartupStatus
    {
        get => startupStatus;
        private set { if (startupStatus == value) return; startupStatus = value; OnPropertyChanged(); }
    }

    public string? GetTrackedDisplayName(string processName) =>
        trackedApplications.FirstOrDefault(application =>
            application.Enabled && string.Equals(application.ProcessName, processName, StringComparison.OrdinalIgnoreCase))?.DisplayName;

    public bool IsTrackedProcess(string processName) =>
        trackedApplications.Any(application =>
            application.Enabled && string.Equals(application.ProcessName, processName, StringComparison.OrdinalIgnoreCase));

    public void ApplyActiveWorkDelta(ActiveWorkDelta delta)
    {
        ArgumentNullException.ThrowIfNull(delta);
        var localDay = DateOnly.FromDateTime(delta.ObservedAt.ToLocalTime().DateTime);
        if (liveWorkDay != localDay)
        {
            liveWorkDay = localDay;
            liveWorkProcessName = null;
            liveWorkSeconds = 0;
        }

        if (delta.ActiveSeconds <= 0 || string.IsNullOrWhiteSpace(delta.ProcessName))
        {
            liveWorkProcessName = null;
            liveWorkSeconds = 0;
            OnPropertyChanged(nameof(WorkDurationText));
            OnPropertyChanged(nameof(TodayWorkDurationText));
            OnPropertyChanged(nameof(TotalWorkDurationText));
            OnPropertyChanged(nameof(WorkStatisticsTotalText));
            OnPropertyChanged(nameof(WorkStatisticsRangeText));
            OnPropertyChanged(nameof(TopApplicationStats));
            return;
        }

        if (liveWorkProcessName is not null &&
            !string.Equals(liveWorkProcessName, delta.ProcessName, StringComparison.OrdinalIgnoreCase))
        {
            liveWorkSeconds = 0;
        }

        liveWorkProcessName = delta.ProcessName;
        liveWorkSeconds = checked(liveWorkSeconds + delta.ActiveSeconds);
        OnPropertyChanged(nameof(WorkDurationText));
        OnPropertyChanged(nameof(TodayWorkDurationText));
        OnPropertyChanged(nameof(TotalWorkDurationText));
        OnPropertyChanged(nameof(WorkStatisticsTotalText));
        OnPropertyChanged(nameof(WorkStatisticsRangeText));
        OnPropertyChanged(nameof(TopApplicationStats));
    }

    public async Task LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (appSettingsStore is not null)
        {
            try
            {
                var storedPetEnabled = await appSettingsStore.ReadAsync("pet.enabled", cancellationToken);
                if (bool.TryParse(storedPetEnabled, out var enabled))
                {
                    petEnabled = enabled;
                    OnPropertyChanged(nameof(PetEnabled));
                    OnPropertyChanged(nameof(ReminderChannelText));
                }
            }
            catch (Exception exception)
            {
                SettingsStatus = $"读取桌宠设置失败：{exception.Message}";
            }
        }

        if (repository is not null)
        {
            var applications = await repository.ReadTrackedApplicationsAsync(cancellationToken);
            foreach (var defaultApplication in DefaultTrackedApplications)
            {
                if (applications.Any(application =>
                    string.Equals(
                        application.ProcessName,
                        defaultApplication.ProcessName,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                await repository.UpsertTrackedApplicationAsync(
                    defaultApplication,
                    cancellationToken);
            }

            applications = await repository.ReadTrackedApplicationsAsync(cancellationToken);
            TrackedApplications = applications;
            var rules = await repository.ReadReminderRulesAsync(cancellationToken);
            var activityRule = rules.FirstOrDefault(rule => rule.Enabled && rule.Kind == "activity");
            if (activityRule is not null)
            {
                RuleSummaryText = $"有效工作 {activityRule.IntervalMinMinutes}–{activityRule.IntervalMaxMinutes} 分钟后提醒，建议离屏轻缓活动 {activityRule.ActivityDurationMinutes} 分钟；参数来源：{activityRule.ParameterSource}。";
            }
        }

        if (startupManager is not null)
        {
            var startupState = startupManager.ReadStatus();
            startupEnabled = startupState.Enabled;
            OnPropertyChanged(nameof(StartupEnabled));
            StartupStatus = startupState.Success
                ? string.Empty
                : "读取开机启动状态失败：" + startupState.ErrorMessage;
        }
    }

    public void SetCurrentSkin(string id, string version, bool loaded, string? error = null)
    {
        CurrentSkinText = loaded ? $"{id} / {version}" : "默认占位皮肤";
        if (!loaded && !string.IsNullOrWhiteSpace(error))
        {
            SettingsStatus = $"皮肤加载失败，已使用占位皮肤：{error}";
        }
    }

    private void PersistPetEnabled()
    {
        if (appSettingsStore is null) return;
        try
        {
            appSettingsStore.WriteAsync(
                "pet.enabled",
                PetEnabled ? "true" : "false",
                CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            SettingsStatus = $"保存桌宠设置失败：{exception.Message}";
        }
    }
    private async Task AddTrackedApplicationAsync()
    {
        if (repository is null) return;
        var processName = NewProcessName.Trim();
        var displayName = NewDisplayName.Trim();
        if (processName.Length == 0 || displayName.Length == 0)
        {
            SettingsStatus = "请填写进程名和显示名。";
            return;
        }

        try
        {
            await repository.UpsertTrackedApplicationAsync(
                new TrackedApplication(processName, displayName, true),
                CancellationToken.None);
            TrackedApplications = await repository.ReadTrackedApplicationsAsync(CancellationToken.None);
            NewProcessName = string.Empty;
            NewDisplayName = string.Empty;
            SettingsStatus = $"已添加 {displayName}，满足条件后开始统计。";
        }
        catch (Exception exception)
        {
            SettingsStatus = $"白名单保存失败：{exception.Message}";
        }
    }
    public string NextReminderText
    {
        get
        {
            var next = TodaySnapshot?.Timeline
                .Where(item => item.Outcome == IkuyoPet.Core.Reminders.ReminderOutcome.None)
                .OrderBy(item => item.ScheduledAt)
                .FirstOrDefault();
            if (next is not null)
            {
                return $"{next.ScheduledAt.ToLocalTime():HH:mm} · {next.Kind}，准备好就出发吧～";
            }

            var rule = TodaySnapshot?.ActiveWorkRule;
            var runtime = TodaySnapshot?.ActiveWorkRuntime;
            if (runtime?.Status == ReminderRuntimeStatus.Accumulating)
            {
                var remainingSeconds = Math.Max(0, runtime.TargetActiveSeconds - runtime.AccumulatedActiveSeconds);
                var remainingMinutes = (int)Math.Ceiling(remainingSeconds / 60d);
                var estimate = DateTimeOffset.Now.AddSeconds(remainingSeconds);
                return $"预计 {estimate:HH:mm} 左右休息（还需约 {remainingMinutes} 分钟有效工作）";
            }
            if (runtime?.Status == ReminderRuntimeStatus.Due)
            {
                return "现在可以休息啦～";
            }
            if (runtime?.Status == ReminderRuntimeStatus.WaitingRetry && runtime.RetryDueAt is { } retryDueAt)
            {
                return $"已搁置，预计 {retryDueAt.ToLocalTime():HH:mm} 再提醒";
            }
            if (runtime?.Status == ReminderRuntimeStatus.Unanswered)
            {
                return "本轮提醒已结束，下一轮有效工作会重新计时";
            }
            if (rule is not null)
            {
                return $"保持前台有效工作约 {rule.IntervalMinMinutes}–{rule.IntervalMaxMinutes} 分钟后休息";
            }
            return "今天还没有下一次提醒，先按自己的节奏来吧～";
        }
    }
    public string HydrationText => $"{TodaySnapshot?.HydrationCount ?? 0} 次";
    public string ActivityText => $"{TodaySnapshot?.ActivityCount ?? 0} 次";
    private string FormatTodayWorkDuration()
    {
        var persisted = TodaySnapshot?.WorkDuration ?? TimeSpan.Zero;
        var duration = persisted + TimeSpan.FromSeconds(GetLiveWorkSeconds());
        return FormatDuration(duration);
    }

    private int GetLiveWorkSeconds() =>
        TodaySnapshot?.Day == liveWorkDay ? Math.Max(0, liveWorkSeconds) : 0;
    public void Navigate(MainWindowPage page)
    {
        navigation.Navigate(page);
        OnPropertyChanged(nameof(CurrentPage));
    }

    public Task LoadTodayAsync(DateOnly day, CancellationToken cancellationToken = default) =>
        RefreshAsync(day, cancellationToken);

    public async Task RefreshAsync(DateOnly day, CancellationToken cancellationToken = default)
    {
        TodaySnapshot = await dashboard.GetAsync(day, cancellationToken);
        if (workStatisticsQuery is not null)
        {
            WorkStatistics = await workStatisticsQuery.GetAsync(
                day,
                (WorkStatisticsPeriod)statisticsPeriodIndex,
                cancellationToken);
        }
        if (repository is not null)
        {
            try
            {
                var sessions = await repository.ReadAllWorkSessionsAsync(cancellationToken);
                var total = sessions.Sum(session => (long)Math.Max(0, session.ActiveSeconds));
                allTimeWorkSeconds = (int)Math.Min(int.MaxValue, total);
            }
            catch (NotSupportedException)
            {
                // Lightweight test repositories may not provide historical aggregation.
            }
        }
        OnPropertyChanged(nameof(NextReminderText));
        OnPropertyChanged(nameof(HydrationText));
        OnPropertyChanged(nameof(ActivityText));
        OnPropertyChanged(nameof(WorkDurationText));
        OnPropertyChanged(nameof(TodayWorkDurationText));
        OnPropertyChanged(nameof(TotalWorkDurationText));
        OnPropertyChanged(nameof(WorkStatisticsTotalText));
        OnPropertyChanged(nameof(WorkStatisticsRangeText));
        OnPropertyChanged(nameof(TopApplicationStats));
    }

    private string FormatStatisticsRange()
    {
        var statistics = WorkStatistics;
        if (statistics is null)
        {
            return string.Empty;
        }

        var start = statistics.StartDate;
        var end = statistics.EndDateExclusive.AddDays(-1);
        return start == end
            ? $"北京时间 {start.Year}年{start.Month}月{start.Day}日"
            : $"北京时间 {start.Year}年{start.Month}月{start.Day}日–{end.Year}年{end.Month}月{end.Day}日";
    }
    private TimeSpan GetStatisticsDuration()
    {
        var persisted = WorkStatistics?.TotalWorkTime ?? TimeSpan.Zero;
        var live = statisticsPeriodIndex == (int)WorkStatisticsPeriod.Day &&
                   TodaySnapshot?.Day == liveWorkDay
            ? TimeSpan.FromSeconds(liveWorkSeconds)
            : TimeSpan.Zero;
        return persisted + live;
    }

    private IReadOnlyList<WorkApplicationUsage> GetStatisticsApplications()
    {
        var applications = WorkStatistics?.TopApplications ?? [];
        if (statisticsPeriodIndex != (int)WorkStatisticsPeriod.Day ||
            TodaySnapshot?.Day != liveWorkDay ||
            liveWorkSeconds <= 0 ||
            string.IsNullOrWhiteSpace(liveWorkProcessName))
        {
            return applications;
        }

        var liveDisplayName = GetTrackedDisplayName(liveWorkProcessName) ?? liveWorkProcessName;
        var merged = applications
            .ToDictionary(
                item => (item.ProcessName, item.DisplayName),
                item => item.ActiveSeconds);
        var key = (liveWorkProcessName, liveDisplayName);
        merged[key] = merged.GetValueOrDefault(key) + liveWorkSeconds;
        var totalSeconds = merged.Values.Sum();
        return merged
            .Select(item => new WorkApplicationUsage(
                item.Key.ProcessName,
                item.Key.DisplayName,
                item.Value,
                totalSeconds > 0 ? (double)item.Value / totalSeconds : 0))
            .OrderByDescending(item => item.ActiveSeconds)
            .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();
    }

    private static string FormatDuration(TimeSpan duration) =>
        $"{(int)duration.TotalHours}小时{duration.Minutes}分钟";

    private sealed class AsyncRelayCommand(Func<Task> execute) : ICommand
    {
        private bool isExecuting;
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => !isExecuting;
        public async void Execute(object? parameter)
        {
            if (isExecuting) return;
            isExecuting = true;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            try { await execute(); }
            finally
            {
                isExecuting = false;
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private sealed class RelayCommand(Action<object?> execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute(parameter);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
