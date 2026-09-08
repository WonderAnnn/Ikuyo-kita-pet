using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;
using IkuyoPet.Core.Dashboard;
using IkuyoPet.Core.Presentation;
using IkuyoPet.Core.Reminders;
using IkuyoPet.Infrastructure.Storage;
using IkuyoPet.Infrastructure.Windows;
using IkuyoPet.Pet;

namespace IkuyoPet.App;

[SuppressMessage("Design", "CA1001", Justification = "Application owns and disposes the tray and cancellation resources on exit.")]
public partial class App : Application
{
    private TrayIconHost? trayIconHost;
    private PetWindow? petWindow;
    private WindowsAppNotificationSink? notificationSink;
    private CancellationTokenSource? lifetimeCancellation;
    private Task? reminderTask;

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
        var viewModel = new MainWindowViewModel(dashboard);
        var window = new MainWindow(viewModel);
        petWindow = new PetWindow();
        var actionCoordinator = new ReminderActionCoordinator(
            repository,
            new ReminderStateMachine(3),
            TimeProvider.System);
        notificationSink = new WindowsAppNotificationSink(new NotificationActionHandler(actionCoordinator));
        var notificationPresenter = new WindowsNotificationPresenter(notificationSink);
        var petPresenter = new PetReminderPresenter(petWindow);
        var router = new ReminderPresentationRouter(petPresenter, notificationPresenter);
        DateTimeOffset? pausedUntil = null;
        var reminderLoop = new ReminderLoop(
            repository,
            router,
            () => viewModel.PetEnabled,
            TimeProvider.System,
            TimeZoneInfo.Local,
            isPaused: () => pausedUntil is { } until && until > DateTimeOffset.UtcNow);

        petWindow.ActionInvoked += async (_, args) =>
            await actionCoordinator.HandleAsync(args.EventId, args.Action, CancellationToken.None);
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
        petWindow.Show();
        window.Closed += (_, _) => trayIconHost?.Dispose();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        lifetimeCancellation?.Cancel();
        try
        {
            reminderTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Expected during application shutdown.
        }

        notificationSink?.Dispose();
        trayIconHost?.Dispose();
        petWindow?.Close();
        lifetimeCancellation?.Dispose();
        base.OnExit(e);
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
