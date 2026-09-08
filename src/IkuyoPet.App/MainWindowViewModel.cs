using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using IkuyoPet.Core.Dashboard;
using IkuyoPet.Core.Storage;
using IkuyoPet.Core.WorkTracking;
using IkuyoPet.Infrastructure.Windows;

namespace IkuyoPet.App;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly IDashboardQueryService dashboard;
    private readonly IEventRepository? repository;
    private readonly WindowsStartupManager? startupManager;
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

    public MainWindowViewModel(
        IDashboardQueryService dashboard,
        IEventRepository? repository = null,
        WindowsStartupManager? startupManager = null)
    {
        this.dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
        this.repository = repository;
        this.startupManager = startupManager;
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
        set { if (petEnabled == value) return; petEnabled = value; OnPropertyChanged(); }
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

    public async Task LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (repository is null) return;
        var applications = await repository.ReadTrackedApplicationsAsync(cancellationToken);
        if (applications.Count == 0)
        {
            await repository.UpsertTrackedApplicationAsync(
                new TrackedApplication("pycharm64", "PyCharm", true),
                cancellationToken);
            applications = await repository.ReadTrackedApplicationsAsync(cancellationToken);
        }

        TrackedApplications = applications;
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
            return next is null
                ? "今天还没有下一次提醒，先按自己的节奏来吧～"
                : $"{next.ScheduledAt:HH:mm} · {next.Kind}，准备好就出发吧～";
        }
    }
    public string HydrationText => $"{TodaySnapshot?.HydrationCount ?? 0} 次";
    public string ActivityText => $"{TodaySnapshot?.ActivityCount ?? 0} 次";
    public string WorkDurationText => (TodaySnapshot?.WorkDuration ?? TimeSpan.Zero).ToString(@"h\小时m\分钟", CultureInfo.InvariantCulture);

    public void Navigate(MainWindowPage page)
    {
        navigation.Navigate(page);
        OnPropertyChanged(nameof(CurrentPage));
    }

    public async Task LoadTodayAsync(DateOnly day, CancellationToken cancellationToken = default)
    {
        TodaySnapshot = await dashboard.GetAsync(day, cancellationToken);
        OnPropertyChanged(nameof(NextReminderText));
        OnPropertyChanged(nameof(HydrationText));
        OnPropertyChanged(nameof(ActivityText));
        OnPropertyChanged(nameof(WorkDurationText));
    }

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
