using System.Diagnostics;
using System.Runtime.InteropServices;
using IkuyoPet.Core.WorkTracking;

namespace IkuyoPet.Infrastructure.Windows;

public sealed record RunningProcessInfo(string ProcessName, int ProcessId)
{
    public string DisplayName => ProcessName;

    public string Label => $"{DisplayName} (PID {ProcessId})";
}

public interface IRunningProcessInspector
{
    IReadOnlyList<RunningProcessInfo> GetRunningProcesses();
    IReadOnlyList<RunningProcessInfo> GetRunningProcesses(bool forceRefresh);

    ProcessObservation CaptureForeground();
}

public sealed class WindowsRunningProcessInspector : IRunningProcessInspector
{
    private readonly IWindowsSessionProbe sessionProbe;
    private readonly Func<IReadOnlyList<RunningProcessInfo>> processReader;
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan cacheWindow;
    private readonly object cacheGate = new();
    private IReadOnlyList<RunningProcessInfo>? cachedProcesses;
    private DateTimeOffset cacheExpiresAt;

    public WindowsRunningProcessInspector(IWindowsSessionProbe? sessionProbe = null)
        : this(sessionProbe, null, null, null)
    {
    }

    internal WindowsRunningProcessInspector(
        IWindowsSessionProbe? sessionProbe,
        Func<IReadOnlyList<RunningProcessInfo>>? processReader,
        TimeProvider? timeProvider,
        TimeSpan? cacheWindow)
    {
        this.sessionProbe = sessionProbe ?? new WindowsSessionProbe();
        this.processReader = processReader ?? ReadProcessesUncached;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.cacheWindow = cacheWindow ?? TimeSpan.FromSeconds(10);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(this.cacheWindow, TimeSpan.Zero);
    }

    public IReadOnlyList<RunningProcessInfo> GetRunningProcesses() =>
        GetRunningProcesses(forceRefresh: false);

    public IReadOnlyList<RunningProcessInfo> GetRunningProcesses(bool forceRefresh)
    {
        lock (cacheGate)
        {
            var now = timeProvider.GetUtcNow();
            if (!forceRefresh && cachedProcesses is not null && now < cacheExpiresAt)
            {
                return cachedProcesses;
            }

            cachedProcesses = Array.AsReadOnly(Normalize(processReader()).ToArray());
            cacheExpiresAt = now + cacheWindow;
            return cachedProcesses;
        }
    }

    private static List<RunningProcessInfo> ReadProcessesUncached()
    {
        var processes = new List<RunningProcessInfo>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(process.ProcessName))
                {
                    processes.Add(new RunningProcessInfo(process.ProcessName, process.Id));
                }
            }
            catch (InvalidOperationException)
            {
                // The process can exit while the list is being read.
            }
            catch (ArgumentException)
            {
                // The process can exit before its name or identifier is available.
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Access to a protected process is intentionally skipped.
            }
            finally
            {
                process.Dispose();
            }
        }

        return processes;
    }

    public ProcessObservation CaptureForeground()
    {
        var foregroundWindow = GetForegroundWindow();
        var session = sessionProbe.Capture(foregroundWindow);
        if (foregroundWindow == IntPtr.Zero ||
            GetWindowThreadProcessId(foregroundWindow, out var processId) == 0)
        {
            return new ProcessObservation(
                string.Empty,
                session.IsLocked,
                TimeSpan.MaxValue,
                session.IsFullScreen,
                session.IsPresentationMode);
        }

        var processName = string.Empty;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            processName = process.ProcessName;
        }
        catch (ArgumentException)
        {
            // The foreground process can exit between native calls.
        }
        catch (InvalidOperationException)
        {
            // The process can become unavailable during a session transition.
        }

        return new ProcessObservation(
            processName,
            session.IsLocked,
            ReadIdleTime(),
            session.IsFullScreen,
            session.IsPresentationMode);
    }

    internal static IReadOnlyList<RunningProcessInfo> Normalize(
        IEnumerable<RunningProcessInfo> processes)
    {
        ArgumentNullException.ThrowIfNull(processes);
        return processes
            .Where(process => !string.IsNullOrWhiteSpace(process.ProcessName))
            .GroupBy(process => process.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(process => process.ProcessId).First())
            .OrderBy(process => process.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static TimeSpan ReadIdleTime()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
        {
            return TimeSpan.MaxValue;
        }

        var currentTick = unchecked((uint)Environment.TickCount64);
        var elapsedMilliseconds = unchecked(currentTick - info.LastInputTick);
        return TimeSpan.FromMilliseconds(elapsedMilliseconds);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(
        IntPtr windowHandle,
        out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetLastInputInfo(ref LastInputInfo lastInputInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint LastInputTick;
    }
}
