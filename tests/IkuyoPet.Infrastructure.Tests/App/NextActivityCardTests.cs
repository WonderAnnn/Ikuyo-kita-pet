using System.ComponentModel;
using System.Globalization;
using IkuyoPet.Core.Dashboard;
using IkuyoPet.Core.Reminders;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.App;

public sealed class NextActivityCardTests
{
    [Fact]
    public async Task EarlierWaterReminderDoesNotReplaceNextActivityWindow()
    {
        var now = DateTimeOffset.Now;
        var activityRule = ReminderRule.CreateDefaultActiveWork(DefaultReminderRuleIds.Activity);
        var snapshot = new DashboardSnapshot(
            DateOnly.FromDateTime(DateTime.Today),
            [
                new TimelineItem(now.AddMinutes(1), "water", "pet", ReminderOutcome.None, null, 0),
                new TimelineItem(now.AddMinutes(12), "activity", "pet", ReminderOutcome.None, null, 0),
            ],
            0, 0, 0, 0, TimeSpan.Zero)
        {
            ActiveWorkRule = activityRule,
        };
        var viewModel = new IkuyoPet.App.MainWindowViewModel(new FixedDashboard(snapshot));

        await viewModel.RefreshAsync(snapshot.Day, TestContext.Current.CancellationToken);

        Assert.Contains(now.AddMinutes(12).ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture), viewModel.NextActivityText);
        Assert.DoesNotContain("water", viewModel.NextActivityText);
        Assert.Contains(now.AddMinutes(1).ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture), viewModel.NextHydrationText);
    }

    [Fact]
    public async Task ActiveRuntimeShowsFuzzyWindowAndRefreshRaisesCardProperties()
    {
        var rule = ReminderRule.CreateDefaultActiveWork(DefaultReminderRuleIds.Activity);
        var runtime = new ReminderRuntimeState(
            rule.Id,
            Guid.NewGuid(),
            targetActiveSeconds: 3_000,
            accumulatedActiveSeconds: 1_200,
            ReminderRuntimeStatus.Accumulating,
            attempt: 0,
            retryDueAt: null,
            updatedAt: DateTimeOffset.UtcNow);
        var snapshot = new DashboardSnapshot(DateOnly.FromDateTime(DateTime.Today), [], 0, 0, 0, 0, TimeSpan.Zero)
        {
            ActiveWorkRule = rule,
            ActiveWorkRuntime = runtime,
        };
        var viewModel = new IkuyoPet.App.MainWindowViewModel(new FixedDashboard(snapshot));
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        await viewModel.RefreshAsync(snapshot.Day, TestContext.Current.CancellationToken);

        Assert.Contains("–", viewModel.NextActivityText);
        Assert.Contains("左右", viewModel.NextActivityText);
        Assert.Contains("还需约 30 分钟", viewModel.NextActivityText);
        Assert.Contains("依据有效工作累计", viewModel.NextActivityText);
        Assert.Contains(nameof(viewModel.NextActivityText), changed);
        Assert.Contains(nameof(viewModel.NextHydrationText), changed);
    }

    [Fact]
    public async Task HydrationRuntimeShowsIndependentRangeAndWallClockReason()
    {
        var rule = ReminderRule.CreateDefaultHydration(DefaultReminderRuleIds.Hydration) with
        {
            IntervalMinMinutes = 15,
            IntervalMaxMinutes = 20,
        };
        var runtime = new ReminderRuntimeState(
            rule.Id, Guid.NewGuid(), 1_200, 300,
            ReminderRuntimeStatus.Accumulating, 0, null, DateTimeOffset.UtcNow);
        var snapshot = new DashboardSnapshot(DateOnly.FromDateTime(DateTime.Today), [], 0, 0, 0, 0, TimeSpan.Zero)
        {
            HydrationRule = rule,
            HydrationRuntime = runtime,
        };
        var viewModel = new IkuyoPet.App.MainWindowViewModel(new FixedDashboard(snapshot));

        await viewModel.RefreshAsync(snapshot.Day, TestContext.Current.CancellationToken);

        Assert.Contains("–", viewModel.NextHydrationText);
        Assert.Contains("左右喝水", viewModel.NextHydrationText);
        Assert.Contains("依据墙钟累计", viewModel.NextHydrationText);
    }

    [Fact]
    public void TodayViewUsesSeparateActivityAndHydrationBindings()
    {
        var view = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "IkuyoPet.App", "Views", "TodayView.xaml"));

        Assert.Contains("NextActivityText", view);
        Assert.Contains("NextHydrationText", view);
        Assert.Contains("下一次离屏活动", view);
        Assert.Contains("下一次喝水", view);

        var windowCode = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "IkuyoPet.App", "MainWindow.xaml.cs"));
        Assert.Contains("RefreshTimeSensitiveDashboardText", windowCode);
    }

    private sealed class FixedDashboard(DashboardSnapshot snapshot) : IDashboardQueryService
    {
        public Task<DashboardSnapshot> GetAsync(DateOnly day, CancellationToken cancellationToken) =>
            Task.FromResult(snapshot);
    }
}
