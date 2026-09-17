using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using IkuyoPet.App.Export;
using IkuyoPet.Core.Analytics;
using IkuyoPet.Core.Bubbles;
using IkuyoPet.Core.Dashboard;
using IkuyoPet.Core.Diagnostics;
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
    private readonly IRunningProcessInspector? processInspector;
    private BubbleThemeCatalog bubbleThemeCatalog;
    private BubbleThemeSelectionStore? bubbleThemeSelectionStore;
    private StatusDiagnosticsService? diagnostics;
    private DataManagementService? dataManagement;

    private readonly MainWindowState navigation = new();
    private DashboardSnapshot? todaySnapshot;
    private bool petEnabled = true;
    private string kindFilter = "全部";
    private string outcomeFilter = "全部结果";
    private DateTime selectedDate = DateTime.Today;
    private string selectedDateText = DateTime.Today.ToString("yyyy/M/d", CultureInfo.InvariantCulture);
    private string logDateValidationMessage = string.Empty;
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
    private int statisticsRequestVersion;
    private WorkStatistics? workStatistics;
    private int allTimeWorkSeconds;
    private readonly string versionText = $"版本 {AppVersion.Text}";
    private string pdfExportStatus = string.Empty;
    private IReadOnlyList<RunningProcessInfo> runningProcesses = [];
    private RunningProcessInfo? selectedRunningProcess;
    private string processTestStatus = string.Empty;
    private ReminderRuleDraft waterRule = new(DefaultReminderRuleIds.Hydration, "water");
    private ReminderRuleDraft activityRule = new(DefaultReminderRuleIds.Activity, "activity");
    private string rulesStatus = "修改后点击保存，最多 30 秒内生效；默认值仅供一般参考，不是医疗建议。";
    private string diagnosticsGeneratedAtText = "尚未诊断";
    private string diagnosticsChannelText = "未知";
    private string diagnosticsPauseText = "未知";
    private string diagnosticsSuppressionText = "未知";
    private string workTrackingSummaryText = "尚未诊断";
    private IReadOnlyList<string> workTrackingReasons = [];
    private IReadOnlyList<RuleDiagnostics> ruleDiagnosticItems = [];
    private string diagnosticsStatus = string.Empty;
    private string dataStatusText = string.Empty;
    private string backupRetentionText = DataManagementService.DefaultRetentionCount.ToString(CultureInfo.InvariantCulture);
    private IReadOnlyList<string> recentBackupNames = [];
    private IReadOnlyList<BubbleThemeCard> bubbleThemes;
    private BubbleThemeDefinition selectedBubbleTheme;
    private string bubbleThemeStatus;

    private static readonly string[] StatisticsPeriodLabels = ["日", "周", "月"];
    private static readonly string[] AcceptedLogDateFormats = ["yyyy/M/d", "yyyy-MM-dd"];

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
        IWorkStatisticsQueryService? workStatisticsQuery = null,
        IRunningProcessInspector? processInspector = null)
    {
        this.dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
        this.repository = repository;
        this.startupManager = startupManager;
        this.appSettingsStore = appSettingsStore;
        this.workStatisticsQuery = workStatisticsQuery;
        this.processInspector = processInspector;
        bubbleThemeCatalog = new BubbleThemeCatalog(
            Path.Combine(AppContext.BaseDirectory, "assets", "bubbles"));
        selectedBubbleTheme = bubbleThemeCatalog.Default;
        bubbleThemes = CreateBubbleThemeCards(bubbleThemeCatalog, selectedBubbleTheme.Id);
        bubbleThemeStatus = $"当前气泡样式：{selectedBubbleTheme.Name}";
        NavigateCommand = new RelayCommand(parameter =>
        {
            if (parameter is string page && Enum.TryParse<MainWindowPage>(page, out var selected)) Navigate(selected);
        });
        AddTrackedApplicationCommand = new AsyncRelayCommand(AddTrackedApplicationAsync);
        ExportPdfCommand = new RelayCommand(_ => RequestPdfExport());
        RefreshRunningProcessesCommand = new RelayCommand(_ => RefreshRunningProcesses(forceRefresh: true));
        UseSelectedProcessCommand = new RelayCommand(_ => UseSelectedProcess());
        TestProcessTrackingCommand = new AsyncRelayCommand(TestProcessTrackingAsync);
        SaveRulesCommand = new AsyncRelayCommand(() => SaveRulesAsync());
        RefreshDiagnosticsCommand = new AsyncRelayCommand(() => RefreshDiagnosticsAsync(CancellationToken.None));
        BackupNowCommand = new RelayCommand(_ => BackupRequested?.Invoke(this, EventArgs.Empty));
        RestoreBackupCommand = new RelayCommand(_ => RestoreBrowseRequested?.Invoke(this, EventArgs.Empty));
        ExportDataCommand = new RelayCommand(_ => ExportDataRequested?.Invoke(this, EventArgs.Empty));
        OpenBackupFolderCommand = new RelayCommand(_ => OpenBackupFolder());
        SelectBubbleThemeCommand = new AsyncRelayCommand(async parameter =>
        {
            if (parameter is string id) await SelectBubbleThemeAsync(id);
        });
        SelectStatisticsPeriodCommand = new AsyncRelayCommand(SelectStatisticsPeriodAsync);
        ManualWaterCommand = new AsyncRelayCommand(() => RequestManualReminderAsync("water"));
        ManualActivityCommand = new AsyncRelayCommand(() => RequestManualReminderAsync("activity"));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? PdfExportRequested;
    public event EventHandler? BackupRequested;
    public event EventHandler? RestoreBrowseRequested;
    public event EventHandler? ExportDataRequested;
    public event EventHandler<string>? RestoreConfirmed;
    public event EventHandler<BubbleThemeDefinition>? BubbleThemeChanged;
    public event Func<ManualReminderRequestedEventArgs, Task>? ManualReminderRequested;
    public MainWindowPage CurrentPage => navigation.CurrentPage;
    public ICommand NavigateCommand { get; }
    public ICommand AddTrackedApplicationCommand { get; }
    public ICommand ExportPdfCommand { get; }
    public ICommand RefreshRunningProcessesCommand { get; }
    public ICommand UseSelectedProcessCommand { get; }
    public ICommand TestProcessTrackingCommand { get; }
    public ICommand SaveRulesCommand { get; }
    public ICommand RefreshDiagnosticsCommand { get; }
    public ICommand BackupNowCommand { get; }
    public ICommand RestoreBackupCommand { get; }
    public ICommand ExportDataCommand { get; }
    public ICommand OpenBackupFolderCommand { get; }
    public ICommand SelectBubbleThemeCommand { get; }
    public ICommand SelectStatisticsPeriodCommand { get; }
    public ICommand ManualWaterCommand { get; }
    public ICommand ManualActivityCommand { get; }
    public IReadOnlyList<BubbleThemeCard> BubbleThemes => bubbleThemes;
    public BubbleThemeDefinition SelectedBubbleTheme
    {
        get => selectedBubbleTheme;
        private set
        {
            if (selectedBubbleTheme == value) return;
            selectedBubbleTheme = value;
            OnPropertyChanged();
        }
    }
    public string BubbleThemeStatus
    {
        get => bubbleThemeStatus;
        private set
        {
            if (bubbleThemeStatus == value) return;
            bubbleThemeStatus = value;
            OnPropertyChanged();
        }
    }
    public DashboardSnapshot? TodaySnapshot
    {
        get => todaySnapshot;
        private set
        {
            todaySnapshot = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TimelineItems));
            NotifyDashboardSummaryChanged();
        }
    }
    public int StatisticsPeriodIndex
    {
        get => statisticsPeriodIndex;
        set
        {
            var normalized = Math.Clamp(value, 0, 2);
            if (statisticsPeriodIndex == normalized) return;
            SetStatisticsPeriodIndex(normalized);
            _ = RefreshWorkStatisticsAsync(
                DateOnly.FromDateTime(selectedDate),
                (WorkStatisticsPeriod)normalized,
                CancellationToken.None);
        }
    }
    public bool IsDayStatisticsSelected => statisticsPeriodIndex == (int)WorkStatisticsPeriod.Day;
    public bool IsWeekStatisticsSelected => statisticsPeriodIndex == (int)WorkStatisticsPeriod.Week;
    public bool IsMonthStatisticsSelected => statisticsPeriodIndex == (int)WorkStatisticsPeriod.Month;
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
    public IReadOnlyList<RunningProcessInfo> RunningProcesses
    {
        get => runningProcesses;
        private set { runningProcesses = value; OnPropertyChanged(); }
    }

    public RunningProcessInfo? SelectedRunningProcess
    {
        get => selectedRunningProcess;
        set { if (ReferenceEquals(selectedRunningProcess, value)) return; selectedRunningProcess = value; OnPropertyChanged(); }
    }

    public string ProcessTestStatus
    {
        get => processTestStatus;
        private set { if (processTestStatus == value) return; processTestStatus = value; OnPropertyChanged(); }
    }

    public string PdfExportStatus
    {
        get => pdfExportStatus;
        private set { if (pdfExportStatus == value) return; pdfExportStatus = value; OnPropertyChanged(); }
    }
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
        set => ApplySelectedDate(value.Date, synchronizeText: true);
    }
    public string SelectedDateText
    {
        get => selectedDateText;
        set
        {
            var input = value ?? string.Empty;
            if (selectedDateText == input) return;
            selectedDateText = input;
            OnPropertyChanged();
            if (!TryParseLogDate(input, out var parsed))
            {
                LogDateValidationMessage = "日期无效，请输入 yyyy/M/d（例如 2026/9/12）。";
                return;
            }

            LogDateValidationMessage = string.Empty;
            ApplySelectedDate(parsed, synchronizeText: false);
        }
    }
    public string LogDateValidationMessage
    {
        get => logDateValidationMessage;
        private set
        {
            if (logDateValidationMessage == value) return;
            logDateValidationMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasLogDateValidationError));
        }
    }
    public bool HasLogDateValidationError => !string.IsNullOrEmpty(LogDateValidationMessage);
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
            RefreshTimeSensitiveDashboardText();
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
        RefreshTimeSensitiveDashboardText();
    }

    public async Task LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (bubbleThemeSelectionStore is not null)
        {
            var storedThemeId = await bubbleThemeSelectionStore.LoadAsync(cancellationToken);
            var storedTheme = bubbleThemeCatalog.Resolve(storedThemeId);
            ApplyBubbleTheme(
                storedTheme,
                raiseChanged: false,
                $"当前气泡样式：{storedTheme.Name}");
        }

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
            var waterRuleSource = rules.FirstOrDefault(rule => ReminderKinds.IsWallClock(rule.Kind)) ??
                ReminderRule.CreateDefaultHydration(DefaultReminderRuleIds.Hydration);
            var activityRuleSource = rules.FirstOrDefault(rule => ReminderKinds.IsActiveWork(rule.Kind)) ??
                ReminderRule.CreateDefaultActiveWork(DefaultReminderRuleIds.Activity);
            WaterRule = new ReminderRuleDraft(waterRuleSource.Id, waterRuleSource.Kind, waterRuleSource);
            ActivityRule = new ReminderRuleDraft(activityRuleSource.Id, activityRuleSource.Kind, activityRuleSource);
            RuleSummaryText = $"有效工作 {activityRuleSource.IntervalMinMinutes}–{activityRuleSource.IntervalMaxMinutes} 分钟后提醒，建议离屏轻缓活动 {activityRuleSource.ActivityDurationMinutes} 分钟；参数来源：{activityRuleSource.ParameterSource}。";
        }

        if (dataManagement is not null) await LoadBackupInfoAsync(cancellationToken);

        if (processInspector is not null) RefreshRunningProcesses();

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

    public void ConfigureBubbleThemes(
        BubbleThemeCatalog catalog,
        BubbleThemeSelectionStore selectionStore)
    {
        bubbleThemeCatalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        bubbleThemeSelectionStore = selectionStore ?? throw new ArgumentNullException(nameof(selectionStore));
        bubbleThemes = CreateBubbleThemeCards(catalog, catalog.Default.Id);
        OnPropertyChanged(nameof(BubbleThemes));
        ApplyBubbleTheme(catalog.Default, raiseChanged: false, $"当前气泡样式：{catalog.Default.Name}");
    }

    private async Task SelectBubbleThemeAsync(string id)
    {
        var theme = bubbleThemeCatalog.Resolve(id);
        var changed = !string.Equals(
            SelectedBubbleTheme.Id,
            theme.Id,
            StringComparison.OrdinalIgnoreCase);
        ApplyBubbleTheme(theme, raiseChanged: changed, $"已选择「{theme.Name}」气泡样式。");
        if (bubbleThemeSelectionStore is null) return;

        try
        {
            await bubbleThemeSelectionStore.SaveAsync(theme.Id, CancellationToken.None);
        }
        catch (Exception exception)
        {
            BubbleThemeStatus = $"保存气泡样式失败：{exception.Message}";
        }
    }

    private void ApplyBubbleTheme(
        BubbleThemeDefinition theme,
        bool raiseChanged,
        string status)
    {
        SelectedBubbleTheme = theme;
        foreach (var card in bubbleThemes) card.IsSelected = card.Id == theme.Id;
        BubbleThemeStatus = status;
        if (raiseChanged) BubbleThemeChanged?.Invoke(this, theme);
    }

    private static BubbleThemeCard[] CreateBubbleThemeCards(
        BubbleThemeCatalog catalog,
        string selectedId) =>
        BubbleThemeCatalog.BuiltInThemes
            .Select(theme => new BubbleThemeCard(
                theme,
                theme.Id,
                theme.Name,
                theme.Id switch
                {
                    "cloud-chibi" => "轻盈云朵与晕乎小伙伴，适合作为默认样式。",
                    "cloud-guitar" => "元气微笑与柔和涂鸦，适合轻松提醒。",
                    _ => "人物与吉他留在左侧，文字在右侧舒展。",
                },
                catalog.ResolveResourcePath(theme),
                theme.Id == selectedId))
            .ToArray();

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
    private void RefreshRunningProcesses(bool forceRefresh = false)
    {
        if (processInspector is null)
        {
            ProcessTestStatus = "当前测试仅在 Windows 桌面运行时可用。";
            return;
        }

        try
        {
            var previousName = SelectedRunningProcess?.ProcessName ?? NewProcessName.Trim();
            var previousId = SelectedRunningProcess?.ProcessId;
            RunningProcesses = processInspector.GetRunningProcesses(forceRefresh);
            SelectedRunningProcess = RunningProcesses.FirstOrDefault(process =>
                    previousId.HasValue && process.ProcessId == previousId.Value) ??
                RunningProcesses.FirstOrDefault(process =>
                    string.Equals(process.ProcessName, previousName, StringComparison.OrdinalIgnoreCase));
            ProcessTestStatus = RunningProcesses.Count == 0
                ? "未读取到可访问的运行进程；请稍后刷新，或保留手动输入。"
                : $"已读取 {RunningProcesses.Count} 个运行进程；选择目标后点击“填入上方输入框”。";
        }
        catch (Exception exception)
        {
            ProcessTestStatus = $"读取运行进程失败：{exception.Message}";
        }
    }

    private void UseSelectedProcess()
    {
        if (SelectedRunningProcess is null)
        {
            ProcessTestStatus = "请先从运行进程列表选择一个进程。";
            return;
        }

        NewProcessName = SelectedRunningProcess.ProcessName;
        NewDisplayName = SelectedRunningProcess.DisplayName;
        ProcessTestStatus = $"已把 {SelectedRunningProcess.ProcessName}（PID {SelectedRunningProcess.ProcessId}）填入“进程名”和“显示名”；确认后点击“添加到白名单”。";
    }

    private async Task TestProcessTrackingAsync()
    {
        if (processInspector is null)
        {
            ProcessTestStatus = "当前测试仅在 Windows 桌面运行时可用。";
            return;
        }

        var expected = NewProcessName.Trim();
        if (expected.Length == 0 && SelectedRunningProcess is not null)
        {
            expected = SelectedRunningProcess.ProcessName;
        }

        if (expected.Length == 0)
        {
            ProcessTestStatus = "请先选择运行进程，或填写进程名后再测试。";
            return;
        }

        var running = processInspector.GetRunningProcesses(forceRefresh: true);
        if (!running.Any(process => string.Equals(process.ProcessName, expected, StringComparison.OrdinalIgnoreCase)))
        {
            ProcessTestStatus = $"当前没有找到进程 {expected}；请先启动它，再刷新运行进程。";
            return;
        }

        ProcessTestStatus = $"测试中：请切换到 {expected} 并保持键盘或鼠标操作约 5 秒。";
        ProcessTrackingDiagnosis? latest = null;
        ProcessObservation? latestObservation = null;
        for (var second = 0; second < 5; second++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            latestObservation = processInspector.CaptureForeground();
            running = processInspector.GetRunningProcesses(forceRefresh: true);
            latest = ProcessTrackingDiagnosisEvaluator.Evaluate(
                expected,
                running.Select(process => process.ProcessName),
                latestObservation,
                TimeSpan.FromMinutes(5));
            if (latest.CanAccumulate)
            {
                ProcessTestStatus = $"测试通过：{expected} 正在前台、电脑未锁屏、不在全屏/演示模式且最近有操作，正式计时可以累计。";
                return;
            }
        }

        ProcessTestStatus = FormatProcessTestFailure(expected, latest, latestObservation);
    }

    private static string FormatProcessTestFailure(
        string expected,
        ProcessTrackingDiagnosis? diagnosis,
        ProcessObservation? observation)
    {
        if (diagnosis is null || observation is null)
        {
            return $"测试未完成：没有取得 {expected} 的前台状态。";
        }

        if (!diagnosis.IsRunning)
        {
            return $"测试失败：没有找到运行中的 {expected}。";
        }

        if (!diagnosis.IsForeground)
        {
            var foreground = string.IsNullOrWhiteSpace(observation.ProcessName) ? "未知进程" : observation.ProcessName;
            return $"测试失败：{expected} 在运行，但当前前台是 {foreground}。后台运行不会计时。";
        }

        if (!diagnosis.IsUnlocked)
        {
            return "测试失败：电脑处于锁屏状态，锁屏时间不会计时。";
        }

        if (!diagnosis.IsSuppressionClear)
        {
            return observation.IsPresentationMode
                ? "测试失败：系统处于演示模式，演示期间不会累计有效工作。"
                : "测试失败：当前窗口占满屏幕，全屏期间不会累计有效工作。";
        }

        if (!diagnosis.HasRecentInput)
        {
            return "测试失败：最近 5 分钟没有键盘或鼠标操作，空闲时间不会计时。";
        }
        return $"测试失败：{expected} 暂不满足有效工作条件。";
    }
    private async Task RequestManualReminderAsync(string kind)
    {
        var handler = ManualReminderRequested;
        if (handler is not null)
        {
            await handler(new ManualReminderRequestedEventArgs(kind));
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
    public string NextActivityText
    {
        get
        {
            var rule = TodaySnapshot?.ActiveWorkRule;
            var runtime = TodaySnapshot?.ActiveWorkRuntime;
            if (runtime?.Status == ReminderRuntimeStatus.Accumulating)
            {
                var remainingSeconds = Math.Max(0, runtime.TargetActiveSeconds - runtime.AccumulatedActiveSeconds);
                var remainingMinutes = (int)Math.Ceiling(remainingSeconds / 60d);
                if (rule is not null)
                {
                    var earliestSeconds = Math.Max(0, rule.IntervalMinMinutes * 60 - runtime.AccumulatedActiveSeconds);
                    var latestSeconds = Math.Max(earliestSeconds, rule.IntervalMaxMinutes * 60 - runtime.AccumulatedActiveSeconds);
                    var earliest = DateTimeOffset.Now.AddSeconds(earliestSeconds);
                    var latest = DateTimeOffset.Now.AddSeconds(latestSeconds);
                    return $"预计 {earliest:HH:mm}–{latest:HH:mm} 左右休息（依据有效工作累计，还需约 {remainingMinutes} 分钟）";
                }

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
            var nextActivity = TodaySnapshot?.Timeline
                .Where(item => item.Outcome == ReminderOutcome.None && ReminderKinds.IsActiveWork(item.Kind))
                .OrderBy(item => item.ScheduledAt)
                .FirstOrDefault();
            if (nextActivity is not null)
            {
                return $"约 {nextActivity.ScheduledAt.ToLocalTime():HH:mm} 前后进行下一次离屏活动";
            }
            if (rule is not null)
            {
                return $"从现在起约 {rule.IntervalMinMinutes}–{rule.IntervalMaxMinutes} 分钟有效工作后休息";
            }
            return "下一次离屏活动尚未安排，先按自己的节奏来吧～";
        }
    }
    public string NextReminderText => NextActivityText;
    public string NextHydrationText
    {
        get
        {
            var rule = TodaySnapshot?.HydrationRule;
            var runtime = TodaySnapshot?.HydrationRuntime;
            if (rule is not null && runtime?.Status == ReminderRuntimeStatus.Accumulating)
            {
                var earliestSeconds = Math.Max(0, rule.IntervalMinMinutes * 60 - runtime.AccumulatedActiveSeconds);
                var latestSeconds = Math.Max(earliestSeconds, rule.IntervalMaxMinutes * 60 - runtime.AccumulatedActiveSeconds);
                var earliest = DateTimeOffset.Now.AddSeconds(earliestSeconds);
                var latest = DateTimeOffset.Now.AddSeconds(latestSeconds);
                return $"预计 {earliest:HH:mm}–{latest:HH:mm} 左右喝水（依据墙钟累计 {rule.IntervalMinMinutes}–{rule.IntervalMaxMinutes} 分钟）";
            }
            if (runtime?.Status == ReminderRuntimeStatus.Due) return "现在该喝水啦～（本轮已到期）";
            if (runtime?.Status == ReminderRuntimeStatus.WaitingRetry && runtime.RetryDueAt is { } retry)
                return $"预计 {retry.ToLocalTime():HH:mm} 再提醒喝水（本轮已搁置）";
            var nextWater = TodaySnapshot?.Timeline
                .Where(item => item.Outcome == ReminderOutcome.None && ReminderKinds.IsWallClock(item.Kind))
                .OrderBy(item => item.ScheduledAt)
                .FirstOrDefault();
            return nextWater is null
                ? "等待下一轮安排"
                : $"预计 {nextWater.ScheduledAt.ToLocalTime():HH:mm} 左右喝水（依据待处理提醒事件）";
        }
    }
    public string HydrationText => $"{TodaySnapshot?.HydrationCount ?? 0} 次";
    public string ActivityText => $"{TodaySnapshot?.ActivityCount ?? 0} 次";

    public void RefreshTimeSensitiveDashboardText()
    {
        OnPropertyChanged(nameof(NextActivityText));
        OnPropertyChanged(nameof(NextReminderText));
        OnPropertyChanged(nameof(NextHydrationText));
    }

    private void NotifyDashboardSummaryChanged()
    {
        RefreshTimeSensitiveDashboardText();
        OnPropertyChanged(nameof(HydrationText));
        OnPropertyChanged(nameof(ActivityText));
        OnPropertyChanged(nameof(WorkDurationText));
        OnPropertyChanged(nameof(TodayWorkDurationText));
        OnPropertyChanged(nameof(TotalWorkDurationText));
    }
    private string FormatTodayWorkDuration()
    {
        var persisted = TodaySnapshot?.WorkDuration ?? TimeSpan.Zero;
        var duration = persisted + TimeSpan.FromSeconds(GetLiveWorkSeconds());
        return FormatDuration(duration);
    }

    private int GetLiveWorkSeconds() =>
        TodaySnapshot?.Day == liveWorkDay ? Math.Max(0, liveWorkSeconds) : 0;
    public PdfLogExportSnapshot CreatePdfExportSnapshot()
    {
        var statistics = WorkStatistics;
        var start = statistics?.StartDate ?? DateOnly.FromDateTime(selectedDate);
        var end = statistics?.EndDateExclusive.AddDays(-1) ?? start;
        var applications = GetStatisticsApplications()
            .Select(application => new PdfLogExportApplication(
                application.ProcessName,
                application.DisplayName,
                application.ActiveTime))
            .ToArray();
        var entries = TimelineItems
            .Select(item => new PdfLogExportEntry(
                TimeOnly.FromTimeSpan(item.LocalScheduledAt.TimeOfDay),
                item.KindText,
                item.ChannelText,
                item.OutcomeText,
                GetExportNote(item)))
            .ToArray();
        return new PdfLogExportSnapshot(
            start,
            end,
            StatisticsPeriodLabels[Math.Clamp(statisticsPeriodIndex, 0, 2)],
            TimeSpan.FromSeconds(Math.Max(0, GetStatisticsDuration().TotalSeconds)),
            TodaySnapshot?.HydrationCount ?? 0,
            TodaySnapshot?.ActivityCount ?? 0,
            applications,
            entries);
    }

    public void SetPdfExportStatus(string message) => PdfExportStatus = message;

    public static bool TryParseLogDate(string? text, out DateTime value) =>
        DateTime.TryParseExact(
            text?.Trim(),
            AcceptedLogDateFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out value);

    private void ApplySelectedDate(DateTime value, bool synchronizeText)
    {
        var normalized = value.Date;
        if (synchronizeText)
        {
            var formatted = normalized.ToString("yyyy/M/d", CultureInfo.InvariantCulture);
            if (selectedDateText != formatted)
            {
                selectedDateText = formatted;
                OnPropertyChanged(nameof(SelectedDateText));
            }
            LogDateValidationMessage = string.Empty;
        }

        if (selectedDate == normalized) return;
        selectedDate = normalized;
        OnPropertyChanged(nameof(SelectedDate));
        _ = LoadTodayAsync(DateOnly.FromDateTime(normalized));
    }

    private void RequestPdfExport()
    {
        if (!TryParseLogDate(SelectedDateText, out _))
        {
            LogDateValidationMessage = "日期无效，请输入 yyyy/M/d（例如 2026/9/12）。";
            PdfExportStatus = "日期无效，无法导出 PDF。";
            return;
        }

        PdfExportStatus = string.Empty;
        PdfExportRequested?.Invoke(this, EventArgs.Empty);
    }

    private static string GetExportNote(TimelineItem item) => item.OutcomeText switch
    {
        "完成" when item.KindText == "喝水" => "喝水完成",
        "完成" => "离屏活动完成",
        "搁置" => "稍后再次提醒",
        "未响应" => "自动重提",
        _ => item.OutcomeText,
    };
    public void Navigate(MainWindowPage page)
    {
        navigation.Navigate(page);
        OnPropertyChanged(nameof(CurrentPage));
    }

    public Task LoadTodayAsync(DateOnly day, CancellationToken cancellationToken = default) =>
        RefreshAsync(day, cancellationToken);

    public async Task RefreshAsync(DateOnly day, CancellationToken cancellationToken = default)
    {
        var statisticsRequest = Interlocked.Increment(ref statisticsRequestVersion);
        var statisticsPeriod = (WorkStatisticsPeriod)statisticsPeriodIndex;
        TodaySnapshot = await dashboard.GetAsync(day, cancellationToken);
        await RefreshWorkStatisticsAsync(
            day,
            statisticsPeriod,
            cancellationToken,
            statisticsRequest);
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

    private async Task SelectStatisticsPeriodAsync(object? parameter)
    {
        if (parameter is not string text ||
            !int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
            index is < 0 or > 2)
        {
            return;
        }

        SetStatisticsPeriodIndex(index);
        await RefreshWorkStatisticsAsync(
            DateOnly.FromDateTime(selectedDate),
            (WorkStatisticsPeriod)index,
            CancellationToken.None);
    }

    private void SetStatisticsPeriodIndex(int value)
    {
        statisticsPeriodIndex = value;
        OnPropertyChanged(nameof(StatisticsPeriodIndex));
        OnPropertyChanged(nameof(IsDayStatisticsSelected));
        OnPropertyChanged(nameof(IsWeekStatisticsSelected));
        OnPropertyChanged(nameof(IsMonthStatisticsSelected));
    }

    private async Task RefreshWorkStatisticsAsync(
        DateOnly anchorDate,
        WorkStatisticsPeriod period,
        CancellationToken cancellationToken,
        int? existingRequestVersion = null)
    {
        if (workStatisticsQuery is null) return;
        var requestVersion = existingRequestVersion ?? Interlocked.Increment(ref statisticsRequestVersion);
        var result = await workStatisticsQuery.GetAsync(anchorDate, period, cancellationToken);
        if (requestVersion != Volatile.Read(ref statisticsRequestVersion)) return;
        WorkStatistics = result;
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

    public ReminderRuleDraft WaterRule
    {
        get => waterRule;
        private set { waterRule = value; OnPropertyChanged(); }
    }

    public ReminderRuleDraft ActivityRule
    {
        get => activityRule;
        private set { activityRule = value; OnPropertyChanged(); }
    }

    public string RulesStatus
    {
        get => rulesStatus;
        private set { if (rulesStatus == value) return; rulesStatus = value; OnPropertyChanged(); }
    }

    public string DiagnosticsGeneratedAtText
    {
        get => diagnosticsGeneratedAtText;
        private set { if (diagnosticsGeneratedAtText == value) return; diagnosticsGeneratedAtText = value; OnPropertyChanged(); }
    }

    public string DiagnosticsChannelText
    {
        get => diagnosticsChannelText;
        private set { if (diagnosticsChannelText == value) return; diagnosticsChannelText = value; OnPropertyChanged(); }
    }

    public string DiagnosticsPauseText
    {
        get => diagnosticsPauseText;
        private set { if (diagnosticsPauseText == value) return; diagnosticsPauseText = value; OnPropertyChanged(); }
    }

    public string DiagnosticsSuppressionText
    {
        get => diagnosticsSuppressionText;
        private set { if (diagnosticsSuppressionText == value) return; diagnosticsSuppressionText = value; OnPropertyChanged(); }
    }

    public string WorkTrackingSummaryText
    {
        get => workTrackingSummaryText;
        private set { if (workTrackingSummaryText == value) return; workTrackingSummaryText = value; OnPropertyChanged(); }
    }

    public IReadOnlyList<string> WorkTrackingReasons
    {
        get => workTrackingReasons;
        private set { workTrackingReasons = value; OnPropertyChanged(); }
    }

    public IReadOnlyList<RuleDiagnostics> RuleDiagnosticItems
    {
        get => ruleDiagnosticItems;
        private set { ruleDiagnosticItems = value; OnPropertyChanged(); }
    }

    public string DiagnosticsStatus
    {
        get => diagnosticsStatus;
        private set { if (diagnosticsStatus == value) return; diagnosticsStatus = value; OnPropertyChanged(); }
    }

    public string DataStatusText
    {
        get => dataStatusText;
        private set { if (dataStatusText == value) return; dataStatusText = value; OnPropertyChanged(); }
    }

    public string BackupDirectoryText =>
        dataManagement?.BackupDirectory ?? string.Empty;

    public string BackupRetentionText
    {
        get => backupRetentionText;
        set
        {
            if (backupRetentionText == value) return;
            backupRetentionText = value;
            OnPropertyChanged();
            PersistBackupRetention();
        }
    }

    public IReadOnlyList<string> RecentBackupNames
    {
        get => recentBackupNames;
        private set { recentBackupNames = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Attaches services that App.xaml.cs builds after the view model, such as
    /// the foreground probe used by the diagnostics panel.
    /// </summary>
    public void AttachServices(
        StatusDiagnosticsService? diagnosticsService,
        DataManagementService? dataManagementService)
    {
        diagnostics = diagnosticsService;
        dataManagement = dataManagementService;
        OnPropertyChanged(nameof(BackupDirectoryText));
    }

    public async Task SaveRulesAsync()
    {
        if (repository is null)
        {
            RulesStatus = "当前环境没有本地数据库，不能保存提醒规则。";
            return;
        }

        var problems = new List<string>();
        problems.AddRange(WaterRule.Validate("喝水"));
        problems.AddRange(ActivityRule.Validate("活动"));
        if (problems.Count > 0)
        {
            RulesStatus = "未保存：" + string.Join("；", problems) + "。";
            return;
        }

        try
        {
            var version = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var rules = new[]
            {
                WaterRule.Build(version),
                ActivityRule.Build(version),
            };
            await repository.UpsertReminderRulesAsync(rules, CancellationToken.None);
            RuleSummaryText = $"有效工作 {ActivityRule.IntervalMinMinutes}–{ActivityRule.IntervalMaxMinutes} 分钟后提醒，建议离屏轻缓活动 {ActivityRule.ActivityDurationMinutes} 分钟。";
            RulesStatus = $"已保存（参数来源：user / {version}）；提醒循环最多 30 秒内改用新参数。";
            await RefreshAsync(DateOnly.FromDateTime(selectedDate), CancellationToken.None);
        }
        catch (Exception exception)
        {
            RulesStatus = $"规则保存失败：{exception.Message}";
        }
    }

    public async Task RefreshDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        if (diagnostics is null)
        {
            DiagnosticsStatus = "诊断服务在完整应用运行时可用。";
            return;
        }

        try
        {
            var report = await diagnostics.GetReportAsync(cancellationToken);
            DiagnosticsGeneratedAtText = $"诊断时间 {report.GeneratedAt.ToLocalTime():HH:mm:ss}";
            DiagnosticsChannelText = report.ReminderChannelText;
            DiagnosticsPauseText = report.PauseText;
            DiagnosticsSuppressionText = report.SuppressionText;
            WorkTrackingSummaryText = report.WorkTracking.CanAccumulate
                ? $"计时中：前台 {report.WorkTracking.ForegroundProcessName}"
                : "当前没有在累计有效工作";
            WorkTrackingReasons = report.WorkTracking.Reasons;
            RuleDiagnosticItems = report.Rules;
            DiagnosticsStatus = string.Empty;
        }
        catch (Exception exception)
        {
            DiagnosticsStatus = $"读取诊断信息失败：{exception.Message}";
        }
    }

    private async Task LoadBackupInfoAsync(CancellationToken cancellationToken)
    {
        if (dataManagement is null) return;
        try
        {
            var retention = await dataManagement.ReadRetentionCountAsync(cancellationToken);
            backupRetentionText = retention.ToString(CultureInfo.InvariantCulture);
            OnPropertyChanged(nameof(BackupRetentionText));
            RefreshBackupList();
            DataStatusText = "数据保存在本机 SQLite 数据库；每天首次启动会自动备份，保留最近若干份。";
        }
        catch (Exception exception)
        {
            DataStatusText = $"读取备份设置失败：{exception.Message}";
        }
    }

    private void RefreshBackupList()
    {
        if (dataManagement is null) return;
        RecentBackupNames = dataManagement
            .ListDailyBackups()
            .Take(10)
            .Select(file => file.Name)
            .ToArray();
    }

    public async Task BackupNowAsync(string destinationPath)
    {
        if (dataManagement is null)
        {
            DataStatusText = "数据管理在完整应用运行时可用。";
            return;
        }

        try
        {
            await dataManagement.BackupToAsync(destinationPath);
            DataStatusText = $"已备份到 {Path.GetFileName(destinationPath)}。";
            RefreshBackupList();
        }
        catch (Exception exception)
        {
            DataStatusText = $"备份失败：{exception.Message}";
        }
    }

    public async Task ExportDataToAsync(string destinationPath)
    {
        if (dataManagement is null)
        {
            DataStatusText = "数据管理在完整应用运行时可用。";
            return;
        }

        try
        {
            await dataManagement.ExportAllDataJsonAsync(destinationPath);
            DataStatusText = $"已导出全部数据：{Path.GetFileName(destinationPath)}。";
        }
        catch (Exception exception)
        {
            DataStatusText = $"导出失败：{exception.Message}";
        }
    }

    /// <summary>
    /// Confirms the restore path chosen in the browse dialog. App.xaml.cs owns
    /// the actual restore because background loops must stop first.
    /// </summary>
    public void RaiseRestoreConfirmed(string backupPath) =>
        RestoreConfirmed?.Invoke(this, backupPath);

    private void PersistBackupRetention()
    {
        if (dataManagement is null) return;
        if (!int.TryParse(BackupRetentionText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return;
        }

        try
        {
            dataManagement.WriteRetentionCountAsync(value).GetAwaiter().GetResult();
            RefreshBackupList();
            DataStatusText = $"已保存保留份数：{Math.Clamp(value, DataManagementService.RetentionLowerBound, DataManagementService.RetentionUpperBound)}；下次自动备份时生效。";
        }
        catch (Exception exception)
        {
            DataStatusText = $"保存备份份数失败：{exception.Message}";
        }
    }

    private void OpenBackupFolder()
    {
        if (dataManagement is null) return;
        try
        {
            Directory.CreateDirectory(dataManagement.BackupDirectory);
            using var process = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("explorer.exe", dataManagement.BackupDirectory)
                {
                    UseShellExecute = true,
                });
        }
        catch (Exception exception)
        {
            DataStatusText = $"打开备份文件夹失败：{exception.Message}";
        }
    }

    private sealed class AsyncRelayCommand : ICommand
    {
        private readonly Func<object?, Task> execute;
        private bool isExecuting;

        public AsyncRelayCommand(Func<Task> execute)
            : this(_ => execute())
        {
        }

        public AsyncRelayCommand(Func<object?, Task> execute)
        {
            this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
        }

        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => !isExecuting;
        public async void Execute(object? parameter)
        {
            if (isExecuting) return;
            isExecuting = true;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            try { await execute(parameter); }
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

public sealed class ManualReminderRequestedEventArgs(string kind) : EventArgs
{
    public string Kind { get; } = kind;
}

public sealed class BubbleThemeCard : INotifyPropertyChanged
{
    private bool isSelected;

    public BubbleThemeCard(
        BubbleThemeDefinition theme,
        string id,
        string name,
        string description,
        string previewPath,
        bool isSelected)
    {
        Theme = theme;
        Id = id;
        Name = name;
        Description = description;
        PreviewPath = previewPath;
        this.isSelected = isSelected;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public BubbleThemeDefinition Theme { get; }
    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public string PreviewPath { get; }
    public bool IsDefault => Id == BubbleThemeCatalog.BuiltInThemes[0].Id;
    public bool IsSelected
    {
        get => isSelected;
        internal set
        {
            if (isSelected == value) return;
            isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
}
