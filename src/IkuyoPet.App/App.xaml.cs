using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;
using IkuyoPet.Core.Dashboard;
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
    private PetInteractionSelector? interactionSelector;
    private bool isExiting;

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        try
        {
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
            var viewModel = new MainWindowViewModel(dashboard, repository, startupManager, appSettingsStore);
            await viewModel.LoadSettingsAsync(CancellationToken.None);
            var window = new MainWindow(viewModel);
            petWindow = new PetWindow();
            interactionSelector = await LoadInteractionSelectorAsync(CancellationToken.None);
            petWindow.InteractionRequested += OnPetInteractionRequested;
            var skinSelectionStore = new SkinSelectionStore(Path.Combine(dataRoot, "skins"));
            var selectedSkin = await skinSelectionStore
                .LoadAsync(CancellationToken.None)
                ?? new SkinSelection("user.ikuyo-local", "1.0.0");
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
            var actionCoordinator = new ReminderActionCoordinator(
                repository,
                new ReminderStateMachine(3),
                TimeProvider.System);
            notificationSink = new WindowsAppNotificationSink(
                new NotificationActionHandler(
                    actionCoordinator,
                    cancellationToken => viewModel.RefreshAsync(
                        DateOnly.FromDateTime(viewModel.SelectedDate),
                        cancellationToken)),
                request => trayIconHost?.ShowNotification(
                    request.Title,
                    $"{request.Message}{Environment.NewLine}{Environment.NewLine}请打开 Ikuyo Pet 处理。"));
            var notificationPresenter = new WindowsNotificationPresenter(notificationSink);
            var petPresenter = new PetReminderPresenter(petWindow);
            var router = new ReminderPresentationRouter(petPresenter, notificationPresenter);
            DateTimeOffset? pausedUntil = null;
            var activityProbe = new ForegroundActivityProbe(viewModel.IsTrackedProcess);
            var reminderLoop = new ReminderLoop(
                repository,
                router,
                () => viewModel.PetEnabled,
                TimeProvider.System,
                TimeZoneInfo.Local,
                isPaused: () => pausedUntil is { } until && until > DateTimeOffset.UtcNow,
                isSuppressed: activityProbe.IsReminderSuppressed,
                actionCoordinator: actionCoordinator);
            workTrackingLoop = new WorkTrackingLoop(
                new WorkTrackingService(
                    activityProbe,
                    repository,
                    displayNameResolver: viewModel.GetTrackedDisplayName),
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
                });

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
                        await petPresenter.ShowFeedbackAsync(args.Action, CancellationToken.None);
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
            if (viewModel.PetEnabled) petWindow.Show();
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

    private static async Task<PetInteractionSelector> LoadInteractionSelectorAsync(
        CancellationToken cancellationToken)
    {
        var loader = new PetInteractionCatalogLoader();
        var fallback = PetInteractionDefaults.Create();
        var publicPath = Path.Combine(
            AppContext.BaseDirectory,
            "interactions",
            "default",
            "click.json");
        var publicResult = await loader.LoadAsync(publicPath, fallback, cancellationToken);
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
        var bundledRoot = Path.Combine(AppContext.BaseDirectory, "local-skins");
        return File.Exists(Path.Combine(userPackage, "manifest.json")) || !Directory.Exists(bundledRoot)
            ? userRoot
            : bundledRoot;
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
