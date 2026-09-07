using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;

namespace IkuyoPet.Infrastructure.Storage;

public sealed class DatabaseMigrator
{
    private static readonly ConcurrentDictionary<string, Lazy<Task>> Migrations =
        new(StringComparer.Ordinal);
    private readonly string _migrationKey;
    private readonly Func<Task> _migrateCoreAsync;

    public DatabaseMigrator(string connectionString)
        : this(connectionString, () => MigrateCoreAsync(connectionString))
    {
    }

    internal DatabaseMigrator(string connectionString, Func<Task> migrateCoreAsync)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(migrateCoreAsync);
        _migrationKey = GetMigrationKey(connectionString);
        _migrateCoreAsync = migrateCoreAsync;
    }

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var migration = Migrations.GetOrAdd(
            _migrationKey,
            _ => CreateMigration(_migrationKey, _migrateCoreAsync));
        await migration.Value.WaitAsync(cancellationToken);
    }

    private static Lazy<Task> CreateMigration(string key, Func<Task> migrateCoreAsync)
    {
        Lazy<Task>? migration = null;
        migration = new Lazy<Task>(
            () => RunMigrationAsync(key, migration!, migrateCoreAsync),
            LazyThreadSafetyMode.ExecutionAndPublication);
        return migration;
    }

    private static async Task RunMigrationAsync(
        string key,
        Lazy<Task> migration,
        Func<Task> migrateCoreAsync)
    {
        try
        {
            await migrateCoreAsync();
        }
        catch
        {
            ((ICollection<KeyValuePair<string, Lazy<Task>>>)Migrations)
                .Remove(new KeyValuePair<string, Lazy<Task>>(key, migration));
            throw;
        }
    }

    private static async Task MigrateCoreAsync(string value)
    {
        await using var connection = new SqliteConnection(value);
        await connection.OpenAsync(CancellationToken.None);

        await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;", CancellationToken.None);
        await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;", CancellationToken.None);
        await using var transaction = connection.BeginTransaction(deferred: false);
        await ExecuteAsync(connection, transaction, """
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

        if (!await HasColumnAsync(connection, transaction, "work_sessions", "domain_id"))
        {
            await ExecuteAsync(
                connection,
                transaction,
                "ALTER TABLE work_sessions ADD COLUMN domain_id TEXT NULL;",
                CancellationToken.None);
        }

        await ExecuteAsync(connection, transaction, """
            UPDATE work_sessions
            SET domain_id = printf('%032x', id)
            WHERE domain_id IS NULL OR domain_id = '';

            CREATE UNIQUE INDEX IF NOT EXISTS ux_work_sessions_domain_id
                ON work_sessions (domain_id);
            """, CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
    }

    private static async Task<bool> HasColumnAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string column)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
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

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string GetMigrationKey(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        var dataSource = builder.DataSource;
        if (builder.Mode == SqliteOpenMode.Memory ||
            string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return $"memory|{dataSource}|cache={builder.Cache}";
        }

        if (Uri.TryCreate(dataSource, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            dataSource = uri.LocalPath;
        }

        var fullPath = Path.GetFullPath(dataSource);
        if (OperatingSystem.IsWindows())
        {
            fullPath = fullPath.ToUpperInvariant();
        }

        return $"file|{fullPath}";
    }
}
