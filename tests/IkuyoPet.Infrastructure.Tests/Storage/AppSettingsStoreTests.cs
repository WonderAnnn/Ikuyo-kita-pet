using System.IO;
using IkuyoPet.Infrastructure.Storage;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.Storage;

public sealed class AppSettingsStoreTests
{
    [Fact]
    public async Task MissingSettingReturnsNull()
    {
        using var database = new TemporaryDatabase();
        var store = new AppSettingsStore(database.ConnectionString);

        var value = await store.ReadAsync("pet.enabled", TestContext.Current.CancellationToken);

        Assert.Null(value);
    }

    [Fact]
    public async Task SettingRoundTripsAndLatestValueWins()
    {
        using var database = new TemporaryDatabase();
        var store = new AppSettingsStore(database.ConnectionString);

        await store.WriteAsync("pet.enabled", "false", TestContext.Current.CancellationToken);
        await store.WriteAsync("pet.enabled", "true", TestContext.Current.CancellationToken);

        var value = await store.ReadAsync("pet.enabled", TestContext.Current.CancellationToken);

        Assert.Equal("true", value);
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        private readonly string path = Path.Combine(
            Path.GetTempPath(),
            $"ikuyo-pet-settings-{Guid.NewGuid():N}.db");

        public string ConnectionString => $"Data Source={path};Pooling=False";

        public void Dispose()
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + "-wal")) File.Delete(path + "-wal");
            if (File.Exists(path + "-shm")) File.Delete(path + "-shm");
        }
    }
}