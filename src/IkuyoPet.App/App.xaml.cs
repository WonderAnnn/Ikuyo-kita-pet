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

    private void OnStartup(object sender, StartupEventArgs e)
    {
        var dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IkuyoPet");
        Directory.CreateDirectory(dataRoot);
        var databasePath = Path.Combine(dataRoot, "ikuyo-pet.db");
        var connectionString = $"Data Source={databasePath};Mode=ReadWriteCreate;Cache=Shared";
        new DatabaseMigrator(connectionString).MigrateAsync().GetAwaiter().GetResult();
        var repository = new SqliteEventRepository(connectionString);
        var dashboard = new DashboardQueryService(repository);
        var startupManager = new WindowsStartupManager(
            new CurrentUserStartupEntryStore(),
            Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "IkuyoPet.exe"));
        var viewModel = new MainWindowViewModel(dashboard, repository, startupManager);
        viewModel.LoadSettingsAsync(CancellationToken.None).GetAwaiter().GetResult();
        var window = new MainWindow(viewModel);
        petWindow = new PetWindow();
        var skinRoot = ResolveSkinRoot(dataRoot);
        var selectedSkin = new SkinSelectionStore(Path.Combine(dataRoot, "skins"))
            .LoadAsync(CancellationToken.None).GetAwaiter().GetResult()
            ?? new SkinSelection("user.ikuyo-local", "1.0.0");
        var skinResult = new SkinBootstrapper(new SkinPackageValidator(), skinRoot)
            .ResolveWithDiagnostics(selectedSkin);
        if (skinResult.Assets is not null)
        {
            petWindow.SetSkinAssets(skinResult.Assets);
        }
        else if (!string.IsNullOrWhiteSpace(skinResult.Error))
        {
            Debug.WriteLine(skinResult.Error);
        }
        var actionCoordinator = new ReminderActionCoordinator(
            repository,
            new ReminderStateMachine(3),
            TimeProvider.System);
        notificationSink = new WindowsAppNotificationSink(new NotificationActionHandler(actionCoordinator));
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
            isSuppressed: activityProbe.IsReminderSuppressed);
        workTrackingLoop = new WorkTrackingLoop(
            new WorkTrackingService(
                activityProbe,
                repository,
                displayNameResolver: viewModel.GetTrackedDisplayName),
            reminderLoop.ConsumeActiveWorkAsync);

        petWindow.ActionInvoked += async (_, args) =>
        {
            await actionCoordinator.HandleAsync(args.EventId, args.Action, CancellationToken.None);
            petWindow.ShowIdleSkin();
        };
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(MainWindowViewModel.PetEnabled)) return;
            if (viewModel.PetEnabled) petWindow.Show();
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
        petWindow.Show();
        window.Closed += (_, _) => trayIconHost?.Dispose();
        MainWindow = window;
        window.Show();
    }

    private static string ResolveSkinRoot(string dataRoot)
    {
        var userRoot = Path.Combine(dataRoot, "skins");
        var bundledRoot = Path.Combine(AppContext.BaseDirectory, "local-skins");
        return Directory.Exists(userRoot) || !Directory.Exists(bundledRoot) ? userRoot : bundledRoot;
    }
    protected override void OnExit(ExitEventArgs e)
    {
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

    private sealed class NotificationActionHandler(ReminderActionCoordinator coordinator)
        : INotificationActionHandler
    {
        public async Task HandleAsync(
            Guid eventId,
            ReminderAction action,
            CancellationToken cancellationToken)
        {
            await coordinator.HandleAsync(eventId, action, cancellationToken);
        }
    }
}
