using System.Security;
using Microsoft.Win32;

namespace IkuyoPet.Infrastructure.Windows;

public interface IStartupEntryStore
{
    string? Read(string valueName);
    void Write(string valueName, string value);
    void Delete(string valueName);
}

public sealed record StartupChangeResult(bool Success, string? ErrorMessage = null);
public sealed record StartupStateResult(bool Success, bool Enabled, string? ErrorMessage = null);

public sealed class WindowsStartupManager
{
    public const string EntryName = "IkuyoPet";

    private readonly IStartupEntryStore store;
    private readonly string executablePath;

    public WindowsStartupManager(IStartupEntryStore store, string executablePath)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.executablePath = string.IsNullOrWhiteSpace(executablePath)
            ? throw new ArgumentException("Executable path is required.", nameof(executablePath))
            : executablePath;
    }

    public bool IsEnabled => ReadStatus().Enabled;

    public StartupStateResult ReadStatus()
    {
        try
        {
            return new StartupStateResult(
                true,
                string.Equals(store.Read(EntryName), executablePath, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException or IOException or InvalidOperationException)
        {
            return new StartupStateResult(false, false, exception.Message);
        }
    }
    public StartupChangeResult SetEnabled(bool enabled)
    {
        try
        {
            if (enabled)
            {
                store.Write(EntryName, executablePath);
            }
            else
            {
                store.Delete(EntryName);
            }

            return new StartupChangeResult(true);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException or IOException or InvalidOperationException)
        {
            return new StartupChangeResult(false, exception.Message);
        }
    }
}

public sealed class CurrentUserStartupEntryStore : IStartupEntryStore
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? Read(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(valueName) as string;
    }

    public void Write(string valueName, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Unable to open the current-user startup key.");
        key.SetValue(valueName, value, RegistryValueKind.String);
    }

    public void Delete(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}
