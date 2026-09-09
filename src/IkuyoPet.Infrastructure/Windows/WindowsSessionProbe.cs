using System.Runtime.InteropServices;

namespace IkuyoPet.Infrastructure.Windows;

public interface IWindowsSessionProbe
{
    WindowsSessionState Capture(IntPtr foregroundWindow);
}

public sealed record WindowsSessionState(
    bool IsLocked,
    bool IsFullScreen,
    bool IsPresentationMode)
{
    public bool SuppressActiveWork => IsLocked || IsFullScreen || IsPresentationMode;
}

public sealed class WindowsSessionProbe : IWindowsSessionProbe
{
    private readonly Func<bool> isLocked;
    private readonly Func<IntPtr, bool> isFullScreen;
    private readonly Func<bool> isPresentationMode;

    public WindowsSessionProbe()
        : this(ReadLocked, ReadFullScreen, ReadPresentationMode)
    {
    }

    internal WindowsSessionProbe(
        Func<bool> isLocked,
        Func<IntPtr, bool> isFullScreen,
        Func<bool> isPresentationMode)
    {
        this.isLocked = isLocked ?? throw new ArgumentNullException(nameof(isLocked));
        this.isFullScreen = isFullScreen ?? throw new ArgumentNullException(nameof(isFullScreen));
        this.isPresentationMode = isPresentationMode ?? throw new ArgumentNullException(nameof(isPresentationMode));
    }

    public WindowsSessionState Capture(IntPtr foregroundWindow) => new(
        isLocked(),
        isFullScreen(foregroundWindow),
        isPresentationMode());

    private static bool ReadLocked()
    {
        const uint DesktopReadObjects = 0x0001;
        var inputDesktop = OpenInputDesktop(0, false, DesktopReadObjects);
        if (inputDesktop == IntPtr.Zero)
        {
            return true;
        }

        try
        {
            return IsLockedDesktopName(ReadDesktopName(inputDesktop));
        }
        finally
        {
            _ = CloseDesktop(inputDesktop);
        }
    }

    internal static bool IsLockedDesktopName(string? desktopName) =>
        !string.Equals(desktopName, "Default", StringComparison.OrdinalIgnoreCase);

    private static string? ReadDesktopName(IntPtr desktop)
    {
        const int UserObjectName = 2;
        _ = GetUserObjectInformation(desktop, UserObjectName, IntPtr.Zero, 0, out var requiredBytes);
        if (requiredBytes == 0)
        {
            return null;
        }

        var buffer = Marshal.AllocHGlobal(checked((int)requiredBytes));
        try
        {
            return GetUserObjectInformation(desktop, UserObjectName, buffer, requiredBytes, out _)
                ? Marshal.PtrToStringUni(buffer)
                : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool ReadFullScreen(IntPtr foregroundWindow)
    {
        if (foregroundWindow == IntPtr.Zero ||
            foregroundWindow == GetDesktopWindow() ||
            foregroundWindow == GetShellWindow() ||
            !GetWindowRect(foregroundWindow, out var windowRect))
        {
            return false;
        }

        var monitor = MonitorFromWindow(foregroundWindow, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var monitorInfo = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        const int tolerance = 1;
        return windowRect.Left <= monitorInfo.Monitor.Left + tolerance &&
               windowRect.Top <= monitorInfo.Monitor.Top + tolerance &&
               windowRect.Right >= monitorInfo.Monitor.Right - tolerance &&
               windowRect.Bottom >= monitorInfo.Monitor.Bottom - tolerance;
    }

    private static bool ReadPresentationMode()
    {
        var result = SHQueryUserNotificationState(out var state);
        return result == 0 && state == QueryUserNotificationState.PresentationMode;
    }

    private const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(
        uint flags,
        [MarshalAs(UnmanagedType.Bool)] bool inherit,
        uint desiredAccess);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserObjectInformation(
        IntPtr userObject,
        int index,
        IntPtr information,
        uint length,
        out uint lengthNeeded);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(IntPtr desktop);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out QueryUserNotificationState state);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    private enum QueryUserNotificationState
    {
        NotPresent = 1,
        Busy = 2,
        RunningDirect3DFullScreen = 3,
        PresentationMode = 4,
        AcceptsNotifications = 5,
        QuietTime = 6,
        App = 7,
    }
}
