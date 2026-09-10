using System.IO;
using IkuyoPet.Core.Analytics;
using IkuyoPet.Core.Dashboard;
using IkuyoPet.Core.Reminders;
using IkuyoPet.Core.WorkTracking;
using IkuyoPet.Infrastructure.Storage;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.App;

public sealed class MainWindowViewModelSettingsTests
{
    [Fact]
    public async Task PetEnabledPersistsAcrossViewModelRestart()
    {
        using var database = new TemporaryDatabase();
        var store = new AppSettingsStore(database.ConnectionString);
        await store.WriteAsync("pet.enabled", "false", TestContext.Current.CancellationToken);
        var first = new IkuyoPet.App.MainWindowViewModel(
            new EmptyDashboard(),
            appSettingsStore: store);

        await first.LoadSettingsAsync(TestContext.Current.CancellationToken);
        Assert.False(first.PetEnabled);

        first.PetEnabled = true;
        var restarted = new IkuyoPet.App.MainWindowViewModel(
            new EmptyDashboard(),
            appSettingsStore: store);
        await restarted.LoadSettingsAsync(TestContext.Current.CancellationToken);

        Assert.True(restarted.PetEnabled);
    }

    [Fact]
    public async Task ActiveWorkDeltaAppearsInWorkDurationText()
    {
        var viewModel = new IkuyoPet.App.MainWindowViewModel(new EmptyDashboard());
        var day = DateOnly.FromDateTime(DateTime.Now);
        await viewModel.RefreshAsync(day, TestContext.Current.CancellationToken);

        viewModel.ApplyActiveWorkDelta(
            new ActiveWorkDelta(90, "WINWORD", DateTimeOffset.UtcNow));

        Assert.Equal("0小时1分钟", viewModel.WorkDurationText);
    }

    [Fact]
    public async Task NextReminderTextShowsEstimatedBreakFromActiveWorkRuntime()
    {
        var rule = ReminderRule.CreateDefaultActiveWork(Guid.NewGuid());
        var runtime = new ReminderRuntimeState(
            rule.Id,
            Guid.NewGuid(),
            targetActiveSeconds: 3_000,
            accumulatedActiveSeconds: 1_200,
            ReminderRuntimeStatus.Accumulating,
            attempt: 0,
            retryDueAt: null,
            updatedAt: DateTimeOffset.UtcNow);
        var viewModel = new IkuyoPet.App.MainWindowViewModel(
            new ProgressDashboard(rule, runtime));

        await viewModel.RefreshAsync(DateOnly.FromDateTime(DateTime.Today), TestContext.Current.CancellationToken);

        Assert.Contains("还需约 30 分钟", viewModel.NextReminderText);
    }

    [Fact]
    public async Task RefreshLoadsWorkStatisticsForSelectedPeriod()
    {
        var statistics = new StubStatisticsQueryService();
        var viewModel = new IkuyoPet.App.MainWindowViewModel(
            new EmptyDashboard(),
            workStatisticsQuery: statistics);

        await viewModel.RefreshAsync(
            DateOnly.FromDateTime(DateTime.Today),
            TestContext.Current.CancellationToken);

        Assert.Equal("1小时30分钟", viewModel.WorkStatisticsTotalText);
        Assert.Equal(["Word", "PyCharm"], viewModel.TopApplicationStats.Select(item => item.DisplayName));

        viewModel.StatisticsPeriodIndex = 1;
        await viewModel.RefreshAsync(
            DateOnly.FromDateTime(DateTime.Today),
            TestContext.Current.CancellationToken);

        Assert.Equal(WorkStatisticsPeriod.Week, statistics.LastPeriod);
    }

    [Fact]
    public async Task LoadSettingsAddsMicrosoftWordProcessToDefaults()
    {
        using var database = new TemporaryDatabase();
        var repository = new SqliteEventRepository(database.ConnectionString);
        var viewModel = new IkuyoPet.App.MainWindowViewModel(
            new EmptyDashboard(),
            repository: repository);

        await viewModel.LoadSettingsAsync(TestContext.Current.CancellationToken);

        Assert.Contains(
            viewModel.TrackedApplications,
            application => application.ProcessName == "WINWORD" &&
                           application.DisplayName == "Microsoft Word" &&
                           application.Enabled);
    }

    private sealed class ProgressDashboard(ReminderRule rule, ReminderRuntimeState runtime) : IDashboardQueryService
    {
        public Task<DashboardSnapshot> GetAsync(DateOnly day, CancellationToken cancellationToken)
        {
            var snapshot = new DashboardSnapshot(day, [], 0, 0, 0, 0, TimeSpan.Zero)
            {
                ActiveWorkRule = rule,
                ActiveWorkRuntime = runtime,
            };
            return Task.FromResult(snapshot);
        }
    }

    private sealed class StubStatisticsQueryService : IWorkStatisticsQueryService
    {
        public WorkStatisticsPeriod LastPeriod { get; private set; }

        public Task<WorkStatistics> GetAsync(
            DateOnly selectedDate,
            WorkStatisticsPeriod period,
            CancellationToken cancellationToken)
        {
            LastPeriod = period;
            return Task.FromResult(new WorkStatistics(
                selectedDate,
                selectedDate.AddDays(1),
                5_400,
                [
                    new WorkApplicationUsage("word", "Word", 3_600, 2d / 3d),
                    new WorkApplicationUsage("pycharm64", "PyCharm", 1_800, 1d / 3d),
                ]));
        }
    }

    private sealed class EmptyDashboard : IDashboardQueryService
    {
        public Task<DashboardSnapshot> GetAsync(DateOnly day, CancellationToken cancellationToken) =>
            Task.FromResult(new DashboardSnapshot(day, [], 0, 0, 0, 0, TimeSpan.Zero));
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        private readonly string path = Path.Combine(
            Path.GetTempPath(),
            $"ikuyo-pet-view-model-{Guid.NewGuid():N}.db");

        public string ConnectionString => $"Data Source={path};Pooling=False";

        public void Dispose()
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + "-wal")) File.Delete(path + "-wal");
            if (File.Exists(path + "-shm")) File.Delete(path + "-shm");
        }
    }
}