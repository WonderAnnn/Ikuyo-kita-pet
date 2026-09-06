using System.Diagnostics;
using System.Runtime.InteropServices;
using IkuyoPet.Core.WorkTracking;

namespace IkuyoPet.Infrastructure.Windows;

public sealed class ForegroundActivityProbe : IActivityProbe
{
    private readonly HashSet<string> _whitelist;
    private readonly TimeProvider _timeProvider;

    public ForegroundActivityProbe(
        IEnumerable<string> whitelist,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(whitelist);
        _whitelist = new HashSet<string>(whitelist, StringComparer.OrdinalIgnoreCase);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ActivitySample Capture()
    {
        var observedAt = _timeProvider.GetUtcNow();
        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero ||
            GetWindowThreadProcessId(foregroundWindow, out var processId) == 0)
        {
            return new ActivitySample(string.Empty, false, true, TimeSpan.MaxValue, observedAt);
        }

        var processName = string.Empty;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            processName = process.ProcessName;
        }
        catch (ArgumentException)
        {
            // The foreground process may exit between the native calls.
        }
        catch (InvalidOperationException)
        {
            // The process may no longer be queryable during session transitions.
        }

        return new ActivitySample(
            processName,
            _whitelist.Contains(processName),
            false,
            ReadIdleTime(),
            observedAt);
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