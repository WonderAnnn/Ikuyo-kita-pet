using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using IkuyoPet.Core.Dashboard;

namespace IkuyoPet.App;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly IDashboardQueryService dashboard;
    private readonly MainWindowState navigation = new();
    private DashboardSnapshot? todaySnapshot;
    private bool petEnabled = true;
    private string kindFilter = "全部";
    private string outcomeFilter = "全部结果";
    private DateTime selectedDate = DateTime.Today;

    public MainWindowViewModel(IDashboardQueryService dashboard)
    {
        this.dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
        NavigateCommand = new RelayCommand(parameter =>
        {
            if (parameter is string page && Enum.TryParse<MainWindowPage>(page, out var selected)) Navigate(selected);
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public MainWindowPage CurrentPage => navigation.CurrentPage;
    public ICommand NavigateCommand { get; }
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

    private sealed class RelayCommand(Action<object?> execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute(parameter);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
