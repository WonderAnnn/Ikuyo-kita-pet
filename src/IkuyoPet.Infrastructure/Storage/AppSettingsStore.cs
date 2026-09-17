using System.Globalization;
using Microsoft.Data.Sqlite;

namespace IkuyoPet.Infrastructure.Storage;

public sealed class AppSettingsStore(string connectionString)
{
    public async Task<string?> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await new DatabaseMigrator(connectionString).MigrateAsync(cancellationToken);

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM app_settings WHERE key = $key;";
        command.Parameters.Add("$key", SqliteType.Text).Value = key;
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    public async Task WriteAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        await new DatabaseMigrator(connectionString).MigrateAsync(cancellationToken);

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO app_settings (key, value, updated_at)
            VALUES ($key, $value, $updated_at)
            ON CONFLICT(key) DO UPDATE SET
                value = excluded.value,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.Add("$key", SqliteType.Text).Value = key;
        command.Parameters.Add("$value", SqliteType.Text).Value = value;
        command.Parameters.Add("$updated_at", SqliteType.Text).Value =
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}