using System.Globalization;
using IkuyoPet.Core.Reminders;
using IkuyoPet.Core.Storage;
using Microsoft.Data.Sqlite;

namespace IkuyoPet.Infrastructure.Storage;

public sealed class SqliteEventRepository(string connectionString) : IEventRepository
{
    public async Task AppendReminderAsync(
        ReminderEvent item,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        await new DatabaseMigrator(connectionString).MigrateAsync(cancellationToken);

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO reminder_events
                (id, rule_id, scheduled_at, displayed_at, channel, action,
                 action_at, retry_index, suppressed_reason, created_at)
            VALUES
                ($id, $rule_id, $scheduled_at, $displayed_at, $channel, $action,
                 $action_at, $retry_index, $suppressed_reason, $created_at);
            """;
        AddText(command, "$id", item.Id.ToString("N"));
        AddNullableText(command, "$rule_id", item.RuleId?.ToString("N"));
        AddText(command, "$scheduled_at", ToDbTimestamp(item.ScheduledAt));
        AddNullableText(command, "$displayed_at", ToDbTimestamp(item.DisplayedAt));
        AddText(command, "$channel", item.Channel);
        AddText(command, "$action", ToDbOutcome(item.Outcome));
        AddNullableText(command, "$action_at", ToDbTimestamp(item.ActionAt));
        command.Parameters.AddWithValue("$retry_index", item.RetryIndex);
        AddNullableText(command, "$suppressed_reason", item.SuppressedReason);
        AddText(command, "$created_at", ToDbTimestamp(item.CreatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReminderEvent>> ReadReminderEventsAsync(
        DateOnly day,
        CancellationToken cancellationToken)
    {
        await new DatabaseMigrator(connectionString).MigrateAsync(cancellationToken);

        var localStart = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var localEnd = day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var start = new DateTimeOffset(localStart, TimeZoneInfo.Local.GetUtcOffset(localStart)).ToUniversalTime();
        var end = new DateTimeOffset(localEnd, TimeZoneInfo.Local.GetUtcOffset(localEnd)).ToUniversalTime();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, rule_id, scheduled_at, displayed_at, channel, action,
                   action_at, retry_index, suppressed_reason, created_at
            FROM reminder_events
            WHERE scheduled_at >= $start AND scheduled_at < $end
            ORDER BY scheduled_at;
            """;
        AddText(command, "$start", ToDbTimestamp(start));
        AddText(command, "$end", ToDbTimestamp(end));

        var result = new List<ReminderEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ReminderEvent(
                Guid.ParseExact(reader.GetString(0), "N"),
                reader.IsDBNull(1) ? null : Guid.ParseExact(reader.GetString(1), "N"),
                ParseTimestamp(reader.GetString(2)),
                reader.IsDBNull(3) ? null : ParseTimestamp(reader.GetString(3)),
                reader.GetString(4),
                ParseOutcome(reader.GetString(5)),
                reader.IsDBNull(6) ? null : ParseTimestamp(reader.GetString(6)),
                reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                ParseTimestamp(reader.GetString(9))));
        }

        return result;
    }

    private static async Task ConfigureConnectionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    private static void AddText(SqliteCommand command, string name, string value)
    {
        command.Parameters.Add(name, SqliteType.Text).Value = value;
    }

    private static void AddNullableText(SqliteCommand command, string name, string? value)
    {
        command.Parameters.Add(name, SqliteType.Text).Value = (object?)value ?? DBNull.Value;
    }

    private static string ToDbTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static string? ToDbTimestamp(DateTimeOffset? value) =>
        value.HasValue ? ToDbTimestamp(value.Value) : null;

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static string ToDbOutcome(ReminderOutcome outcome) => outcome switch
    {
        ReminderOutcome.None => "none",
        ReminderOutcome.Completed => "completed",
        ReminderOutcome.Snoozed => "snoozed",
        ReminderOutcome.Skipped => "skipped",
        ReminderOutcome.Unanswered => "unanswered",
        ReminderOutcome.Suppressed => "suppressed",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };

    private static ReminderOutcome ParseOutcome(string value) => value switch
    {
        "none" => ReminderOutcome.None,
        "completed" => ReminderOutcome.Completed,
        "snoozed" => ReminderOutcome.Snoozed,
        "skipped" => ReminderOutcome.Skipped,
        "unanswered" => ReminderOutcome.Unanswered,
        "suppressed" => ReminderOutcome.Suppressed,
        _ => throw new InvalidOperationException($"Unknown reminder outcome '{value}'."),
    };
}