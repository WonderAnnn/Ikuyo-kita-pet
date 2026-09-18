using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;
using IkuyoPet.Core.Analytics;
using IkuyoPet.Core.Bubbles;
using IkuyoPet.Core.Dashboard;
using IkuyoPet.Core.Performance;
using IkuyoPet.Core.Diagnostics;
using IkuyoPet.Core.Presentation;
using IkuyoPet.Core.Reminders;
using IkuyoPet.Core.Skins;
using IkuyoPet.Infrastructure.Storage;
using IkuyoPet.Infrastructure.Windows;
using IkuyoPet.Pet;
using IkuyoPet.Pet.Skins;

namespace IkuyoPet.App;

[SuppressMessage("Design", "CA1001", Justification = "Application owns and disposes the tray and cancellation resources on exit.")]
public partial class App : Application
{
    private TrayIconHost? trayIconHost;
    private PetWindow? petWindow;
    private WindowsAppNotificationSink? notificationSink;
    private CancellationTokenSource? lifetimeCancellation;
    private Task? reminderTask;
    private WorkTrackingLoop? workTrackingLoop;
    private Task? workTrackingTask;
    private DataManagementService? dataManagement;
    private PetInteractionSelector? interactionSelector;
    private bool isExiting;

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        try
        {
            WheelScrollSupport.Register();
            var dataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IkuyoPet");
            Directory.CreateDirectory(dataRoot);
            var databasePath = Path.Combine(dataRoot, "ikuyo-pet.db");
            var connectionString = $"Data Source={databasePath};Mode=ReadWriteCreate;Cache=Shared";
            await new DatabaseMigrator(connectionString).MigrateAsync();
            var repository = new SqliteEventRepository(connectionString);
            var dashboard = new DashboardQueryService(repository);
            var startupManager = new WindowsStartupManager(
                new CurrentUserStartupEntryStore(),
                Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "IkuyoPet.exe"));
            var appSettingsStore = new AppSettingsStore(connectionString);
            var bubbleAssetsRoot = Path.Combine(AppContext.BaseDirectory, "assets", "bubbles");
            var bubbleThemeCatalog = new BubbleThemeCatalog(bubbleAssetsRoot);
            var bubblePreloadPaths = BubbleThemeCatalog.BuiltInThemes
                .SelectMany(theme =>
                {
                    var filled = bubbleThemeCatalog.ResolveResourcePath(theme);
                    var directory = Path.GetDirectoryName(filled)!;
                    return new[] { filled, Path.Combine(directory, "bubble-source.png") };
                })
                .ToArray();
            _ = BubbleRenderer.Assets.PreloadAsync(
                bubblePreloadPaths,
                CancellationToken.None);
            var bubbleThemeSelectionStore = new BubbleThemeSelectionStore(
                Path.Combine(dataRoot, "bubble-theme"));
            var dataManagement = new DataManagementService(
                databasePath,
                Path.Combine(dataRoot, "backups"),
                appSettingsStore);
            try
            {
                var backupResult = await dataManagement.RunStartupBackupAsync(
                    DateOnly.FromDateTime(DateTime.Now));
                if (backupResult.CreatedBackup)
                {
                    Debug.WriteLine($"Startup backup created: {backupResult.BackupPath}");
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Startup backup failed: {exception}");
            }

            var viewModel = new MainWindowViewModel(
                dashboard,
                repository,
                startupManager,
                appSettingsStore,
                new WorkStatisticsQueryService(repository),
                new WindowsRunningProcessInspector());
            viewModel.ConfigureBubbleThemes(bubbleThemeCatalog, bubbleThemeSelectionStore);
            await viewModel.LoadSettingsAsync(CancellationToken.None);
            var window = new MainWindow(viewModel);
            petWindow = new PetWindow();
            petWindow.SetBubbleTheme(
                viewModel.SelectedBubbleTheme,
                bubbleThemeCatalog.ResolveResourcePath(viewModel.SelectedBubbleTheme));
            viewModel.BubbleThemeChanged += (_, theme) =>
                petWindow?.SetBubbleTheme(theme, bubbleThemeCatalog.ResolveResourcePath(theme));
            interactionSelector = await LoadInteractionSelectorAsync(CancellationToken.None);
            petWindow.InteractionRequested += OnPetInteractionRequested;
            var skinSelectionStore = new SkinSelectionStore(Path.Combine(dataRoot, "skins"));
            var selectedSkin = await skinSelectionStore
                .LoadAsync(CancellationToken.None)
                ?? new SkinSelection("kita-original", "1.0.0");
            selectedSkin = MigrateLegacySkinSelection(dataRoot, selectedSkin);
            var skinRoot = ResolveSkinRoot(dataRoot, selectedSkin);
            var skinResult = new SkinBootstrapper(new SkinPackageValidator(), skinRoot)
                .ResolveWithDiagnostics(selectedSkin);
            if (skinResult.Assets is not null)
            {
                petWindow.SetSkinAssets(skinResult.Assets);
                await skinSelectionStore.SaveAsync(selectedSkin, CancellationToken.None);
                viewModel.SetCurrentSkin(selectedSkin.Id, selectedSkin.Version, loaded: true);
            }
            else
            {
                viewModel.SetCurrentSkin(selectedSkin.Id, selectedSkin.Version, loaded: false, skinResult.Error);
                if (!string.IsNullOrWhiteSpace(skinResult.Error)) Debug.WriteLine(skinResult.Error);
            }
            var runtimeHealth = new RuntimeHealthRegistry();
            var actionCoordinator = new ReminderActionCoordinator(
                repository,
                new ReminderStateMachine(3),
                TimeProvider.System,
                healthRegistry: runtimeHealth);
            this.dataManagement = dataManagement;
            notificationSink = new WindowsAppNotificationSink(
                new NotificationActionHandler(
                    actionCoordinator,
                    cancellationToken => viewModel.RefreshAsync(
                        DateOnly.FromDateTime(viewModel.SelectedDate),
                        cancellationToken)),
                request => trayIconHost?.ShowNotification(
                    request.Title,
                    $"{request.Message}{Environment.NewLine}{Environment.NewLine}请打开 Ikuyo Pet 处理。"),
                () => trayIconHost?.ClearNotifications());
            await notificationSink.ClearPendingAsync(CancellationToken.None);
            var notificationPresenter = new WindowsNotificationPresenter(notificationSink);
            var petPresenter = new PetReminderPresenter(petWindow);
            var router = new ReminderPresentationRouter(petPresenter, notificationPresenter);
            DateTimeOffset? pausedUntil = null;
            var rawActivityProbe = new ForegroundActivityProbe(viewModel.IsTrackedProcess);
            var activityProbe = new CachedActivityProbe(rawActivityProbe, TimeProvider.System);
            var diagnosticsService = new StatusDiagnosticsService(
                repository,
                activityProbe,
                TimeZoneInfo.Local,
                TimeProvider.System,
                isPetEnabled: () => viewModel.PetEnabled,
                isPaused: () => pausedUntil is { } until && until > DateTimeOffset.UtcNow,
                getPausedUntil: () => pausedUntil);
            viewModel.AttachServices(diagnosticsService, dataManagement);
            viewModel.RestoreConfirmed += OnRestoreConfirmed;
            var reminderLoop = new ReminderLoop(
                repository,
                router,
                () => viewModel.PetEnabled,
                TimeProvider.System,
                TimeZoneInfo.Local,
                isPaused: () => pausedUntil is { } until && until > DateTimeOffset.UtcNow,
                isSuppressed: activityProbe.IsReminderSuppressed,
                actionCoordinator: actionCoordinator,
                healthRegistry: runtimeHealth);
            var pollingPolicy = new AdaptivePollingPolicy();
            var workTrackingService = new WorkTrackingService(
                    activityProbe,
                    repository,
                    displayNameResolver: viewModel.GetTrackedDisplayName);
            workTrackingLoop = new WorkTrackingLoop(
                workTrackingService,
                async (delta, cancellationToken) =>
                {
                    await reminderLoop.ConsumeActiveWorkAsync(delta, cancellationToken);
                    await window.Dispatcher.InvokeAsync(
                        () =>
                        {
                            viewModel.ApplyActiveWorkDelta(delta);
                            return viewModel.RefreshAsync(
                                DateOnly.FromDateTime(viewModel.SelectedDate),
                                cancellationToken);
                        },
                        System.Windows.Threading.DispatcherPriority.Background).Task.Unwrap();
                },
                sampleIntervalProvider: () =>
                {
                    var sample = workTrackingService.LastSample;
                    var longIdle = sample is { IdleTime: var idle } &&
                        idle >= TimeSpan.FromMinutes(5);
                    return pollingPolicy.GetWorkTrackingInterval(sample?.IsLocked ?? false, longIdle);
                },
                healthRegistry: runtimeHealth,
                timeProvider: TimeProvider.System);

            viewModel.ManualReminderRequested += async request
            =>
            {
                try
                {
                    // Snapshot the setting at click time so disabling the desktop pet is a
                    // hard guarantee for this action: it can never resurrect PetWindow.
                    var petEnabled = viewModel.PetEnabled;
                    var result = await actionCoordinator.HandleManualAsync(
                        request.Kind,
                        petEnabled ? "pet" : "notification",
                        CancellationToken.None);
                    if (result.Applied)
                    {
                        await viewModel.RefreshAsync(
                            DateOnly.FromDateTime(viewModel.SelectedDate),
                            CancellationToken.None);
                        if (petEnabled)
                        {
                            await petPresenter.ShowFeedbackAsync(
                                ReminderAction.Complete,
                                request.Kind,
                                CancellationToken.None);
                        }
                        else
                        {
                            await notificationPresenter.ShowAsync(
                                new ReminderDue(
                                    Guid.NewGuid(),
                                    request.Kind,
                                    PetReminderPresenter.BuildFeedback(
                                        ReminderAction.Complete,
                                        request.Kind),
                                    Array.Empty<ReminderActionOption>()),
                                CancellationToken.None);
                        }
                    }
                }
                catch (Exception exception)
                {
                    Debug.WriteLine($"Manual reminder action failed: {exception}");
                }
            };
            petWindow.ActionInvoked += async (_, args) =>
            {
                try
                {
                    var result = await actionCoordinator.HandleAsync(
                        args.EventId,
                        args.Action,
                        CancellationToken.None);
                    if (result is { Applied: true })
                    {
                        await viewModel.RefreshAsync(
                            DateOnly.FromDateTime(viewModel.SelectedDate),
                            CancellationToken.None);
                        await petPresenter.ShowFeedbackAsync(args.Action, args.ReminderKind, CancellationToken.None);
                    }
                    else
                    {
                        petWindow.RestoreIdle();
                    }
                }
                catch (Exception exception)
                {
                    Debug.WriteLine($"Reminder action failed: {exception}");
                    petWindow.RestoreIdle();
                }
            };
            viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName != nameof(MainWindowViewModel.PetEnabled)) return;
                if (viewModel.PetEnabled)
                {
                    petWindow.RestoreIdle();
                    petWindow.Show();
                }
                else petWindow.Hide();
            };

            trayIconHost = new TrayIconHost(
                window,
                petWindow,
                setPetEnabled: enabled => viewModel.PetEnabled = enabled,
                pauseReminders: duration => pausedUntil = DateTimeOffset.UtcNow.Add(duration),
                exitApplication: Shutdown);
            lifetimeCancellation = new CancellationTokenSource();
            reminderTask = RunReminderLoopAsync(reminderLoop, lifetimeCancellation.Token);
            workTrackingTask = RunWorkTrackingLoopAsync(workTrackingLoop, lifetimeCancellation.Token);
            if (viewModel.PetEnabled) petWindow.ShowAtStartup();
            window.Closed += (_, _) => trayIconHost?.Dispose();
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Ikuyo Pet startup failed: {exception}");
            var errorPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IkuyoPet",
                "startup-error.log");
            Directory.CreateDirectory(Path.GetDirectoryName(errorPath)!);
            File.AppendAllText(errorPath, $"[{DateTimeOffset.Now:O}] App startup: {exception}{Environment.NewLine}");
            System.Windows.MessageBox.Show(
                $"Ikuyo Pet 启动失败：{exception.Message}",
                "Ikuyo Pet",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private async void OnRestoreConfirmed(object? sender, string backupPath)
    {
        if (dataManagement is null || isExiting) return;

        try
        {
            lifetimeCancellation?.Cancel();
            await WaitForLoopAsync(reminderTask, "reminder loop");
            await WaitForLoopAsync(workTrackingTask, "work tracking loop");
            var result = dataManagement.ApplyRestore(backupPath, DateTimeOffset.Now);
            Debug.WriteLine(
                $"Database restored from {result.RestoredFromPath}; safety copy at {result.SafetyCopyPath}");
            var executablePath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                using var process = System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(executablePath)
                    {
                        UseShellExecute = true,
                    });
            }

            Shutdown(0);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Restore failed: {exception}");
            MessageBox.Show(
                $"恢复备份失败：{exception.Message}",
                "Ikuyo Pet",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static async Task WaitForLoopAsync(Task? task, string component)
    {
        if (task is null) return;
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping the background loops for a restore.
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"{component} stopped with an error: {exception}");
        }
    }

    private static async Task<PetInteractionSelector> LoadInteractionSelectorAsync(
        CancellationToken cancellationToken)
    {
        var loader = new PetInteractionCatalogLoader();
        var fallback = PetInteractionDefaults.Create();
        var fallbackPath = Path.Combine(
            AppContext.BaseDirectory,
            "interactions",
            "default",
            "click.json");
        var fallbackResult = await loader.LoadAsync(fallbackPath, fallback, cancellationToken);
        if (!string.IsNullOrWhiteSpace(fallbackResult.Diagnostic))
            Debug.WriteLine($"Fallback pet interaction catalog: {fallbackResult.Diagnostic}");

        var publicPath = Path.Combine(
            AppContext.BaseDirectory,
            "interactions",
            "kita-click.json");
        var publicResult = await loader.LoadAsync(publicPath, fallbackResult.Catalog, cancellationToken);
        if (!string.IsNullOrWhiteSpace(publicResult.Diagnostic))
            Debug.WriteLine($"Public pet interaction catalog: {publicResult.Diagnostic}");

        var privatePath = Path.Combine(
            AppContext.BaseDirectory,
            "interactions",
            "ikuyo-click.json");
        var privateResult = await loader.LoadAsync(privatePath, publicResult.Catalog, cancellationToken);
        if (!string.IsNullOrWhiteSpace(privateResult.Diagnostic))
            Debug.WriteLine($"Private pet interaction catalog: {privateResult.Diagnostic}");

        return new PetInteractionSelector(privateResult.Catalog);
    }

    private async void OnPetInteractionRequested(object? sender, EventArgs e)
    {
        if (isExiting || interactionSelector is null || petWindow is null) return;

        try
        {
            var message = interactionSelector.Next();
            await petWindow.ShowInteractionAsync(
                message.Text,
                TimeSpan.FromSeconds(4),
                CancellationToken.None);
        }
        catch (OperationCanceledException) when (isExiting)
        {
            // Expected if application shutdown interrupts the visible interaction.
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Pet interaction failed: {exception}");
        }
    }

    private static string ResolveSkinRoot(string dataRoot, SkinSelection selection)
    {
        var userRoot = Path.Combine(dataRoot, "skins");
        var userPackage = Path.Combine(userRoot, selection.Id, selection.Version);
        if (File.Exists(Path.Combine(userPackage, "manifest.json")))
            return userRoot;

        var publicRoot = Path.Combine(AppContext.BaseDirectory, "skins");
        if (File.Exists(Path.Combine(publicRoot, selection.Id, selection.Version, "manifest.json")))
            return publicRoot;

        var legacyRoot = Path.Combine(AppContext.BaseDirectory, "local-skins");
        return Directory.Exists(legacyRoot) ? legacyRoot : publicRoot;
    }

    private static SkinSelection MigrateLegacySkinSelection(string dataRoot, SkinSelection selection)
    {
        if (!string.Equals(selection.Id, "user.ikuyo-local", StringComparison.Ordinal))
            return selection;

        var userManifest = Path.Combine(dataRoot, "skins", selection.Id, selection.Version, "manifest.json");
        if (File.Exists(userManifest))
            return selection;

        var publicManifest = Path.Combine(
            AppContext.BaseDirectory,
            "skins",
            "kita-original",
            selection.Version,
            "manifest.json");
        return File.Exists(publicManifest)
            ? new SkinSelection("kita-original", selection.Version)
            : selection;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        isExiting = true;
        if (petWindow is not null)
            petWindow.InteractionRequested -= OnPetInteractionRequested;
        lifetimeCancellation?.Cancel();
        try
        {
            WaitForShutdown(reminderTask, "reminder loop");
            WaitForShutdown(workTrackingTask, "work tracking loop");
        }
        finally
        {
            notificationSink?.Dispose();
            trayIconHost?.Dispose();
            petWindow?.Close();
            lifetimeCancellation?.Dispose();
            base.OnExit(e);
        }
    }

    private static void WaitForShutdown(Task? task, string component)
    {
        if (task is null) return;
        try
        {
            task.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Expected during application shutdown.
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"{component} stopped with an error: {exception}");
        }
    }

    private static async Task RunWorkTrackingLoopAsync(WorkTrackingLoop? loop, CancellationToken cancellationToken)
    {
        if (loop is null) return;
        await loop.RunAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task RunReminderLoopAsync(ReminderLoop loop, CancellationToken cancellationToken)
    {
        try
        {
            await loop.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when the tray Exit command shuts down the app.
        }
    }

    private sealed class NotificationActionHandler(
        ReminderActionCoordinator coordinator,
        Func<CancellationToken, Task> refreshDashboardAsync)
        : INotificationActionHandler
    {
        public async Task HandleAsync(
            Guid eventId,
            ReminderAction action,
            CancellationToken cancellationToken)
        {
            var result = await coordinator.HandleAsync(eventId, action, cancellationToken);
            if (result is { Applied: true }) await refreshDashboardAsync(cancellationToken);
        }
    }
}
