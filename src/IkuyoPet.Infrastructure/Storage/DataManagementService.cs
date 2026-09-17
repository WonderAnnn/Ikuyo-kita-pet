using System.Globalization;
using System.Text.Json;
using IkuyoPet.Core.Storage;
using Microsoft.Data.Sqlite;

namespace IkuyoPet.Infrastructure.Storage;

/// <summary>
/// Backup, restore and full-data export over the embedded SQLite database.
/// Backups use "VACUUM INTO", which produces a transactionally consistent,
/// compact copy while the app keeps running. Restore replaces the live file and
/// must run only after the background loops have stopped.
/// </summary>
public sealed class DataManagementService
{
    public const string BackupFilePattern = "ikuyo-pet-*.db";
    public const string RestoreSafetyPrefix = "pre-restore-";
    public const string RetentionSettingKey = "backup.retention";
    public const int DefaultRetentionCount = 14;
    public const int RetentionLowerBound = 1;
    public const int RetentionUpperBound = 365;

    private readonly string databasePath;
    private readonly string backupDirectory;
    private readonly AppSettingsStore? settingsStore;

    public DataManagementService(
        string databasePath,
        string backupDirectory,
        AppSettingsStore? settingsStore = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(backupDirectory);
        this.databasePath = Path.GetFullPath(databasePath);
        this.backupDirectory = backupDirectory;
        this.settingsStore = settingsStore;
    }

    public string BackupDirectory => backupDirectory;

    public async Task BackupToAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var destination = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination))
        {
            File.Delete(destination);
        }

        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder($"Data Source={databasePath}") { Mode = SqliteOpenMode.ReadOnly }.ToString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "VACUUM INTO $path;";
        command.Parameters.AddWithValue("$path", destination);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Runs the automatic startup backup: one backup per local date, keeping
    /// the most recent <paramref name="retentionCount"/> daily files. Missing
    /// settings fall back to the default retention.
    /// </summary>
    public async Task<StartupBackupResult> RunStartupBackupAsync(
        DateOnly today,
        int? retentionOverride = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(backupDirectory);
        var retention = Math.Clamp(
            retentionOverride ?? await ReadRetentionCountAsync(cancellationToken),
            RetentionLowerBound,
            RetentionUpperBound);
        var target = Path.Combine(backupDirectory, $"ikuyo-pet-{today:yyyyMMdd}.db");
        if (File.Exists(target))
        {
            return new StartupBackupResult(false, target, retention, PruneDailyBackups(retention));
        }

        await BackupToAsync(target, cancellationToken);
        return new StartupBackupResult(true, target, retention, PruneDailyBackups(retention));
    }

    public int PruneDailyBackups(int retentionCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(retentionCount, 1);
        var deleted = 0;
        foreach (var file in ListDailyBackups().Skip(retentionCount))
        {
            TryDelete(file.FullName);
            deleted++;
        }

        return deleted;
    }

    public IReadOnlyList<FileInfo> ListDailyBackups()
    {
        if (!Directory.Exists(backupDirectory)) return [];
        return Directory
            .EnumerateFiles(backupDirectory, BackupFilePattern)
            .Select(path => new FileInfo(path))
            .Where(file => file.Name.StartsWith("ikuyo-pet-", StringComparison.Ordinal) &&
                file.Name.EndsWith(".db", StringComparison.Ordinal) &&
                !file.Name.Contains(RestoreSafetyPrefix, StringComparison.Ordinal))
            .OrderByDescending(file => file.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<int> ReadRetentionCountAsync(CancellationToken cancellationToken = default)
    {
        if (settingsStore is null) return DefaultRetentionCount;
        var stored = await settingsStore.ReadAsync(RetentionSettingKey, cancellationToken);
        return int.TryParse(stored, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, RetentionLowerBound, RetentionUpperBound)
            : DefaultRetentionCount;
    }

    public async Task WriteRetentionCountAsync(
        int retentionCount,
        CancellationToken cancellationToken = default)
    {
        if (settingsStore is null) return;
        var normalized = Math.Clamp(retentionCount, RetentionLowerBound, RetentionUpperBound);
        await settingsStore.WriteAsync(
            RetentionSettingKey,
            normalized.ToString(CultureInfo.InvariantCulture),
            cancellationToken);
    }

    /// <summary>
    /// Replaces the live database with the selected backup. Callers must stop
    /// all background loops first; this method checkpoints the WAL, clears the
    /// connection pool, and keeps a safety copy of the pre-restore data.
    /// </summary>
    public RestoreResult ApplyRestore(string selectedBackupPath, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedBackupPath);
        var backup = Path.GetFullPath(selectedBackupPath);
        if (!File.Exists(backup))
        {
            throw new FileNotFoundException("备份文件不存在。", backup);
        }

        if (Path.GetFullPath(backup) == Path.GetFullPath(databasePath))
        {
            throw new InvalidOperationException("不能把当前数据库恢复为它自己。");
        }

        Directory.CreateDirectory(backupDirectory);
        CheckpointWal();
        SqliteConnection.ClearAllPools();

        var safetyPath = Path.Combine(
            backupDirectory,
            $"{RestoreSafetyPrefix}{now.ToLocalTime():yyyyMMdd-HHmmss}.db");
        File.Copy(databasePath, safetyPath, overwrite: true);
        TryDelete($"{databasePath}-wal");
        TryDelete($"{databasePath}-shm");
        File.Copy(backup, databasePath, overwrite: true);
        TryDelete($"{databasePath}-wal");
        TryDelete($"{databasePath}-shm");
        return new RestoreResult(safetyPath, backup);
    }

    private void CheckpointWal()
    {
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder($"Data Source={databasePath}") { Mode = SqliteOpenMode.ReadWrite }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        command.ExecuteNonQuery();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // The restore still proceeds; stale sidecar files are rewritten by SQLite.
        }
    }

    /// <summary>
    /// Exports every table to a versioned JSON file. Values are serialized as
    /// stored in SQLite so an export remains a faithful, re-importable record
    /// of the database contents.
    /// </summary>
    public async Task ExportAllDataJsonAsync(
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var destination = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder($"Data Source={databasePath}") { Mode = SqliteOpenMode.ReadOnly }.ToString());
        await connection.OpenAsync(cancellationToken);
        var document = new Dictionary<string, object?>
        {
            ["schema_version"] = 1,
            ["application"] = "IkuyoPet",
            ["exported_at"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
        };
        foreach (var (table, columns) in ExportTables)
        {
            document[table] = await ReadTableAsync(
                connection,
                table,
                columns,
                cancellationToken).ConfigureAwait(false);
        }

        var options = JsonExportOptions;
        await File.WriteAllTextAsync(
            destination,
            JsonSerializer.Serialize(document, options),
            cancellationToken);
    }

    private static readonly JsonSerializerOptions JsonExportOptions = new() { WriteIndented = true };

    private static async Task<List<Dictionary<string, object?>>> ReadTableAsync(
        SqliteConnection connection,
        string table,
        string[] columns,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {string.Join(", ", columns)} FROM {table};";
        var rows = new List<Dictionary<string, object?>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>(columns.Length);
            for (var index = 0; index < columns.Length; index++)
            {
                row[columns[index]] = reader.IsDBNull(index) ? null : reader.GetValue(index);
            }

            rows.Add(row);
        }

        return rows;
    }

    private static readonly (string Table, string[] Columns)[] ExportTables =
    [
        ("reminder_rules",
            ["id", "name", "kind", "enabled", "start_local", "end_local", "interval_minutes",
             "interval_min_minutes", "interval_max_minutes", "activity_duration_minutes",
             "parameter_source", "parameter_version", "daily_goal", "quiet_start", "quiet_end",
             "quiet_enabled", "max_retries", "created_at", "updated_at"]),
        ("reminder_events",
            ["id", "rule_id", "scheduled_at", "displayed_at", "channel", "action", "action_at",
             "retry_index", "suppressed_reason", "created_at", "activity_duration_minutes",
             "parameter_source", "parameter_version"]),
        ("reminder_runtime_state",
            ["rule_id", "cycle_id", "target_active_seconds", "accumulated_active_seconds",
             "state", "attempt", "retry_due_at", "updated_at"]),
        ("tracked_apps",
            ["id", "process_name", "display_name", "enabled", "created_at", "updated_at"]),
        ("work_sessions",
            ["id", "domain_id", "tracked_app_id", "started_at", "ended_at", "active_seconds",
             "end_reason"]),
        ("app_settings", ["key", "value", "updated_at"]),
    ];
}

public sealed record StartupBackupResult(
    bool CreatedBackup,
    string BackupPath,
    int RetentionCount,
    int DeletedCount);

public sealed record RestoreResult(
    string SafetyCopyPath,
    string RestoredFromPath);
