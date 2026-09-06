using Microsoft.Data.Sqlite;

namespace IkuyoPet.Infrastructure.Storage;

public sealed class DatabaseMigrator(string connectionString)
{
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;", cancellationToken);
        await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken);
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
            """, cancellationToken);
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