using IkuyoPet.Core.Reminders;
using IkuyoPet.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.Storage;

public sealed class ReminderRuntimeRepositoryTests
{
    [Fact]
    public async Task RuntimeStateRoundTripsAndCanBeOverwritten()
    {
        await using var database = TestDatabase.CreateInMemory();
        var repository = new SqliteEventRepository(database.ConnectionString);
        var rule = ReminderRule.CreateDefaultActiveWork(Guid.NewGuid());
        await repository.UpsertReminderRuleAsync(rule, TestContext.Current.CancellationToken);
        var original = new ReminderRuntimeState(
            rule.Id,
            Guid.NewGuid(),
            2_700,
            1_200,
            ReminderRuntimeStatus.Accumulating,
            0,
            null,
            new DateTimeOffset(2026, 9, 9, 8, 20, 0, TimeSpan.Zero));
        var updated = original with
        {
            AccumulatedActiveSeconds = 2_700,
            Status = ReminderRuntimeStatus.Due,
            Attempt = 1,
            UpdatedAt = original.UpdatedAt.AddMinutes(25),
        };

        await repository.UpsertReminderRuntimeStateAsync(original, TestContext.Current.CancellationToken);
        await repository.UpsertReminderRuntimeStateAsync(updated, TestContext.Current.CancellationToken);
        var stored = await repository.ReadReminderRuntimeStateAsync(
            rule.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(updated, stored);
    }

    [Fact]
    public async Task ConditionalRuntimeUpdateRejectsStaleSampler()
    {
        await using var database = TestDatabase.CreateInMemory();
        var repository = new SqliteEventRepository(database.ConnectionString);
        var rule = ReminderRule.CreateDefaultActiveWork(Guid.NewGuid());
        await repository.UpsertReminderRuleAsync(rule, TestContext.Current.CancellationToken);
        var original = new ReminderRuntimeState(
            rule.Id, Guid.NewGuid(), 2_700, 1_200,
            ReminderRuntimeStatus.Accumulating, 0, null,
            new DateTimeOffset(2026, 9, 9, 8, 20, 0, TimeSpan.Zero));
        var concurrent = original with
        {
            AccumulatedActiveSeconds = 1_500,
            UpdatedAt = original.UpdatedAt.AddSeconds(30),
        };
        var staleNext = original with
        {
            AccumulatedActiveSeconds = 1_300,
            UpdatedAt = original.UpdatedAt.AddSeconds(15),
        };

        await repository.UpsertReminderRuntimeStateAsync(original, TestContext.Current.CancellationToken);
        await repository.UpsertReminderRuntimeStateAsync(concurrent, TestContext.Current.CancellationToken);

        Assert.False(await repository.TryUpdateReminderRuntimeStateAsync(
            original, staleNext, TestContext.Current.CancellationToken));
        Assert.Equal(concurrent, await repository.ReadReminderRuntimeStateAsync(
            rule.Id, TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task ExtendedRuleFieldsRoundTrip()
    {
        await using var database = TestDatabase.CreateInMemory();
        var repository = new SqliteEventRepository(database.ConnectionString);
        var rule = ReminderRule.CreateDefaultActiveWork(Guid.NewGuid()) with
        {
            IntervalMinMinutes = 42,
            IntervalMaxMinutes = 48,
            ActivityDurationMinutes = 7,
            ParameterSource = "clinician",
            ParameterVersion = "doctor-2026-09-10",
        };

        await repository.UpsertReminderRuleAsync(rule, TestContext.Current.CancellationToken);
        var stored = Assert.Single(
            await repository.ReadReminderRulesAsync(TestContext.Current.CancellationToken));

        Assert.Equal(42, stored.IntervalMinMinutes);
        Assert.Equal(48, stored.IntervalMaxMinutes);
        Assert.Equal(7, stored.ActivityDurationMinutes);
        Assert.Equal("clinician", stored.ParameterSource);
        Assert.Equal("doctor-2026-09-10", stored.ParameterVersion);
    }

    [Fact]
    public async Task MigratesLegacyRuleUsingSingleIntervalAsBothBounds()
    {
        await using var database = TestDatabase.CreateInMemory();
        var id = Guid.NewGuid();
        await using (var command = database.Connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE reminder_rules (
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    kind TEXT NOT NULL,
                    enabled INTEGER NOT NULL,
                    start_local TEXT NOT NULL,
                    end_local TEXT NOT NULL,
                    interval_minutes INTEGER NOT NULL,
                    daily_goal INTEGER NOT NULL DEFAULT 0,
                    quiet_start TEXT NOT NULL,
                    quiet_end TEXT NOT NULL,
                    quiet_enabled INTEGER NOT NULL DEFAULT 0,
                    max_retries INTEGER NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
                INSERT INTO reminder_rules
                    (id, name, kind, enabled, start_local, end_local, interval_minutes,
                     daily_goal, quiet_start, quiet_end, quiet_enabled, max_retries,
                     created_at, updated_at)
                VALUES
                    ($id, '旧规则', 'activity', 1, '08:00:00', '23:00:00', 45,
                     0, '00:00:00', '00:00:00', 0, 3,
                     '2026-09-01T00:00:00Z', '2026-09-01T00:00:00Z');
                """;
            command.Parameters.AddWithValue("$id", id.ToString("N"));
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var repository = new SqliteEventRepository(database.ConnectionString);
        var stored = Assert.Single(
            await repository.ReadReminderRulesAsync(TestContext.Current.CancellationToken));

        Assert.Equal(45, stored.IntervalMinutes);
        Assert.Equal(45, stored.IntervalMinMinutes);
        Assert.Equal(45, stored.IntervalMaxMinutes);
        Assert.Equal(5, stored.ActivityDurationMinutes);
        Assert.Equal("legacy", stored.ParameterSource);
        Assert.Equal("legacy", stored.ParameterVersion);
    }

    [Fact]
    public async Task ReminderEventKeepsActivityParameterSnapshot()
    {
        await using var database = TestDatabase.CreateInMemory();
        var repository = new SqliteEventRepository(database.ConnectionString);
        var now = new DateTimeOffset(2026, 9, 9, 9, 0, 0, TimeSpan.Zero);
        var item = ReminderEvent.Completed(Guid.NewGuid(), now, "pet") with
        {
            ActivityDurationMinutes = 5,
            ParameterSource = "general-default",
            ParameterVersion = "2026-09-09",
        };

        await repository.AppendReminderAsync(item, TestContext.Current.CancellationToken);
        var stored = await repository.ReadReminderEventAsync(
            item.Id,
            TestContext.Current.CancellationToken);

        Assert.NotNull(stored);
        Assert.Equal(5, stored.ActivityDurationMinutes);
        Assert.Equal("general-default", stored.ParameterSource);
        Assert.Equal("2026-09-09", stored.ParameterVersion);
    }

    [Fact]
    public async Task DefaultActivityRuleIsInsertedOnlyWhenRuleTableIsEmpty()
    {
        await using var database = TestDatabase.CreateInMemory();
        var repository = new SqliteEventRepository(database.ConnectionString);
        var first = ReminderRule.CreateDefaultActiveWork(Guid.NewGuid());
        var second = ReminderRule.CreateDefaultActiveWork(Guid.NewGuid());

        Assert.True(await repository.TryAddDefaultReminderRuleAsync(
            first,
            TestContext.Current.CancellationToken));
        Assert.False(await repository.TryAddDefaultReminderRuleAsync(
            second,
            TestContext.Current.CancellationToken));

        var stored = Assert.Single(
            await repository.ReadReminderRulesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(first.Id, stored.Id);
        Assert.Equal(40, stored.IntervalMinMinutes);
        Assert.Equal(50, stored.IntervalMaxMinutes);
        Assert.Equal(5, stored.ActivityDurationMinutes);
        Assert.Equal("general-default", stored.ParameterSource);
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private TestDatabase(SqliteConnection connection)
        {
            this.connection = connection;
        }

        public string ConnectionString => connection.ConnectionString;

        public SqliteConnection Connection => connection;

        public static TestDatabase CreateInMemory()
        {
            var name = $"ikuyo-pet-runtime-tests-{Guid.NewGuid():N}";
            var connection = new SqliteConnection(
                $"Data Source=file:{name};Mode=Memory;Cache=Shared");
            connection.Open();
            return new TestDatabase(connection);
        }

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }
}
