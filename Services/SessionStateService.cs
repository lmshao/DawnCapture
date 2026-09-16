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
    private const int GwlWndProc = -4;

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
    private IntPtr _originalWindowProc;
    private bool _pausedForSuspend;
    private bool _sessionEndHandled;

    public SessionStateService(IRecordingService recording)
    {
        _recording = recording;
        _windowProc = HandleWindowMessage;
    }

    /// <summary>
    /// Starts observing the session on the given top level window. Must run on the UI thread:
    /// that thread's message loop is what dispatches the notifications.
    /// </summary>
    public void Attach(IntPtr hwnd)
    {
        if (_hwnd != IntPtr.Zero || hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            _originalWindowProc = SetWindowLongPtr(
                hwnd,
                GwlWndProc,
                Marshal.GetFunctionPointerForDelegate(_windowProc));
            if (_originalWindowProc == IntPtr.Zero)
            {
                Log.Error($"Session state monitoring unavailable: subclassing failed ({Marshal.GetLastWin32Error()}).");
                return;
            }

            _hwnd = hwnd;
            if (WTSRegisterSessionNotification(hwnd, NotifyForThisSession))
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
        SetWindowLongPtr(_hwnd, GwlWndProc, _originalWindowProc);
        _originalWindowProc = IntPtr.Zero;
        _hwnd = IntPtr.Zero;
    }

    private IntPtr HandleWindowMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WmQueryEndSession:
                // Observed only: the window's own procedure answers it below, so the
                // shutdown stays unblocked.
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

        // The window's own procedure stays in charge of the result: that keeps the shutdown
        // unblocked and leaves WinUI responsible for everything else.
        return CallWindowProc(_originalWindowProc, hWnd, msg, wParam, lParam);
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

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int index, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern IntPtr SetWindowLong32(IntPtr hWnd, int index, IntPtr value);

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value) =>
        IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, index, value)
            : SetWindowLong32(hWnd, index, value);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CallWindowProc(IntPtr previous, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSRegisterSessionNotification(IntPtr hWnd, int flags);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSUnRegisterSessionNotification(IntPtr hWnd);
}
