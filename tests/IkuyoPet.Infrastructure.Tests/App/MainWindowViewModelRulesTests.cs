using System.IO;
using IkuyoPet.Core.Dashboard;
using IkuyoPet.Core.Reminders;
using IkuyoPet.Core.Storage;
using IkuyoPet.Core.WorkTracking;
using IkuyoPet.Infrastructure.Storage;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.App;

public sealed class MainWindowViewModelRulesTests
{
    [Fact]
    public async Task LoadSettingsInitializesDraftsFromPersistedRules()
    {
        using var database = new TemporaryDatabase();
        var repository = new SqliteEventRepository(database.ConnectionString);
        var custom = ReminderRule.CreateDefaultHydration(DefaultReminderRuleIds.Hydration) with
        {
            Message = "医生建议的喝水文案",
            IntervalMinMinutes = 20,
            IntervalMaxMinutes = 30,
        };
        await repository.UpsertReminderRuleAsync(custom, TestContext.Current.CancellationToken);

        var viewModel = new IkuyoPet.App.MainWindowViewModel(
            new EmptyDashboard(),
            repository: repository);

        await viewModel.LoadSettingsAsync(TestContext.Current.CancellationToken);

        Assert.Equal("医生建议的喝水文案", viewModel.WaterRule.Message);
        Assert.Equal("20", viewModel.WaterRule.IntervalMinMinutes);
        Assert.Equal("30", viewModel.WaterRule.IntervalMaxMinutes);
        Assert.True(viewModel.WaterRule.IsWallClock);
        Assert.Equal(
            DefaultReminderRuleIds.Activity,
            viewModel.ActivityRule.RuleId);
    }

    [Fact]
    public async Task SaveRulesCommandPersistsValidEdits()
    {
        using var database = new TemporaryDatabase();
        var repository = new SqliteEventRepository(database.ConnectionString);
        var viewModel = new IkuyoPet.App.MainWindowViewModel(
            new EmptyDashboard(),
            repository: repository);
        await viewModel.LoadSettingsAsync(TestContext.Current.CancellationToken);

        viewModel.WaterRule.IntervalMinMinutes = "25";
        viewModel.WaterRule.IntervalMaxMinutes = "35";
        viewModel.ActivityRule.IntervalMinMinutes = "50";
        viewModel.ActivityRule.IntervalMaxMinutes = "60";
        viewModel.ActivityRule.ActivityDurationMinutes = "8";
        await viewModel.SaveRulesAsync();

        var rules = await repository.ReadReminderRulesAsync(TestContext.Current.CancellationToken);
        var water = rules.Single(rule => ReminderKinds.IsWallClock(rule.Kind));
        var activity = rules.Single(rule => ReminderKinds.IsActiveWork(rule.Kind));
        Assert.Equal(25, water.IntervalMinMinutes);
        Assert.Equal(35, water.IntervalMaxMinutes);
        Assert.Equal("user", water.ParameterSource);
        Assert.Equal(50, activity.IntervalMinMinutes);
        Assert.Equal(60, activity.IntervalMaxMinutes);
        Assert.Equal(8, activity.ActivityDurationMinutes);
        Assert.Contains("已保存", viewModel.RulesStatus);
    }

    [Fact]
    public async Task SaveRulesCommandKeepsDatabaseUntouchedWhenValidationFails()
    {
        using var database = new TemporaryDatabase();
        var repository = new SqliteEventRepository(database.ConnectionString);
        var viewModel = new IkuyoPet.App.MainWindowViewModel(
            new EmptyDashboard(),
            repository: repository);
        await viewModel.LoadSettingsAsync(TestContext.Current.CancellationToken);
        var before = await repository.ReadReminderRulesAsync(TestContext.Current.CancellationToken);

        viewModel.WaterRule.IntervalMinMinutes = "45";
        viewModel.WaterRule.IntervalMaxMinutes = "20";
        await viewModel.SaveRulesAsync();

        var after = await repository.ReadReminderRulesAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            before.Select(rule => rule.IntervalMaxMinutes).Order(),
            after.Select(rule => rule.IntervalMaxMinutes).Order());
        Assert.Contains("未保存", viewModel.RulesStatus);
    }

    [Fact]
    public async Task BatchFailureRollsBackWaterActivityAndQuietHoursTogether()
    {
        using var database = new TemporaryDatabase();
        var baseline = new SqliteEventRepository(database.ConnectionString);
        var oldWater = ReminderRule.CreateDefaultHydration(DefaultReminderRuleIds.Hydration);
        var oldActivity = ReminderRule.CreateDefaultActiveWork(DefaultReminderRuleIds.Activity);
        await baseline.UpsertReminderRulesAsync([oldWater, oldActivity], TestContext.Current.CancellationToken);
        var failing = new SqliteEventRepository(
            database.ConnectionString,
            batchFailureInjector: (index, _) => index == 1 ? new IOException("injected batch failure") : null);
        var newQuiet = new QuietHours(new TimeOnly(22, 0), new TimeOnly(7, 0), true);
        var newWater = oldWater with { IntervalMinMinutes = 25, QuietHours = newQuiet };
        var newActivity = oldActivity with { IntervalMinMinutes = 55, QuietHours = newQuiet };

        await Assert.ThrowsAsync<IOException>(() => failing.UpsertReminderRulesAsync(
            [newWater, newActivity], TestContext.Current.CancellationToken));

        var after = await baseline.ReadReminderRulesAsync(TestContext.Current.CancellationToken);
        Assert.Equal(oldWater.IntervalMinMinutes, after.Single(rule => rule.Id == oldWater.Id).IntervalMinMinutes);
        Assert.Equal(oldActivity.IntervalMinMinutes, after.Single(rule => rule.Id == oldActivity.Id).IntervalMinMinutes);
        Assert.False(after.Single(rule => rule.Id == oldWater.Id).QuietHours.Enabled);
        Assert.False(after.Single(rule => rule.Id == oldActivity.Id).QuietHours.Enabled);
    }


    [Fact]
    public async Task ManualActionCommandsRaiseWaterAndActivityRequests()
    {
        var viewModel = new IkuyoPet.App.MainWindowViewModel(new EmptyDashboard());
        var requests = new List<IkuyoPet.App.ManualReminderRequestedEventArgs>();
        var received = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.ManualReminderRequested += request =>
        {
            requests.Add(request);
            received.TrySetResult(null);
            return Task.CompletedTask;
        };

        viewModel.ManualWaterCommand.Execute(null);
        await received.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        received = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.ManualActivityCommand.Execute(null);
        await received.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Collection(
            requests,
            water => Assert.Equal("water", water.Kind),
            activity => Assert.Equal("activity", activity.Kind));
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
            $"ikuyo-pet-rules-{Guid.NewGuid():N}.db");

        public string ConnectionString => $"Data Source={path};Pooling=False";

        public void Dispose()
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + "-wal")) File.Delete(path + "-wal");
            if (File.Exists(path + "-shm")) File.Delete(path + "-shm");
        }
    }
}
