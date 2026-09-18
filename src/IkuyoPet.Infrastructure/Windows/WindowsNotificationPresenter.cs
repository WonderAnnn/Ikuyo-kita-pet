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

public sealed class WindowsAppNotificationSink : INotificationSink, IPendingNotificationTransport, IDisposable
{
    public const string ReminderTag = "ikuyo-pet-reminder";
    public const string ReminderGroup = "ikuyo-pet-reminder";

    private readonly AppNotificationManager? manager;
    private readonly INotificationActionHandler actionHandler;
    private readonly Action<NotificationRequest>? unavailableFallback;
    private readonly Action? unavailableFallbackClear;
    private readonly SinglePendingNotificationCoordinator coordinator;
    private readonly object registrationGate = new();
    private bool registered;

    public WindowsAppNotificationSink(
        INotificationActionHandler actionHandler,
        Action<NotificationRequest>? unavailableFallback = null,
        Action? unavailableFallbackClear = null)
    {
        this.actionHandler = actionHandler ?? throw new ArgumentNullException(nameof(actionHandler));
        this.unavailableFallback = unavailableFallback;
        this.unavailableFallbackClear = unavailableFallbackClear;
        coordinator = new SinglePendingNotificationCoordinator(this);

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

    public async Task ShowAsync(NotificationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (manager is not null) EnsureRegistered();
            await coordinator.ReplaceAsync(request, cancellationToken);
        }
        catch (Exception exception) when (
            exception is COMException or
            PlatformNotSupportedException or
            InvalidOperationException)
        {
            // Unpackaged Windows installs can report support but fail registration.
            // Keep manual reminders visible through the configured tray fallback.
            Debug.WriteLine($"Windows App SDK notification fallback: {exception.Message}");
            unavailableFallback?.Invoke(request);
        }
    }

    public async Task ClearPendingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (manager is not null) EnsureRegistered();
            await coordinator.ClearAsync(cancellationToken);
        }
        catch (Exception exception) when (
            exception is COMException or
            PlatformNotSupportedException or
            InvalidOperationException)
        {
            Debug.WriteLine($"Unable to clear pending Windows notification: {exception.Message}");
        }
    }

    public void Dispose()
    {
        if (manager is not null)
        {
            manager.NotificationInvoked -= OnNotificationInvoked;
            if (registered)
            {
                manager.Unregister();
                registered = false;
            }
        }

        coordinator.Dispose();
    }

    async Task IPendingNotificationTransport.ClearAsync(CancellationToken cancellationToken)
    {
        if (manager is null)
        {
            unavailableFallbackClear?.Invoke();
            return;
        }

        try
        {
            await manager
                .RemoveByTagAndGroupAsync(ReminderTag, ReminderGroup)
                .AsTask(cancellationToken);
        }
        catch (Exception exception) when (
            exception is COMException or
            PlatformNotSupportedException or
            InvalidOperationException)
        {
            Debug.WriteLine($"Unable to remove pending Windows notification: {exception.Message}");
        }
    }

    Task IPendingNotificationTransport.ShowAsync(
        NotificationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (manager is null)
        {
            unavailableFallback?.Invoke(request);
            return Task.CompletedTask;
        }

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
        notification.Tag = ReminderTag;
        notification.Group = ReminderGroup;
        manager.Show(notification);
        return Task.CompletedTask;
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
            await coordinator.ClearAsync(CancellationToken.None);
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
