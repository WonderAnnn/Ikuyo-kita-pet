using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using IkuyoPet.Core.Presentation;
using IkuyoPet.Pet;

namespace IkuyoPet.App;

public sealed class TrayIconHost : IDisposable
{
    private readonly MainWindow mainWindow;
    private readonly PetWindow petWindow;
    private readonly Action<TimeSpan>? pauseReminders;
    private readonly Action<bool>? setPetEnabled;
    private readonly Action? exitApplication;
    private readonly TaskbarIcon taskbarIcon;
    private readonly Icon trayIcon;
    private MenuItem? pauseMenuItem;
    private bool allowWindowClose;
    private bool disposed;

    public TrayIconHost(
        MainWindow mainWindow,
        PetWindow petWindow,
        Action<TimeSpan>? pauseReminders = null,
        Action<bool>? setPetEnabled = null,
        Action? exitApplication = null)
    {
        this.mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        this.petWindow = petWindow ?? throw new ArgumentNullException(nameof(petWindow));
        this.pauseReminders = pauseReminders;
        this.setPetEnabled = setPetEnabled;
        this.exitApplication = exitApplication;
        trayIcon = LoadTrayIcon();
        taskbarIcon = new TaskbarIcon
        {
            Icon = trayIcon,
            ToolTipText = "Ikuyo Pet",
            ContextMenu = CreateContextMenu(),
        };
        taskbarIcon.TrayLeftMouseUp += (_, _) => Invoke(TrayCommand.OpenToday);
        taskbarIcon.TrayLeftMouseDoubleClick += (_, _) => Invoke(TrayCommand.OpenToday);
        taskbarIcon.TrayContextMenuOpen += (_, _) => UpdatePauseLabel();
        taskbarIcon.ForceCreate(true);
        mainWindow.Closing += MainWindowOnClosing;
    }

    public bool IsPetVisible => petWindow.IsVisible;
    public DateTimeOffset? PausedUntil { get; private set; }

    public void ShowNotification(string title, string message)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        taskbarIcon.ShowNotification(title, message, H.NotifyIcon.Core.NotificationIcon.Info);
    }

    public void Invoke(TrayCommand command)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        switch (command)
        {
            case TrayCommand.TogglePet:
                var enabled = !petWindow.IsVisible;
                if (enabled) petWindow.Show();
                else petWindow.Hide();
                setPetEnabled?.Invoke(enabled);
                break;
            case TrayCommand.PauseReminders:
                PausedUntil = DateTimeOffset.UtcNow.AddMinutes(30);
                pauseReminders?.Invoke(TimeSpan.FromMinutes(30));
                UpdatePauseLabel();
                break;
            case TrayCommand.OpenToday:
                mainWindow.Show();
                mainWindow.Activate();
                break;
            case TrayCommand.Exit:
                allowWindowClose = true;
                petWindow.Close();
                exitApplication?.Invoke();
                if (exitApplication is null) Application.Current?.Shutdown();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), command, null);
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        mainWindow.Closing -= MainWindowOnClosing;
        taskbarIcon.Dispose();
        trayIcon.Dispose();
    }

    private static Icon LoadTrayIcon()
    {
        try
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
            {
                using var associatedIcon = Icon.ExtractAssociatedIcon(processPath!);
                if (associatedIcon is not null) return (Icon)associatedIcon.Clone();
            }
        }
#pragma warning disable CA1031
        catch (Exception)
#pragma warning restore CA1031
        {
        }

        return (Icon)SystemIcons.Application.Clone();
    }
    private ContextMenu CreateContextMenu()
    {
        var menu = new ContextMenu();
        foreach (var item in TrayMenuModel.T1)
        {
            var menuItem = new MenuItem { Header = item.Label, Tag = item.Command };
            if (item.Command == TrayCommand.PauseReminders) pauseMenuItem = menuItem;
            menuItem.Click += (_, _) => Invoke((TrayCommand)menuItem.Tag);
            menu.Items.Add(menuItem);
        }

        return menu;
    }

    private void UpdatePauseLabel()
    {
        if (pauseMenuItem is null) return;
        if (PausedUntil is not { } until || until <= DateTimeOffset.UtcNow)
        {
            pauseMenuItem.Header = "暂停提醒 30 分钟";
            return;
        }

        var remaining = until - DateTimeOffset.UtcNow;
        pauseMenuItem.Header = $"暂停中（剩余 {Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes))} 分钟）";
    }

    private void MainWindowOnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (allowWindowClose) return;
        e.Cancel = true;
        mainWindow.Hide();
    }
}