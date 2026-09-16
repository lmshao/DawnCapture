using System;
using System.Runtime.InteropServices;
using DawnCapture.Helpers;
using DawnCapture.Models;

namespace DawnCapture.Services;

/// <summary>
/// Reacts to what the operating system does to the session while a recording is running.
///
/// Logging off and shutting down kill the process, so the MP4 being written never gets its
/// index and cannot be played. This service runs a short emergency finalize when the system
/// announces a session end. It never vetoes the shutdown: a lost recording is bad, but an
/// operating system that waits for us is worse.
///
/// Locking the screen is not a session end - the recording deliberately continues, because a
/// locked screen does not mean the meeting or the music stopped (see the frame repetition in
/// <c>RecordingService.Capture.cs</c>). Suspending is the opposite: nothing can be captured
/// while the machine sleeps, so the recording is paused and resumed around it, which also
/// keeps the sleeping time out of the timeline.
/// </summary>
public sealed class SessionStateService : IDisposable
{
    private const string WindowClassName = "DawnCapture.SessionStateWindow";
    private const int ErrorClassAlreadyExists = 1410;

    private const uint WmQueryEndSession = 0x0011;
    private const uint WmEndSession = 0x0016;
    private const uint WmPowerBroadcast = 0x0218;
    private const uint WmWtsSessionChange = 0x02B1;

    private const int WtsSessionLock = 0x7;
    private const int WtsSessionUnlock = 0x8;

    private const int PbtApmSuspend = 0x0004;
    private const int PbtApmResumeSuspend = 0x0007;
    private const int PbtApmResumeAutomatic = 0x0012;

    private const int NotifyForThisSession = 0;

    /// <summary>
    /// Deliberately short: writing the index takes a few hundred milliseconds, so needing
    /// more than this means the encoder is in trouble and waiting only delays the shutdown.
    /// </summary>
    private static readonly TimeSpan ShutdownBudget = TimeSpan.FromSeconds(3);

    private readonly IRecordingService _recording;
    private readonly WindowProc _windowProc;
    private IntPtr _hwnd;
    private bool _pausedForSuspend;
    private bool _sessionEndHandled;

    public SessionStateService(IRecordingService recording)
    {
        _recording = recording;
        _windowProc = HandleWindowMessage;
    }

    /// <summary>
    /// Creates the hidden window that receives the session notifications. Must run on the UI
    /// thread, because that thread's message loop is what dispatches them.
    /// </summary>
    public void Attach()
    {
        if (_hwnd != IntPtr.Zero)
        {
            return;
        }

        try
        {
            var windowClass = new WindowClassEx
            {
                cbSize = (uint)Marshal.SizeOf<WindowClassEx>(),
                lpfnWndProc = _windowProc,
                hInstance = GetModuleHandle(null),
                lpszClassName = WindowClassName
            };

            if (RegisterClassEx(ref windowClass) == 0)
            {
                int error = Marshal.GetLastWin32Error();
                if (error != ErrorClassAlreadyExists)
                {
                    Log.Error($"Session state monitoring unavailable: RegisterClassEx failed ({error}).");
                    return;
                }
            }

            // A message-only window cannot receive broadcasts, and both the session end and
            // the power notifications are broadcast to top level windows. This window is
            // never shown, so it stays invisible.
            _hwnd = CreateWindowEx(
                0,
                WindowClassName,
                WindowClassName,
                0,
                0,
                0,
                0,
                0,
                IntPtr.Zero,
                IntPtr.Zero,
                windowClass.hInstance,
                IntPtr.Zero);

            if (_hwnd == IntPtr.Zero)
            {
                Log.Error($"Session state monitoring unavailable: CreateWindowEx failed ({Marshal.GetLastWin32Error()}).");
                return;
            }

            if (WTSRegisterSessionNotification(_hwnd, NotifyForThisSession))
            {
                Log.Info("Session state monitoring started.");
            }
            else
            {
                // Session end and power notifications still arrive without this.
                Log.Info("Session state monitoring started without lock/unlock notifications.");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Session state monitoring could not start", ex);
        }
    }

    public void Dispose()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        WTSUnRegisterSessionNotification(_hwnd);
        DestroyWindow(_hwnd);
        _hwnd = IntPtr.Zero;
    }

    private IntPtr HandleWindowMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WmQueryEndSession:
                // Answering with the default keeps the shutdown unblocked.
                Log.Info("Session ending (log off or shutdown).");
                FinalizeRecordingForSessionEnd();
                break;

            case WmEndSession:
                if (wParam != IntPtr.Zero)
                {
                    FinalizeRecordingForSessionEnd();
                }
                else
                {
                    // Another application vetoed it, so this session keeps running.
                    Log.Info("Session end cancelled.");
                    _sessionEndHandled = false;
                }

                break;

            case WmWtsSessionChange:
                HandleSessionChange(wParam.ToInt32());
                break;

            case WmPowerBroadcast:
                HandlePowerEvent(wParam.ToInt32());
                break;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void FinalizeRecordingForSessionEnd()
    {
        if (_sessionEndHandled)
        {
            return;
        }

        _sessionEndHandled = true;

        // EmergencyFinalize logs its own outcome, so this stays a single line.
        _recording.EmergencyFinalize(ShutdownBudget);
    }

    /// <summary>
    /// Locking is logged only, on purpose: the screen being locked does not mean the session
    /// stopped, and the frame repetition keeps the file continuous while it lasts.
    /// </summary>
    private static void HandleSessionChange(int reason)
    {
        switch (reason)
        {
            case WtsSessionLock:
                Log.Info("Session locked; recording continues.");
                break;

            case WtsSessionUnlock:
                Log.Info("Session unlocked.");
                break;
        }
    }

    private void HandlePowerEvent(int powerEvent)
    {
        switch (powerEvent)
        {
            case PbtApmSuspend:
                PauseForSuspend();
                break;

            case PbtApmResumeAutomatic:
            case PbtApmResumeSuspend:
                ResumeAfterSuspend();
                break;
        }
    }

    private void PauseForSuspend()
    {
        if (_recording.State != RecordingState.Recording)
        {
            return;
        }

        // Only a pause that this service asked for is resumed automatically, so a manual
        // pause is never undone by the machine waking up.
        _pausedForSuspend = true;
        _recording.Pause();
        Log.Info("System suspending; recording paused.");
    }

    private void ResumeAfterSuspend()
    {
        if (!_pausedForSuspend)
        {
            return;
        }

        _pausedForSuspend = false;
        _recording.Resume();
        Log.Info("System resumed; recording continues.");
    }

    private delegate IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClassEx
    {
        public uint cbSize;
        public uint style;
        public WindowProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WindowClassEx windowClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        uint exStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr param);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSRegisterSessionNotification(IntPtr hWnd, int flags);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSUnRegisterSessionNotification(IntPtr hWnd);
}
