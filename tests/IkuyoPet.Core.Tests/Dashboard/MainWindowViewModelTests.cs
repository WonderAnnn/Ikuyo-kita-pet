using IkuyoPet.Core.Dashboard;
using Xunit;

namespace IkuyoPet.Core.Tests.Dashboard;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void DefaultsToTodayPage()
    {
        var viewModel = new MainWindowViewModel(new FakeDashboardQueryService());

        Assert.Equal(MainWindowPage.Today, viewModel.CurrentPage);
    }

    [Fact]
    public void NavigatesBetweenPages()
    {
        var viewModel = new MainWindowViewModel(new FakeDashboardQueryService());

        viewModel.Navigate(MainWindowPage.Log);

        Assert.Equal(MainWindowPage.Log, viewModel.CurrentPage);
    }

    [Fact]
    public async Task LoadTodayAsyncSetsTodaySnapshot()
    {
        var expectedDay = new DateOnly(2026, 9, 8);
        var snapshot = new DashboardSnapshot(
            expectedDay,
            Array.Empty<TimelineItem>(),
            3,
            1,
            0,
            2,
            TimeSpan.FromMinutes(75));
        var queryService = new FakeDashboardQueryService(snapshot);
        var viewModel = new MainWindowViewModel(queryService);

        await viewModel.LoadTodayAsync(expectedDay, TestContext.Current.CancellationToken);

        Assert.Equal(expectedDay, viewModel.Today.Day);
        Assert.Equal(3, viewModel.Today.CompletedCount);
        Assert.Equal(TimeSpan.FromMinutes(75), viewModel.Today.WorkDuration);
        Assert.Equal(expectedDay, queryService.RequestedDay);
    }

    [Fact]
    public async Task RefreshAsyncReloadsTheSelectedDayAfterAnAction()
    {
        var day = new DateOnly(2026, 9, 9);
        var queryService = new SequencedDashboardQueryService(
            Snapshot(day, completed: 0),
            Snapshot(day, completed: 1));
        var viewModel = new MainWindowViewModel(queryService);

        await viewModel.RefreshAsync(day, TestContext.Current.CancellationToken);
        await viewModel.RefreshAsync(day, TestContext.Current.CancellationToken);

        Assert.Equal(2, queryService.RequestCount);
        Assert.Equal(1, viewModel.Today.CompletedCount);
    }

    private static DashboardSnapshot Snapshot(DateOnly day, int completed) => new(
        day,
        Array.Empty<TimelineItem>(),
        completed,
        0,
        0,
        0,
        TimeSpan.Zero);

    private sealed class SequencedDashboardQueryService(params DashboardSnapshot[] snapshots)
        : IDashboardQueryService
    {
        private int index;
        public int RequestCount { get; private set; }

        public Task<DashboardSnapshot> GetAsync(DateOnly day, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            var snapshot = snapshots[Math.Min(index, snapshots.Length - 1)];
            index++;
            return Task.FromResult(snapshot);
        }
    }
    private sealed class FakeDashboardQueryService : IDashboardQueryService
    {
        private readonly DashboardSnapshot _snapshot;

        public FakeDashboardQueryService()
            : this(new DashboardSnapshot(
                new DateOnly(2026, 9, 8),
                Array.Empty<TimelineItem>(),
                0,
                0,
                0,
                0,
                TimeSpan.Zero))
        {
        }

        public FakeDashboardQueryService(DashboardSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public DateOnly? RequestedDay { get; private set; }

        public Task<DashboardSnapshot> GetAsync(DateOnly day, CancellationToken cancellationToken)
        {
            RequestedDay = day;
            return Task.FromResult(_snapshot);
        }
    }
}
