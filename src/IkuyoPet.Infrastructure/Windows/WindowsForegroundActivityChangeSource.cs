using System.Diagnostics;
using System.Runtime.InteropServices;
using IkuyoPet.Core.WorkTracking;

namespace IkuyoPet.Infrastructure.Windows;

/// <summary>
/// Captures a fresh activity sample for every Windows foreground-window event.
/// The hook lives on a dedicated message thread so the WPF dispatcher is never
/// blocked by native event delivery.
/// </summary>
public sealed class WindowsForegroundActivityChangeSource : IForegroundActivityChangeSource
{
    private const uint EventSystemForeground = 0x0003;
    private const uint WineventOutOfContext = 0;
    private const uint WmQuit = 0x0012;
    private const uint PmNoremove = 0;

    private readonly IActivityProbe probe;
    private readonly ManualResetEventSlim threadReady = new(false);
    private readonly WinEventDelegate callback;
    private Thread? hookThread;
    private uint hookThreadId;
    private IntPtr hookHandle;
    private int state;

    public WindowsForegroundActivityChangeSource(IActivityProbe probe)
    {
        this.probe = probe ?? throw new ArgumentNullException(nameof(probe));
        callback = OnWinEvent;
    }

    public event Action<ActivitySample>? SampleCaptured;

    public void Start()
    {
        if (Interlocked.CompareExchange(ref state, 1, 0) != 0)
        {
            return;
        }

        hookThread = new Thread(HookThreadMain)
        {
            IsBackground = true,
            Name = "IkuyoPet.ForegroundHook",
        };
        hookThread.Start();
        threadReady.Wait();
    }

    public void Shutdown()
    {
        if (Interlocked.Exchange(ref state, 2) == 2)
        {
            return;
        }

        if (hookThreadId != 0)
        {
            _ = PostThreadMessage(hookThreadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        }

        hookThread?.Join(TimeSpan.FromSeconds(2));
    }

    public void Dispose()
    {
        Shutdown();
        threadReady.Dispose();
    }

    private void HookThreadMain()
    {
        try
        {
            hookThreadId = GetCurrentThreadId();
            _ = PeekMessage(out _, IntPtr.Zero, 0, 0, PmNoremove);
            hookHandle = SetWinEventHook(
                EventSystemForeground,
                EventSystemForeground,
                IntPtr.Zero,
                callback,
                0,
                0,
                WineventOutOfContext);
            threadReady.Set();

            if (hookHandle == IntPtr.Zero)
            {
                Debug.WriteLine($"SetWinEventHook failed: {Marshal.GetLastWin32Error()}");
                return;
            }

            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                _ = TranslateMessage(ref message);
                _ = DispatchMessage(ref message);
            }
        }
        catch (Exception exception)
        {
            threadReady.Set();
            Debug.WriteLine($"Foreground activity event source failed: {exception}");
        }
        finally
        {
            if (hookHandle != IntPtr.Zero)
            {
                _ = UnhookWinEvent(hookHandle);
            }

            hookHandle = IntPtr.Zero;
        }
    }

    private void OnWinEvent(
        IntPtr hook,
        uint eventType,
        IntPtr windowHandle,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        if (eventType != EventSystemForeground || Volatile.Read(ref state) != 1)
        {
            return;
        }

        try
        {
            SampleCaptured?.Invoke(probe.Capture());
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Foreground activity capture failed: {exception}");
        }
    }

    private delegate void WinEventDelegate(
        IntPtr hook,
        uint eventType,
        IntPtr windowHandle,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr WindowHandle;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PointX;
        public int PointY;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr moduleHandle,
        WinEventDelegate callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hookHandle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(
        uint threadId,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(
        out NativeMessage message,
        IntPtr windowHandle,
        uint minimumMessage,
        uint maximumMessage);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref NativeMessage message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref NativeMessage message);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(
        out NativeMessage message,
        IntPtr windowHandle,
        uint minimumMessage,
        uint maximumMessage,
        uint removeMessage);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
