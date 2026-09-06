using System.Globalization;
using IkuyoPet.Core.Reminders;
using IkuyoPet.Core.WorkTracking;
using IkuyoPet.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.Storage;

public sealed class SqliteEventRepositoryTests
{
    [Fact]
    public async Task SavesAndReadsReminderOutcomeWithoutWindowContent()
    {
        await using var database = TestDatabase.CreateInMemory();
        var repository = new SqliteEventRepository(database.ConnectionString);
        var item = ReminderEvent.Completed(
            Guid.NewGuid(),
            DateTimeOffset.Parse("2026-09-06T10:20:00+08:00", CultureInfo.InvariantCulture),
            "pet");

        await repository.AppendReminderAsync(item, TestContext.Current.CancellationToken);
        var stored = await repository.ReadReminderEventsAsync(
            DateOnly.Parse("2026-09-06", CultureInfo.InvariantCulture),
            TestContext.Current.CancellationToken);

        Assert.Single(stored);
        Assert.Equal(ReminderOutcome.Completed, stored[0].Outcome);
    }

    [Fact]
    public async Task SavesWorkSessionAgainstTrackedApplication()
    {
        await using var database = TestDatabase.CreateInMemory();
        var repository = new SqliteEventRepository(database.ConnectionString);
        var start = new DateTimeOffset(2026, 9, 6, 10, 0, 0, TimeSpan.FromHours(8));
        var session = new WorkSession(
            Guid.NewGuid(),
            "pycharm64",
            "PyCharm",
            start,
            start.AddSeconds(5),
            5,
            "stopped");

        await repository.AppendWorkSessionAsync(session, TestContext.Current.CancellationToken);

        await using var connection = new SqliteConnection(database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.process_name, a.display_name, s.active_seconds, s.end_reason
            FROM work_sessions AS s
            INNER JOIN tracked_apps AS a ON a.id = s.tracked_app_id;
            """;
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal("pycharm64", reader.GetString(0));
        Assert.Equal("PyCharm", reader.GetString(1));
        Assert.Equal(5, reader.GetInt32(2));
        Assert.Equal("stopped", reader.GetString(3));
    }
    [Fact]
    public async Task MigrationIsIdempotentAndCreatesTheFiveLocalTables()
    {
        await using var database = TestDatabase.CreateInMemory();
        var migrator = new DatabaseMigrator(database.ConnectionString);

        await migrator.MigrateAsync(TestContext.Current.CancellationToken);
        await migrator.MigrateAsync(TestContext.Current.CancellationToken);

        await using var connection = new SqliteConnection(database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND name IN ('reminder_rules', 'reminder_events', 'tracked_apps', 'work_sessions', 'app_settings');
            """;

        Assert.Equal(5L, (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!);
    }

    [Fact]
    public async Task ReadsOnlyEventsScheduledOnRequestedLocalDate()
    {
        await using var database = TestDatabase.CreateInMemory();
        var repository = new SqliteEventRepository(database.ConnectionString);
        var first = ReminderEvent.Completed(
            Guid.NewGuid(),
            LocalAt(2026, 9, 6, 23, 59),
            "pet");
        var nextDay = ReminderEvent.Completed(
            Guid.NewGuid(),
            LocalAt(2026, 9, 7, 0, 1),
            "notification");

        await repository.AppendReminderAsync(first, TestContext.Current.CancellationToken);
        await repository.AppendReminderAsync(nextDay, TestContext.Current.CancellationToken);

        var stored = await repository.ReadReminderEventsAsync(
            DateOnly.Parse("2026-09-06", CultureInfo.InvariantCulture),
            TestContext.Current.CancellationToken);

        var only = Assert.Single(stored);
        Assert.Equal(first.Id, only.Id);
    }

    [Fact]
    public async Task HonorsCancellationBeforeOpeningDatabase()
    {
        await using var database = TestDatabase.CreateInMemory();
        var repository = new SqliteEventRepository(database.ConnectionString);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        #pragma warning disable xUnit1051
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            repository.ReadReminderEventsAsync(
                DateOnly.Parse("2026-09-06", CultureInfo.InvariantCulture),
                cancellation.Token));
        #pragma warning restore xUnit1051
    }

    [Fact]
    public async Task ReminderEventsSchemaDoesNotContainWindowTitle()
    {
        await using var database = TestDatabase.CreateInMemory();
        await new DatabaseMigrator(database.ConnectionString).MigrateAsync(TestContext.Current.CancellationToken);

        await using var connection = new SqliteConnection(database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(reminder_events);";
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var columns = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            columns.Add(reader.GetString(1));
        }

        Assert.DoesNotContain("window_title", columns);
        Assert.DoesNotContain("window_content", columns);
    }

    private static DateTimeOffset LocalAt(int year, int month, int day, int hour, int minute)
    {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }
    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private TestDatabase(SqliteConnection connection)
        {
            _connection = connection;
        }

        public string ConnectionString => _connection.ConnectionString;

        public static TestDatabase CreateInMemory()
        {
            var name = $"ikuyo-pet-tests-{Guid.NewGuid():N}";
            var connection = new SqliteConnection(
                $"Data Source=file:{name};Mode=Memory;Cache=Shared");
            connection.Open();
            return new TestDatabase(connection);
        }

        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }
}