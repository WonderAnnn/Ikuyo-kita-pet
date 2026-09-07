using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;

namespace IkuyoPet.Infrastructure.Storage;

public sealed class DatabaseMigrator(string connectionString)
{
    private static readonly ConcurrentDictionary<string, Lazy<Task>> Migrations =
        new(StringComparer.Ordinal);

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var migration = Migrations.GetOrAdd(
            connectionString,
            static value => new Lazy<Task>(
                () => MigrateCoreAsync(value),
                LazyThreadSafetyMode.ExecutionAndPublication));
        var migrationTask = migration.Value;

        try
        {
            await migrationTask.WaitAsync(cancellationToken);
        }
        catch
        {
            if (migrationTask.IsFaulted || migrationTask.IsCanceled)
            {
                if (Migrations.TryGetValue(connectionString, out var cached) &&
                    ReferenceEquals(cached, migration))
                {
                    Migrations.TryRemove(connectionString, out _);
                }
            }

            throw;
        }
    }

    private static async Task MigrateCoreAsync(string value)
    {
        await using var connection = new SqliteConnection(value);
        await connection.OpenAsync(CancellationToken.None);

        await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;", CancellationToken.None);
        await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;", CancellationToken.None);
        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS reminder_rules (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                kind TEXT NOT NULL,
                enabled INTEGER NOT NULL,
                start_local TEXT NOT NULL,
                end_local TEXT NOT NULL,
                interval_minutes INTEGER NOT NULL,
                quiet_start TEXT NOT NULL,
                quiet_end TEXT NOT NULL,
                max_retries INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS reminder_events (
                id TEXT PRIMARY KEY,
                rule_id TEXT NULL REFERENCES reminder_rules(id),
                scheduled_at TEXT NOT NULL,
                displayed_at TEXT NULL,
                channel TEXT NOT NULL,
                action TEXT NOT NULL CHECK (action IN ('none', 'completed', 'snoozed', 'skipped', 'unanswered', 'suppressed')),
                action_at TEXT NULL,
                retry_index INTEGER NOT NULL,
                suppressed_reason TEXT NULL,
                created_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS tracked_apps (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                process_name TEXT NOT NULL UNIQUE,
                display_name TEXT NOT NULL,
                enabled INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS work_sessions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                domain_id TEXT NOT NULL,
                tracked_app_id INTEGER NOT NULL REFERENCES tracked_apps(id),
                started_at TEXT NOT NULL,
                ended_at TEXT NULL,
                active_seconds INTEGER NOT NULL,
                end_reason TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS app_settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_reminder_events_scheduled_at
                ON reminder_events (scheduled_at);
            CREATE INDEX IF NOT EXISTS ix_work_sessions_started_at
                ON work_sessions (started_at);
            """, CancellationToken.None);

        if (!await HasColumnAsync(connection, "work_sessions", "domain_id"))
        {
            await ExecuteAsync(
                connection,
                "ALTER TABLE work_sessions ADD COLUMN domain_id TEXT NULL;",
                CancellationToken.None);
        }

        await ExecuteAsync(connection, """
            UPDATE work_sessions
            SET domain_id = printf('%032x', id)
            WHERE domain_id IS NULL OR domain_id = '';

            CREATE UNIQUE INDEX IF NOT EXISTS ux_work_sessions_domain_id
                ON work_sessions (domain_id);
            """, CancellationToken.None);
    }

    private static async Task<bool> HasColumnAsync(
        SqliteConnection connection,
        string table,
        string column)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1
            FROM pragma_table_info($table)
            WHERE name = $column
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$table", table);
        command.Parameters.AddWithValue("$column", column);
        return await command.ExecuteScalarAsync(CancellationToken.None) is not null;
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
