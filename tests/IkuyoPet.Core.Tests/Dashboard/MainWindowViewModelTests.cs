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
