using System.IO;
using IkuyoPet.Core.Dashboard;
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