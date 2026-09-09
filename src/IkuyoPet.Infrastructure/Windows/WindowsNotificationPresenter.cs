using System.Diagnostics;
using System.Runtime.InteropServices;
using IkuyoPet.Core.Presentation;
using IkuyoPet.Core.Reminders;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace IkuyoPet.Infrastructure.Windows;

public sealed record NotificationRequest(
    Guid EventId,
    string Title,
    string Message,
    IReadOnlyList<ReminderAction> Actions);

public interface INotificationSink
{
    Task ShowAsync(NotificationRequest request, CancellationToken cancellationToken);
}

public sealed class WindowsNotificationPresenter(INotificationSink sink) : IReminderPresenter
{
    public string Channel => "notification";

    public async Task ShowAsync(ReminderDue due, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(due);
        cancellationToken.ThrowIfCancellationRequested();

        await sink.ShowAsync(
            new NotificationRequest(
                due.EventId,
                "Ikuyo Pet",
                due.Message,
                due.Actions.Select(option => option.Action).ToArray()),
            cancellationToken);
    }
}

public interface INotificationActionHandler
{
    Task HandleAsync(Guid eventId, ReminderAction action, CancellationToken cancellationToken);
}

public sealed class WindowsAppNotificationSink : INotificationSink, IDisposable
{
    private readonly AppNotificationManager? manager;
    private readonly INotificationActionHandler actionHandler;
    private readonly Action<NotificationRequest>? unavailableFallback;
    private readonly object registrationGate = new();
    private bool registered;

    public WindowsAppNotificationSink(
        INotificationActionHandler actionHandler,
        Action<NotificationRequest>? unavailableFallback = null)
    {
        this.actionHandler = actionHandler ?? throw new ArgumentNullException(nameof(actionHandler));
        this.unavailableFallback = unavailableFallback;

        try
        {
            if (AppNotificationManager.IsSupported())
            {
                manager = AppNotificationManager.Default;
                manager.NotificationInvoked += OnNotificationInvoked;
            }
        }
        catch (COMException exception)
        {
            // Some Windows installations do not have the Windows App SDK
            // notification class registered. Notifications must not prevent
            // the tray app and desktop pet from starting.
            Debug.WriteLine($"Windows App SDK notifications unavailable: {exception.Message}");
            manager = null;
        }
    }

    public Task ShowAsync(NotificationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (manager is null)
        {
            unavailableFallback?.Invoke(request);
            return Task.CompletedTask;
        }

        EnsureRegistered();

        var builder = new AppNotificationBuilder()
            .AddText(request.Title)
            .AddText(request.Message);

        foreach (var action in request.Actions.Distinct())
        {
            builder.AddButton(
                new AppNotificationButton(GetActionLabel(action))
                    .AddArgument("eventId", request.EventId.ToString("D"))
                    .AddArgument("action", action.ToString()));
        }

        var notification = builder.BuildNotification();
        notification.SuppressDisplay = true;
        notification.Tag = request.EventId.ToString("D");
        manager.Show(notification);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (manager is null) return;

        manager.NotificationInvoked -= OnNotificationInvoked;
        if (registered)
        {
            manager.Unregister();
            registered = false;
        }
    }

    private void EnsureRegistered()
    {
        if (registered) return;

        lock (registrationGate)
        {
            if (registered) return;
            if (manager is null || !AppNotificationManager.IsSupported())
            {
                throw new PlatformNotSupportedException("Windows App SDK app notifications are not supported.");
            }

            manager.Register();
            registered = true;
        }
    }

    private async void OnNotificationInvoked(
        AppNotificationManager sender,
        AppNotificationActivatedEventArgs args)
    {
        try
        {
            if (!args.Arguments.TryGetValue("eventId", out var eventIdText) ||
                !Guid.TryParse(eventIdText, out var eventId) ||
                !args.Arguments.TryGetValue("action", out var actionText) ||
                !Enum.TryParse<ReminderAction>(actionText, ignoreCase: true, out var action))
            {
                return;
            }

            await actionHandler.HandleAsync(eventId, action, CancellationToken.None);
        }
        catch
        {
            // Notification activation must not crash the host process.
        }
    }

    private static string GetActionLabel(ReminderAction action) => action switch
    {
        ReminderAction.Complete => "现在完成",
        ReminderAction.Snooze => "稍后提醒",
        ReminderAction.Skip => "这次跳过",
        _ => "处理",
    };
}
