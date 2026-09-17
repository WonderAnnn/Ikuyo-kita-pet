using System.Globalization;
using IkuyoPet.Core.Reminders;
using IkuyoPet.Core.Storage;
using IkuyoPet.Core.WorkTracking;
using Microsoft.Data.Sqlite;

namespace IkuyoPet.Infrastructure.Storage;

public sealed class SqliteEventRepository(
    string connectionString,
    TimeZoneInfo? timeZone = null,
    Func<int, ReminderRule, Exception?>? batchFailureInjector = null) : IEventRepository
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
                 action_at, retry_index, suppressed_reason, created_at,
                 activity_duration_minutes, parameter_source, parameter_version)
            VALUES
                ($id, $rule_id, $scheduled_at, $displayed_at, $channel, $action,
                 $action_at, $retry_index, $suppressed_reason, $created_at,
                 $activity_duration_minutes, $parameter_source, $parameter_version);
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
        command.Parameters.AddWithValue("$activity_duration_minutes", item.ActivityDurationMinutes);
        AddText(command, "$parameter_source", item.ParameterSource);
        AddText(command, "$parameter_version", item.ParameterVersion);
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
                   action_at, retry_index, suppressed_reason, created_at,
                   activity_duration_minutes, parameter_source, parameter_version
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
                retry_index = $retry_index,
                suppressed_reason = $suppressed_reason
            WHERE id = $id
              AND action = $expected_action
              AND retry_index = $expected_retry_index;
            """;
        AddText(command, "$action", ToDbOutcome(item.Outcome));
        AddNullableText(command, "$action_at", ToDbTimestamp(item.ActionAt));
        command.Parameters.AddWithValue("$retry_index", item.RetryIndex);
        AddNullableText(command, "$suppressed_reason", item.SuppressedReason);
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
                   daily_goal, quiet_start, quiet_end, quiet_enabled,
                   interval_min_minutes, interval_max_minutes, activity_duration_minutes,
                   parameter_source, parameter_version
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
                IntervalMinMinutes = reader.GetInt32(11),
                IntervalMaxMinutes = reader.GetInt32(12),
                ActivityDurationMinutes = reader.GetInt32(13),
                ParameterSource = reader.GetString(14),
                ParameterVersion = reader.GetString(15),
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
                 interval_min_minutes, interval_max_minutes, activity_duration_minutes,
                 parameter_source, parameter_version, daily_goal, quiet_start, quiet_end,
                 quiet_enabled, max_retries, created_at, updated_at)
            VALUES
                ($id, $name, $kind, $enabled, $start_local, $end_local, $interval_minutes,
                 $interval_min_minutes, $interval_max_minutes, $activity_duration_minutes,
                 $parameter_source, $parameter_version, $daily_goal, $quiet_start, $quiet_end,
                 $quiet_enabled, $max_retries, $created_at, $updated_at)
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name,
                kind = excluded.kind,
                enabled = excluded.enabled,
                start_local = excluded.start_local,
                end_local = excluded.end_local,
                interval_minutes = excluded.interval_minutes,
                interval_min_minutes = excluded.interval_min_minutes,
                interval_max_minutes = excluded.interval_max_minutes,
                activity_duration_minutes = excluded.activity_duration_minutes,
                parameter_source = excluded.parameter_source,
                parameter_version = excluded.parameter_version,
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
        command.Parameters.AddWithValue("$interval_min_minutes", rule.IntervalMinMinutes);
        command.Parameters.AddWithValue("$interval_max_minutes", rule.IntervalMaxMinutes);
        command.Parameters.AddWithValue("$activity_duration_minutes", rule.ActivityDurationMinutes);
        AddText(command, "$parameter_source", rule.ParameterSource);
        AddText(command, "$parameter_version", rule.ParameterVersion);
        command.Parameters.AddWithValue("$daily_goal", rule.DailyGoal);
        AddText(command, "$quiet_start", ToDbLocalTime(rule.QuietHours.StartLocalTime));
        AddText(command, "$quiet_end", ToDbLocalTime(rule.QuietHours.EndLocalTime));
        command.Parameters.AddWithValue("$quiet_enabled", rule.QuietHours.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$max_retries", 3);
        AddText(command, "$created_at", now);
        AddText(command, "$updated_at", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertReminderRulesAsync(
        IReadOnlyList<ReminderRule> rules,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (rules.Count == 0) return;
        if (rules.Any(rule => rule is null)) throw new ArgumentException("Rules cannot contain null.", nameof(rules));
        await new DatabaseMigrator(connectionString).MigrateAsync(cancellationToken);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(connection, cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var now = ToDbTimestamp(DateTimeOffset.UtcNow);
        for (var index = 0; index < rules.Count; index++)
        {
            var rule = rules[index];
            var injected = batchFailureInjector?.Invoke(index, rule);
            if (injected is not null) throw injected;
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO reminder_rules
                    (id, name, kind, enabled, start_local, end_local, interval_minutes,
                     interval_min_minutes, interval_max_minutes, activity_duration_minutes,
                     parameter_source, parameter_version, daily_goal, quiet_start, quiet_end,
                     quiet_enabled, max_retries, created_at, updated_at)
                VALUES
                    ($id, $name, $kind, $enabled, $start_local, $end_local, $interval_minutes,
                     $interval_min_minutes, $interval_max_minutes, $activity_duration_minutes,
                     $parameter_source, $parameter_version, $daily_goal, $quiet_start, $quiet_end,
                     $quiet_enabled, $max_retries, $created_at, $updated_at)
                ON CONFLICT(id) DO UPDATE SET
                    name=excluded.name, kind=excluded.kind, enabled=excluded.enabled,
                    start_local=excluded.start_local, end_local=excluded.end_local,
                    interval_minutes=excluded.interval_minutes,
                    interval_min_minutes=excluded.interval_min_minutes,
                    interval_max_minutes=excluded.interval_max_minutes,
                    activity_duration_minutes=excluded.activity_duration_minutes,
                    parameter_source=excluded.parameter_source,
                    parameter_version=excluded.parameter_version,
                    daily_goal=excluded.daily_goal, quiet_start=excluded.quiet_start,
                    quiet_end=excluded.quiet_end, quiet_enabled=excluded.quiet_enabled,
                    updated_at=excluded.updated_at;
                """;
            AddText(command, "$id", rule.Id.ToString("N"));
            AddText(command, "$name", rule.Message);
            AddText(command, "$kind", rule.Kind);
            command.Parameters.AddWithValue("$enabled", rule.Enabled ? 1 : 0);
            AddText(command, "$start_local", ToDbLocalTime(rule.StartLocal));
            AddText(command, "$end_local", ToDbLocalTime(rule.EndLocal));
            command.Parameters.AddWithValue("$interval_minutes", rule.IntervalMinutes);
            command.Parameters.AddWithValue("$interval_min_minutes", rule.IntervalMinMinutes);
            command.Parameters.AddWithValue("$interval_max_minutes", rule.IntervalMaxMinutes);
            command.Parameters.AddWithValue("$activity_duration_minutes", rule.ActivityDurationMinutes);
            AddText(command, "$parameter_source", rule.ParameterSource);
            AddText(command, "$parameter_version", rule.ParameterVersion);
            command.Parameters.AddWithValue("$daily_goal", rule.DailyGoal);
            AddText(command, "$quiet_start", ToDbLocalTime(rule.QuietHours.StartLocalTime));
            AddText(command, "$quiet_end", ToDbLocalTime(rule.QuietHours.EndLocalTime));
            command.Parameters.AddWithValue("$quiet_enabled", rule.QuietHours.Enabled ? 1 : 0);
            command.Parameters.AddWithValue("$max_retries", 3);
            AddText(command, "$created_at", now);
            AddText(command, "$updated_at", now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> TryAddDefaultReminderRuleAsync(
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
                 interval_min_minutes, interval_max_minutes, activity_duration_minutes,
                 parameter_source, parameter_version, daily_goal, quiet_start, quiet_end,
                 quiet_enabled, max_retries, created_at, updated_at)
            SELECT
                $id, $name, $kind, $enabled, $start_local, $end_local, $interval_minutes,
                $interval_min_minutes, $interval_max_minutes, $activity_duration_minutes,
                $parameter_source, $parameter_version, $daily_goal, $quiet_start, $quiet_end,
                $quiet_enabled, $max_retries, $created_at, $updated_at
            WHERE NOT EXISTS (SELECT 1 FROM reminder_rules);
            """;
        AddText(command, "$id", rule.Id.ToString("N"));
        AddText(command, "$name", rule.Message);
        AddText(command, "$kind", rule.Kind);
        command.Parameters.AddWithValue("$enabled", rule.Enabled ? 1 : 0);
        AddText(command, "$start_local", ToDbLocalTime(rule.StartLocal));
        AddText(command, "$end_local", ToDbLocalTime(rule.EndLocal));
        command.Parameters.AddWithValue("$interval_minutes", rule.IntervalMinutes);
        command.Parameters.AddWithValue("$interval_min_minutes", rule.IntervalMinMinutes);
        command.Parameters.AddWithValue("$interval_max_minutes", rule.IntervalMaxMinutes);
        command.Parameters.AddWithValue("$activity_duration_minutes", rule.ActivityDurationMinutes);
        AddText(command, "$parameter_source", rule.ParameterSource);
        AddText(command, "$parameter_version", rule.ParameterVersion);
        command.Parameters.AddWithValue("$daily_goal", rule.DailyGoal);
        AddText(command, "$quiet_start", ToDbLocalTime(rule.QuietHours.StartLocalTime));
        AddText(command, "$quiet_end", ToDbLocalTime(rule.QuietHours.EndLocalTime));
        command.Parameters.AddWithValue("$quiet_enabled", rule.QuietHours.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$max_retries", 3);
        AddText(command, "$created_at", now);
        AddText(command, "$updated_at", now);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<ReminderRuntimeState?> ReadReminderRuntimeStateAsync(
        Guid ruleId,
        CancellationToken cancellationToken)
    {
        await new DatabaseMigrator(connectionString).MigrateAsync(cancellationToken);

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT rule_id, cycle_id, target_active_seconds, accumulated_active_seconds,
                   state, attempt, retry_due_at, updated_at
            FROM reminder_runtime_state
            WHERE rule_id = $rule_id;
            """;
        AddText(command, "$rule_id", ruleId.ToString("N"));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ReminderRuntimeState(
            Guid.ParseExact(reader.GetString(0), "N"),
            Guid.ParseExact(reader.GetString(1), "N"),
            reader.GetInt32(2),
            reader.GetInt32(3),
            ParseRuntimeStatus(reader.GetString(4)),
            reader.GetInt32(5),
            reader.IsDBNull(6) ? null : ParseTimestamp(reader.GetString(6)),
            ParseTimestamp(reader.GetString(7)));
    }

    public async Task UpsertReminderRuntimeStateAsync(
        ReminderRuntimeState state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        await new DatabaseMigrator(connectionString).MigrateAsync(cancellationToken);

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO reminder_runtime_state
                (rule_id, cycle_id, target_active_seconds, accumulated_active_seconds,
                 state, attempt, retry_due_at, updated_at)
            VALUES
                ($rule_id, $cycle_id, $target_active_seconds, $accumulated_active_seconds,
                 $state, $attempt, $retry_due_at, $updated_at)
            ON CONFLICT(rule_id) DO UPDATE SET
                cycle_id = excluded.cycle_id,
                target_active_seconds = excluded.target_active_seconds,
                accumulated_active_seconds = excluded.accumulated_active_seconds,
                state = excluded.state,
                attempt = excluded.attempt,
                retry_due_at = excluded.retry_due_at,
                updated_at = excluded.updated_at;
            """;
        AddText(command, "$rule_id", state.RuleId.ToString("N"));
        AddText(command, "$cycle_id", state.CycleId.ToString("N"));
        command.Parameters.AddWithValue("$target_active_seconds", state.TargetActiveSeconds);
        command.Parameters.AddWithValue("$accumulated_active_seconds", state.AccumulatedActiveSeconds);
        AddText(command, "$state", ToDbRuntimeStatus(state.Status));
        command.Parameters.AddWithValue("$attempt", state.Attempt);
        AddNullableText(command, "$retry_due_at", ToDbTimestamp(state.RetryDueAt));
        AddText(command, "$updated_at", ToDbTimestamp(state.UpdatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> TryUpdateReminderRuntimeStateAsync(
        ReminderRuntimeState expected,
        ReminderRuntimeState updated,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(updated);
        if (expected.RuleId != updated.RuleId)
        {
            throw new ArgumentException("Runtime states must belong to the same rule.", nameof(updated));
        }

        await new DatabaseMigrator(connectionString).MigrateAsync(cancellationToken);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE reminder_runtime_state
            SET cycle_id = $next_cycle_id,
                target_active_seconds = $next_target_active_seconds,
                accumulated_active_seconds = $next_accumulated_active_seconds,
                state = $next_state,
                attempt = $next_attempt,
                retry_due_at = $next_retry_due_at,
                updated_at = $next_updated_at
            WHERE rule_id = $expected_rule_id
              AND cycle_id = $expected_cycle_id
              AND target_active_seconds = $expected_target_active_seconds
              AND accumulated_active_seconds = $expected_accumulated_active_seconds
              AND state = $expected_state
              AND attempt = $expected_attempt
              AND ((retry_due_at = $expected_retry_due_at)
                   OR (retry_due_at IS NULL AND $expected_retry_due_at IS NULL))
              AND updated_at = $expected_updated_at;
            """;
        AddText(command, "$next_cycle_id", updated.CycleId.ToString("N"));
        command.Parameters.AddWithValue("$next_target_active_seconds", updated.TargetActiveSeconds);
        command.Parameters.AddWithValue("$next_accumulated_active_seconds", updated.AccumulatedActiveSeconds);
        AddText(command, "$next_state", ToDbRuntimeStatus(updated.Status));
        command.Parameters.AddWithValue("$next_attempt", updated.Attempt);
        AddNullableText(command, "$next_retry_due_at", ToDbTimestamp(updated.RetryDueAt));
        AddText(command, "$next_updated_at", ToDbTimestamp(updated.UpdatedAt));
        AddText(command, "$expected_rule_id", expected.RuleId.ToString("N"));
        AddText(command, "$expected_cycle_id", expected.CycleId.ToString("N"));
        command.Parameters.AddWithValue("$expected_target_active_seconds", expected.TargetActiveSeconds);
        command.Parameters.AddWithValue("$expected_accumulated_active_seconds", expected.AccumulatedActiveSeconds);
        AddText(command, "$expected_state", ToDbRuntimeStatus(expected.Status));
        command.Parameters.AddWithValue("$expected_attempt", expected.Attempt);
        AddNullableText(command, "$expected_retry_due_at", ToDbTimestamp(expected.RetryDueAt));
        AddText(command, "$expected_updated_at", ToDbTimestamp(expected.UpdatedAt));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
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

    public async Task<IReadOnlyList<WorkSession>> ReadAllWorkSessionsAsync(
        CancellationToken cancellationToken)
    {
        await new DatabaseMigrator(connectionString).MigrateAsync(cancellationToken);

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.domain_id, a.process_name, a.display_name, s.started_at, s.ended_at,
                   s.active_seconds, s.end_reason
            FROM work_sessions AS s
            INNER JOIN tracked_apps AS a ON a.id = s.tracked_app_id
            ORDER BY s.started_at, s.domain_id;
            """;

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
                   action_at, retry_index, suppressed_reason, created_at,
                   activity_duration_minutes, parameter_source, parameter_version
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
        ParseTimestamp(reader.GetString(9)))
    {
        ActivityDurationMinutes = reader.GetInt32(10),
        ParameterSource = reader.GetString(11),
        ParameterVersion = reader.GetString(12),
    };

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

    private static string ToDbRuntimeStatus(ReminderRuntimeStatus status) => status switch
    {
        ReminderRuntimeStatus.Accumulating => "accumulating",
        ReminderRuntimeStatus.Due => "due",
        ReminderRuntimeStatus.WaitingRetry => "waiting_retry",
        ReminderRuntimeStatus.Unanswered => "unanswered",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private static ReminderRuntimeStatus ParseRuntimeStatus(string value) => value switch
    {
        "accumulating" => ReminderRuntimeStatus.Accumulating,
        "due" => ReminderRuntimeStatus.Due,
        "waiting_retry" => ReminderRuntimeStatus.WaitingRetry,
        "unanswered" => ReminderRuntimeStatus.Unanswered,
        _ => throw new InvalidOperationException($"Unknown reminder runtime status '{value}'."),
    };
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
