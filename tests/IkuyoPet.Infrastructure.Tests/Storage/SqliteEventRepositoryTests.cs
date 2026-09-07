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
    public async Task AppendingSessionPreservesDisabledTrackedApplication()
    {
        await using var database = TestDatabase.CreateInMemory();
        var repository = new SqliteEventRepository(database.ConnectionString);
        var application = new TrackedApplication("pycharm64", "PyCharm", false);
        var start = LocalAt(2026, 9, 6, 10, 0);
        var session = new WorkSession(
            Guid.NewGuid(),
            application.ProcessName,
            application.DisplayName,
            start,
            start.AddMinutes(1),
            60,
            "stopped");

        await repository.UpsertTrackedApplicationAsync(
            application,
            TestContext.Current.CancellationToken);
        await repository.AppendWorkSessionAsync(
            session,
            TestContext.Current.CancellationToken);
        var stored = await repository.ReadTrackedApplicationsAsync(
            TestContext.Current.CancellationToken);

        Assert.False(Assert.Single(stored).Enabled);
    }

    [Fact]
    public async Task UpsertsAndReadsReminderRules()
    {
        await using var database = TestDatabase.CreateInMemory();
        var repository = new SqliteEventRepository(database.ConnectionString);
        var id = Guid.NewGuid();
        var original = new ReminderRule(
            id,
            "water",
            "喝点水吧",
            new TimeOnly(9, 0),
            new TimeOnly(18, 0),
            45,
            true);
        var updated = original with
        {
            Message = "该补水啦",
            IntervalMinutes = 60,
            Enabled = false,
        };

        await repository.UpsertReminderRuleAsync(original, TestContext.Current.CancellationToken);
        await repository.UpsertReminderRuleAsync(updated, TestContext.Current.CancellationToken);
        var stored = await repository.ReadReminderRulesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(updated, Assert.Single(stored));
    }

    [Fact]
    public async Task ReadsWorkSessionsOnlyForRequestedLocalDate()
    {
        await using var database = TestDatabase.CreateInMemory();
        var repository = new SqliteEventRepository(database.ConnectionString);
        var requested = new WorkSession(
            Guid.NewGuid(),
            "pycharm64",
            "PyCharm",
            LocalAt(2026, 9, 6, 10, 0),
            LocalAt(2026, 9, 6, 10, 1),
            60,
            "app-switched");
        var nextDay = new WorkSession(
            Guid.NewGuid(),
            "code",
            "Visual Studio Code",
            LocalAt(2026, 9, 7, 0, 1),
            LocalAt(2026, 9, 7, 0, 2),
            60,
            "stopped");

        await repository.AppendWorkSessionAsync(requested, TestContext.Current.CancellationToken);
        await repository.AppendWorkSessionAsync(nextDay, TestContext.Current.CancellationToken);
        var stored = await repository.ReadWorkSessionsAsync(
            new DateOnly(2026, 9, 6),
            TestContext.Current.CancellationToken);

        var session = Assert.Single(stored);
        Assert.Equal(requested.Id, session.Id);
        Assert.Equal(requested.ProcessName, session.ProcessName);
        Assert.Equal(requested.DisplayName, session.DisplayName);
        Assert.Equal(requested.StartedAt.ToUniversalTime(), session.StartedAt);
        Assert.Equal(requested.EndedAt.ToUniversalTime(), session.EndedAt);
        Assert.Equal(requested.ActiveSeconds, session.ActiveSeconds);
        Assert.Equal(requested.EndReason, session.EndReason);
    }

    [Fact]
    public async Task ReadsCrossMidnightSessionForBothIntersectingLocalDays()
    {
        await using var database = TestDatabase.CreateInMemory();
        var repository = new SqliteEventRepository(database.ConnectionString);
        var session = new WorkSession(
            Guid.NewGuid(),
            "pycharm64",
            "PyCharm",
            LocalAt(2026, 9, 6, 23, 59),
            LocalAt(2026, 9, 7, 0, 1),
            120,
            "stopped");

        await repository.AppendWorkSessionAsync(session, TestContext.Current.CancellationToken);
        var firstDay = await repository.ReadWorkSessionsAsync(
            new DateOnly(2026, 9, 6),
            TestContext.Current.CancellationToken);
        var secondDay = await repository.ReadWorkSessionsAsync(
            new DateOnly(2026, 9, 7),
            TestContext.Current.CancellationToken);

        Assert.Equal(session.Id, Assert.Single(firstDay).Id);
        Assert.Equal(session.Id, Assert.Single(secondDay).Id);
    }

    [Theory]
    [InlineData(2026, 3, 8, 23)]
    [InlineData(2026, 11, 1, 25)]
    public async Task ReadsOnlySessionsIntersectingInjectedDstDay(
        int year,
        int month,
        int dayOfMonth,
        int expectedHours)
    {
        await using var database = TestDatabase.CreateInMemory();
        var zone = CreateEasternTimeZone();
        var repository = new SqliteEventRepository(database.ConnectionString, zone);
        var day = new DateOnly(year, month, dayOfMonth);
        var start = AtStartOfDay(day, zone);
        var end = AtStartOfDay(day.AddDays(1), zone);
        var requested = new WorkSession(
            Guid.NewGuid(),
            "pycharm64",
            "PyCharm",
            start,
            end,
            expectedHours * 60 * 60,
            "stopped");
        var before = requested with
        {
            Id = Guid.NewGuid(),
            StartedAt = start.AddMinutes(-1),
            EndedAt = start,
            ActiveSeconds = 60,
        };
        var after = requested with
        {
            Id = Guid.NewGuid(),
            StartedAt = end,
            EndedAt = end.AddMinutes(1),
            ActiveSeconds = 60,
        };

        await repository.AppendWorkSessionAsync(before, TestContext.Current.CancellationToken);
        await repository.AppendWorkSessionAsync(requested, TestContext.Current.CancellationToken);
        await repository.AppendWorkSessionAsync(after, TestContext.Current.CancellationToken);
        var stored = await repository.ReadWorkSessionsAsync(day, TestContext.Current.CancellationToken);

        Assert.Equal(requested.Id, Assert.Single(stored).Id);
        Assert.Equal(TimeSpan.FromHours(expectedHours), requested.EndedAt - requested.StartedAt);
    }

    [Fact]
    public async Task RejectsDuplicateWorkSessionDomainId()
    {
        await using var database = TestDatabase.CreateInMemory();
        var repository = new SqliteEventRepository(database.ConnectionString);
        var start = LocalAt(2026, 9, 6, 10, 0);
        var session = new WorkSession(
            Guid.NewGuid(),
            "pycharm64",
            "PyCharm",
            start,
            start.AddMinutes(1),
            60,
            "stopped");

        await repository.AppendWorkSessionAsync(session, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<SqliteException>(() =>
            repository.AppendWorkSessionAsync(session, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpsertsAndReadsTrackedApplications()
    {
        await using var database = TestDatabase.CreateInMemory();
        var repository = new SqliteEventRepository(database.ConnectionString);
        var original = new TrackedApplication("pycharm64", "PyCharm", true);
        var updated = original with { DisplayName = "PyCharm 2026", Enabled = false };

        await repository.UpsertTrackedApplicationAsync(original, TestContext.Current.CancellationToken);
        await repository.UpsertTrackedApplicationAsync(updated, TestContext.Current.CancellationToken);
        var stored = await repository.ReadTrackedApplicationsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(updated, Assert.Single(stored));
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
    public async Task ConcurrentMigrationCallsCompleteAgainstSameDatabase()
    {
        await using var database = TestDatabase.CreateInMemory();
        var migrations = Enumerable.Range(0, 20)
            .Select(_ => new DatabaseMigrator(database.ConnectionString)
                .MigrateAsync(TestContext.Current.CancellationToken));

        await Task.WhenAll(migrations);

        await using var command = database.Connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'work_sessions';";
        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!);
    }

    [Fact]
    public async Task FailedMigrationCanBeRetried()
    {
        await using var database = TestDatabase.CreateInMemory();
        await using (var createConflict = database.Connection.CreateCommand())
        {
            createConflict.CommandText = "CREATE VIEW work_sessions AS SELECT 1 AS value;";
            await createConflict.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var migrator = new DatabaseMigrator(database.ConnectionString);
        await Assert.ThrowsAsync<SqliteException>(() =>
            migrator.MigrateAsync(TestContext.Current.CancellationToken));

        await using (var removeConflict = database.Connection.CreateCommand())
        {
            removeConflict.CommandText = "DROP VIEW work_sessions;";
            await removeConflict.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await migrator.MigrateAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task MigrationAddsDomainIdToExistingWorkSessions()
    {
        await using var database = TestDatabase.CreateInMemory();
        await using (var createLegacy = database.Connection.CreateCommand())
        {
            createLegacy.CommandText = """
                CREATE TABLE work_sessions (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    tracked_app_id INTEGER NOT NULL,
                    started_at TEXT NOT NULL,
                    ended_at TEXT NULL,
                    active_seconds INTEGER NOT NULL,
                    end_reason TEXT NULL
                );
                INSERT INTO work_sessions
                    (tracked_app_id, started_at, ended_at, active_seconds, end_reason)
                VALUES
                    (1, '2026-09-06T02:00:00.0000000+00:00',
                     '2026-09-06T02:01:00.0000000+00:00', 60, 'legacy');
                """;
            await createLegacy.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await new DatabaseMigrator(database.ConnectionString)
            .MigrateAsync(TestContext.Current.CancellationToken);

        await using var command = database.Connection.CreateCommand();
        command.CommandText = "SELECT domain_id FROM work_sessions WHERE id = 1;";
        Assert.Equal(
            "00000000000000000000000000000001",
            (string)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!);
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

    private static DateTimeOffset AtStartOfDay(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private static TimeZoneInfo CreateEasternTimeZone()
    {
        var daylightStart = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0),
            3,
            2,
            DayOfWeek.Sunday);
        var daylightEnd = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0),
            11,
            1,
            DayOfWeek.Sunday);
        var adjustment = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2020, 1, 1),
            new DateTime(2030, 12, 31),
            TimeSpan.FromHours(1),
            daylightStart,
            daylightEnd);
        return TimeZoneInfo.CreateCustomTimeZone(
            "IkuyoPet-Test-Eastern",
            TimeSpan.FromHours(-5),
            "IkuyoPet Test Eastern",
            "IkuyoPet Test Eastern Standard",
            "IkuyoPet Test Eastern Daylight",
            [adjustment]);
    }
    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private TestDatabase(SqliteConnection connection)
        {
            _connection = connection;
        }

        public string ConnectionString => _connection.ConnectionString;

        public SqliteConnection Connection => _connection;

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
