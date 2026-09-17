using System.IO;
using System.Text.Json;
using IkuyoPet.Core.Reminders;
using IkuyoPet.Core.WorkTracking;
using IkuyoPet.Infrastructure.Storage;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.Storage;

public sealed class DataManagementServiceTests
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"ikuyo-pet-data-{Guid.NewGuid():N}");

    private string DatabasePath => Path.Combine(root, "live", "ikuyo-pet.db");

    private string BackupDirectory => Path.Combine(root, "backups");

    [Fact]
    public async Task BackupToAsyncProducesReadableCopyOfCurrentData()
    {
        var (service, repository) = await CreateSeededServiceAsync();
        var target = Path.Combine(root, "manual", "copy.db");

        await service.BackupToAsync(target, TestContext.Current.CancellationToken);

        Assert.True(File.Exists(target));
        var verify = new SqliteEventRepository($"Data Source={target};Pooling=False");
        var rules = await verify.ReadReminderRulesAsync(TestContext.Current.CancellationToken);
        Assert.Contains(rules, rule => rule.Kind == "water");
        var sessions = await verify.ReadAllWorkSessionsAsync(TestContext.Current.CancellationToken);
        Assert.Single(sessions);
    }

    [Fact]
    public async Task StartupBackupCreatesOneFilePerDayAndPrunesOldBackups()
    {
        var (service, repository) = await CreateSeededServiceAsync(addSettingsStore: true);
        Directory.CreateDirectory(BackupDirectory);
        var stale = Path.Combine(BackupDirectory, "ikuyo-pet-20260101.db");
        File.WriteAllText(stale, "old");

        var today = new DateOnly(2026, 9, 11);
        var first = await service.RunStartupBackupAsync(today, 2, TestContext.Current.CancellationToken);
        Assert.True(first.CreatedBackup);

        var second = await service.RunStartupBackupAsync(today, 2, TestContext.Current.CancellationToken);
        Assert.False(second.CreatedBackup);
        Assert.Equal(first.BackupPath, second.BackupPath);
        Assert.Equal(2, second.RetentionCount);

        var tomorrow = await service.RunStartupBackupAsync(
            today.AddDays(1),
            2,
            TestContext.Current.CancellationToken);
        Assert.True(tomorrow.CreatedBackup);
        Assert.Equal(1, tomorrow.DeletedCount);
        Assert.False(File.Exists(stale));
        Assert.Equal(2, service.ListDailyBackups().Count);
    }

    [Fact]
    public async Task RetentionRoundTripsThroughAppSettings()
    {
        var (service, repository) = await CreateSeededServiceAsync(addSettingsStore: true);

        Assert.Equal(
            DataManagementService.DefaultRetentionCount,
            await service.ReadRetentionCountAsync(TestContext.Current.CancellationToken));

        await service.WriteRetentionCountAsync(30, TestContext.Current.CancellationToken);
        Assert.Equal(30, await service.ReadRetentionCountAsync(TestContext.Current.CancellationToken));

        await service.WriteRetentionCountAsync(9_999, TestContext.Current.CancellationToken);
        Assert.Equal(
            DataManagementService.RetentionUpperBound,
            await service.ReadRetentionCountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ApplyRestoreReplacesDatabaseAndKeepsSafetyCopy()
    {
        var (service, repository) = await CreateSeededServiceAsync();
        var backupPath = Path.Combine(root, "restore-source.db");
        await repository.UpsertReminderRuleAsync(
            ReminderRule.CreateDefaultHydration(DefaultReminderRuleIds.Hydration) with
            {
                Message = "备份里的文案",
            },
            TestContext.Current.CancellationToken);
        await service.BackupToAsync(backupPath, TestContext.Current.CancellationToken);

        // Diverge the live database after the backup was taken.
        await repository.UpsertReminderRuleAsync(
            ReminderRule.CreateDefaultHydration(DefaultReminderRuleIds.Hydration) with
            {
                Message = "恢复前的新文案",
            },
            TestContext.Current.CancellationToken);

        var result = service.ApplyRestore(backupPath, DateTimeOffset.Now);

        Assert.True(File.Exists(result.SafetyCopyPath));
        var restored = new SqliteEventRepository(
            $"Data Source={DatabasePath};Pooling=False");
        var rules = await restored.ReadReminderRulesAsync(TestContext.Current.CancellationToken);
        Assert.Contains(rules, rule => rule.Message == "备份里的文案");
        Assert.DoesNotContain(rules, rule => rule.Message == "恢复前的新文案");

        // The safety copy still holds the pre-restore content.
        var safety = new SqliteEventRepository(
            $"Data Source={result.SafetyCopyPath};Pooling=False");
        var safetyRules = await safety.ReadReminderRulesAsync(TestContext.Current.CancellationToken);
        Assert.Contains(safetyRules, rule => rule.Message == "恢复前的新文案");
    }

    [Fact]
    public async Task ApplyRestoreRejectsMissingAndSelfTargets()
    {
        var (service, _) = await CreateSeededServiceAsync(addSettingsStore: true);

        Assert.Throws<FileNotFoundException>(() =>
            service.ApplyRestore(Path.Combine(root, "missing.db"), DateTimeOffset.Now));
        Assert.Throws<InvalidOperationException>(() =>
            service.ApplyRestore(DatabasePath, DateTimeOffset.Now));
    }

    [Fact]
    public async Task ExportAllDataJsonAsyncWritesEveryTable()
    {
        var (service, repository) = await CreateSeededServiceAsync(addSettingsStore: true);
        var store = new AppSettingsStore($"Data Source={DatabasePath}");
        await store.WriteAsync("pet.enabled", "true", TestContext.Current.CancellationToken);
        var target = Path.Combine(root, "export.json");

        await service.ExportAllDataJsonAsync(target, TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(
            await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
        var rootElement = document.RootElement;
        Assert.Equal(1, rootElement.GetProperty("schema_version").GetInt32());
        Assert.Equal("IkuyoPet", rootElement.GetProperty("application").GetString());
        foreach (var table in new[]
                 {
                     "reminder_rules",
                     "reminder_events",
                     "reminder_runtime_state",
                     "tracked_apps",
                     "work_sessions",
                     "app_settings",
                 })
        {
            Assert.True(rootElement.TryGetProperty(table, out _), $"missing table {table}");
        }

        var rules = rootElement.GetProperty("reminder_rules").EnumerateArray().ToList();
        Assert.Contains(rules, rule => rule.GetProperty("kind").GetString() == "water");
        var settings = rootElement.GetProperty("app_settings").EnumerateArray().ToList();
        Assert.Contains(settings, setting =>
            setting.GetProperty("key").GetString() == "pet.enabled");
    }

    private async Task<(DataManagementService Service, SqliteEventRepository Repository)> CreateSeededServiceAsync(
        bool addSettingsStore = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        var connectionString = $"Data Source={DatabasePath}";
        var repository = new SqliteEventRepository(connectionString);
        await repository.UpsertReminderRuleAsync(
            ReminderRule.CreateDefaultHydration(DefaultReminderRuleIds.Hydration),
            TestContext.Current.CancellationToken);
        await repository.UpsertTrackedApplicationAsync(
            new TrackedApplication("pycharm64", "PyCharm", true),
            TestContext.Current.CancellationToken);
        var now = DateTimeOffset.UtcNow;
        await repository.AppendWorkSessionAsync(
            new WorkSession(
                Guid.NewGuid(),
                "pycharm64",
                "PyCharm",
                now.AddMinutes(-5),
                now,
                300,
                "test"),
            TestContext.Current.CancellationToken);
        var store = addSettingsStore
            ? new AppSettingsStore(connectionString)
            : null;
        var service = new DataManagementService(DatabasePath, BackupDirectory, store);
        return (service, repository);
    }

    [Fact]
    public async Task ListDailyBackupsIgnoresSafetyCopies()
    {
        var (service, repository) = await CreateSeededServiceAsync();
        Directory.CreateDirectory(BackupDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(BackupDirectory, "pre-restore-20260911-090000.db"),
            "safety",
            TestContext.Current.CancellationToken);
        var result = await service.RunStartupBackupAsync(
            new DateOnly(2026, 9, 11),
            5,
            TestContext.Current.CancellationToken);

        Assert.Single(service.ListDailyBackups());
        Assert.Equal(
            Path.Combine(BackupDirectory, "ikuyo-pet-20260911.db"),
            result.BackupPath);
    }
}
