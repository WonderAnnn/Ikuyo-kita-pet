using System.Globalization;
using IkuyoPet.Core.Reminders;
using IkuyoPet.Core.Storage;
using IkuyoPet.Core.WorkTracking;
using Microsoft.Data.Sqlite;

namespace IkuyoPet.Infrastructure.Storage;

public sealed class SqliteEventRepository(
    string connectionString,
    TimeZoneInfo? timeZone = null) : IEventRepository
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

    public async Task<ReminderEvent?> ReadReminderEventAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await new DatabaseMigrator(connectionString).MigrateAsync(cancellationToken);

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, rule_id, scheduled_at, displayed_at, channel, action,
                   action_at, retry_index, suppressed_reason, created_at
            FROM reminder_events
            WHERE id = $id;
            """;
        AddText(command, "$id", id.ToString("N"));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadReminderEvent(reader) : null;
    }

    public async Task<bool> TryUpdateReminderAsync(
        ReminderEvent item,
        ReminderOutcome expectedOutcome,
        int expectedRetryIndex,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        await new DatabaseMigrator(connectionString).MigrateAsync(cancellationToken);

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE reminder_events
            SET action = $action,
                action_at = $action_at,
                retry_index = $retry_index
            WHERE id = $id
              AND action = $expected_action
              AND retry_index = $expected_retry_index;
            """;
        AddText(command, "$action", ToDbOutcome(item.Outcome));
        AddNullableText(command, "$action_at", ToDbTimestamp(item.ActionAt));
        command.Parameters.AddWithValue("$retry_index", item.RetryIndex);
        AddText(command, "$id", item.Id.ToString("N"));
        AddText(command, "$expected_action", ToDbOutcome(expectedOutcome));
        command.Parameters.AddWithValue("$expected_retry_index", expectedRetryIndex);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> UpdateReminderChannelAsync(
        Guid id,
        string expectedChannel,
        string channel,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedChannel);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        await new DatabaseMigrator(connectionString).MigrateAsync(cancellationToken);

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE reminder_events
            SET channel = $channel
            WHERE id = $id
              AND channel = $expected_channel;
            """;
        AddText(command, "$channel", channel);
        AddText(command, "$id", id.ToString("N"));
        AddText(command, "$expected_channel", expectedChannel);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task AppendWorkSessionAsync(
        WorkSession session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        await new DatabaseMigrator(connectionString).MigrateAsync(cancellationToken);

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(connection, cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using var upsert = connection.CreateCommand();
        upsert.Transaction = transaction;
        upsert.CommandText = """
            INSERT INTO tracked_apps
                (process_name, display_name, enabled, created_at, updated_at)
            VALUES
                ($process_name, $display_name, 1, $created_at, $updated_at)
            ON CONFLICT(process_name) DO UPDATE SET
                display_name = excluded.display_name,
                updated_at = excluded.updated_at;
            """;
        AddText(upsert, "$process_name", session.ProcessName);
        AddText(upsert, "$display_name", session.DisplayName);
        AddText(upsert, "$created_at", ToDbTimestamp(session.StartedAt));
        AddText(upsert, "$updated_at", ToDbTimestamp(session.EndedAt));
        await upsert.ExecuteNonQueryAsync(cancellationToken);

        await using var findApp = connection.CreateCommand();
        findApp.Transaction = transaction;
        findApp.CommandText = "SELECT id FROM tracked_apps WHERE process_name = $process_name;";
        AddText(findApp, "$process_name", session.ProcessName);
        var trackedAppId = (long)(await findApp.ExecuteScalarAsync(cancellationToken))!;

        await using var insertSession = connection.CreateCommand();
        insertSession.Transaction = transaction;
        insertSession.CommandText = """
            INSERT INTO work_sessions
                (domain_id, tracked_app_id, started_at, ended_at, active_seconds, end_reason)
            VALUES
                ($domain_id, $tracked_app_id, $started_at, $ended_at, $active_seconds, $end_reason);
            """;
        AddText(insertSession, "$domain_id", session.Id.ToString("N"));
        insertSession.Parameters.AddWithValue("$tracked_app_id", trackedAppId);
        AddText(insertSession, "$started_at", ToDbTimestamp(session.StartedAt));
        AddText(insertSession, "$ended_at", ToDbTimestamp(session.EndedAt));
        insertSession.Parameters.AddWithValue("$active_seconds", session.ActiveSeconds);
        AddText(insertSession, "$end_reason", session.EndReason);
        await insertSession.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReminderRule>> ReadReminderRulesAsync(
        CancellationToken cancellationToken)
    {
        await new DatabaseMigrator(connectionString).MigrateAsync(cancellationToken);

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, kind, name, start_local, end_local, interval_minutes, enabled,
                   daily_goal, quiet_start, quiet_end, quiet_enabled
            FROM reminder_rules
            ORDER BY id;
            """;

        var result = new List<ReminderRule>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ReminderRule(
                Guid.ParseExact(reader.GetString(0), "N"),
                reader.GetString(1),
                reader.GetString(2),
                ParseLocalTime(reader.GetString(3)),
                ParseLocalTime(reader.GetString(4)),
                reader.GetInt32(5),
                reader.GetInt32(6) != 0)
            {
                DailyGoal = reader.GetInt32(7),
                QuietHours = new QuietHours(
                    ParseLocalTime(reader.GetString(8)),
                    ParseLocalTime(reader.GetString(9)),
                    reader.GetInt32(10) != 0),
            });
        }

        return result;
    }

    public async Task UpsertReminderRuleAsync(
        ReminderRule rule,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rule);
        await new DatabaseMigrator(connectionString).MigrateAsync(cancellationToken);

        var now = ToDbTimestamp(DateTimeOffset.UtcNow);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO reminder_rules
                (id, name, kind, enabled, start_local, end_local, interval_minutes,
                 daily_goal, quiet_start, quiet_end, quiet_enabled, max_retries,
                 created_at, updated_at)
            VALUES
                ($id, $name, $kind, $enabled, $start_local, $end_local, $interval_minutes,
                 $daily_goal, $quiet_start, $quiet_end, $quiet_enabled, $max_retries,
                 $created_at, $updated_at)
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name,
                kind = excluded.kind,
                enabled = excluded.enabled,
                start_local = excluded.start_local,
                end_local = excluded.end_local,
                interval_minutes = excluded.interval_minutes,
                daily_goal = excluded.daily_goal,
                quiet_start = excluded.quiet_start,
                quiet_end = excluded.quiet_end,
                quiet_enabled = excluded.quiet_enabled,
                updated_at = excluded.updated_at;
            """;
        AddText(command, "$id", rule.Id.ToString("N"));
        AddText(command, "$name", rule.Message);
        AddText(command, "$kind", rule.Kind);
        command.Parameters.AddWithValue("$enabled", rule.Enabled ? 1 : 0);
        AddText(command, "$start_local", ToDbLocalTime(rule.StartLocal));
        AddText(command, "$end_local", ToDbLocalTime(rule.EndLocal));
        command.Parameters.AddWithValue("$interval_minutes", rule.IntervalMinutes);
        command.Parameters.AddWithValue("$daily_goal", rule.DailyGoal);
        AddText(command, "$quiet_start", ToDbLocalTime(rule.QuietHours.StartLocalTime));
        AddText(command, "$quiet_end", ToDbLocalTime(rule.QuietHours.EndLocalTime));
        command.Parameters.AddWithValue("$quiet_enabled", rule.QuietHours.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$max_retries", 3);
        AddText(command, "$created_at", now);
        AddText(command, "$updated_at", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkSession>> ReadWorkSessionsAsync(
        DateOnly day,
        CancellationToken cancellationToken)
    {
        await new DatabaseMigrator(connectionString).MigrateAsync(cancellationToken);
        var (start, end) = GetUtcDayBounds(day);

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.domain_id, a.process_name, a.display_name, s.started_at, s.ended_at,
                   s.active_seconds, s.end_reason
            FROM work_sessions AS s
            INNER JOIN tracked_apps AS a ON a.id = s.tracked_app_id
            WHERE s.started_at < $end
              AND s.ended_at > $start
            ORDER BY s.started_at, s.domain_id;
            """;
        AddText(command, "$start", ToDbTimestamp(start));
        AddText(command, "$end", ToDbTimestamp(end));

        var result = new List<WorkSession>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new WorkSession(
                Guid.ParseExact(reader.GetString(0), "N"),
                reader.GetString(1),
                reader.GetString(2),
                ParseTimestamp(reader.GetString(3)),
                ParseTimestamp(reader.GetString(4)),
                reader.GetInt32(5),
                reader.IsDBNull(6) ? string.Empty : reader.GetString(6)));
        }

        return result;
    }

    public async Task<IReadOnlyList<TrackedApplication>> ReadTrackedApplicationsAsync(
        CancellationToken cancellationToken)
    {
        await new DatabaseMigrator(connectionString).MigrateAsync(cancellationToken);

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT process_name, display_name, enabled
            FROM tracked_apps
            ORDER BY display_name, process_name;
            """;

        var result = new List<TrackedApplication>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new TrackedApplication(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2) != 0));
        }

        return result;
    }

    public async Task UpsertTrackedApplicationAsync(
        TrackedApplication application,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        await new DatabaseMigrator(connectionString).MigrateAsync(cancellationToken);

        var now = ToDbTimestamp(DateTimeOffset.UtcNow);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO tracked_apps
                (process_name, display_name, enabled, created_at, updated_at)
            VALUES
                ($process_name, $display_name, $enabled, $created_at, $updated_at)
            ON CONFLICT(process_name) DO UPDATE SET
                display_name = excluded.display_name,
                enabled = excluded.enabled,
                updated_at = excluded.updated_at;
            """;
        AddText(command, "$process_name", application.ProcessName);
        AddText(command, "$display_name", application.DisplayName);
        command.Parameters.AddWithValue("$enabled", application.Enabled ? 1 : 0);
        AddText(command, "$created_at", now);
        AddText(command, "$updated_at", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReminderEvent>> ReadReminderEventsAsync(
        DateOnly day,
        CancellationToken cancellationToken)
    {
        await new DatabaseMigrator(connectionString).MigrateAsync(cancellationToken);
        var (start, end) = GetUtcDayBounds(day);

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
            result.Add(ReadReminderEvent(reader));
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

    private static ReminderEvent ReadReminderEvent(SqliteDataReader reader) => new(
        Guid.ParseExact(reader.GetString(0), "N"),
        reader.IsDBNull(1) ? null : Guid.ParseExact(reader.GetString(1), "N"),
        ParseTimestamp(reader.GetString(2)),
        reader.IsDBNull(3) ? null : ParseTimestamp(reader.GetString(3)),
        reader.GetString(4),
        ParseOutcome(reader.GetString(5)),
        reader.IsDBNull(6) ? null : ParseTimestamp(reader.GetString(6)),
        reader.GetInt32(7),
        reader.IsDBNull(8) ? null : reader.GetString(8),
        ParseTimestamp(reader.GetString(9)));

    private static string ToDbLocalTime(TimeOnly value) =>
        value.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture);

    private static TimeOnly ParseLocalTime(string value) =>
        TimeOnly.Parse(value, CultureInfo.InvariantCulture);

    private (DateTimeOffset Start, DateTimeOffset End) GetUtcDayBounds(DateOnly day)
    {
        var effectiveTimeZone = timeZone ?? TimeZoneInfo.Local;
        var localStart = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var localEnd = day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return (
            new DateTimeOffset(localStart, effectiveTimeZone.GetUtcOffset(localStart)).ToUniversalTime(),
            new DateTimeOffset(localEnd, effectiveTimeZone.GetUtcOffset(localEnd)).ToUniversalTime());
    }

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
