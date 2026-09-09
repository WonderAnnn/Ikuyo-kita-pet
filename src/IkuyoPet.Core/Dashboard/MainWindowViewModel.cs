namespace IkuyoPet.Core.Dashboard;

public interface IDashboardQueryService
{
    Task<DashboardSnapshot> GetAsync(DateOnly day, CancellationToken cancellationToken);
}

/// <summary>Testable navigation and dashboard state shared by the WPF shell.</summary>
public sealed class MainWindowViewModel
{
    private readonly IDashboardQueryService dashboard;
    private readonly MainWindowState navigation = new();

    public MainWindowViewModel(IDashboardQueryService dashboard)
    {
        this.dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
    }

    public MainWindowPage CurrentPage => navigation.CurrentPage;
    public DashboardSnapshot Today { get; private set; } = new(
        DateOnly.FromDateTime(DateTime.Today), Array.Empty<TimelineItem>(), 0, 0, 0, 0, TimeSpan.Zero);

    public void Navigate(MainWindowPage page)
    {
        navigation.Navigate(page);
    }

    public Task LoadTodayAsync(DateOnly day, CancellationToken cancellationToken = default) =>
        RefreshAsync(day, cancellationToken);

    public async Task RefreshAsync(DateOnly day, CancellationToken cancellationToken = default)
    {
        Today = await dashboard.GetAsync(day, cancellationToken);
    }
}
