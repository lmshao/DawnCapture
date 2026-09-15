using System;
using DawnCapture.Models;
using DawnCapture.Views;
using Microsoft.UI.Xaml;

namespace DawnCapture.Services;

public sealed partial class RecordingService
{
    private void AttachRecordingControlWindow(Action<RecordingControlWindow> position)
    {
        var control = new RecordingControlWindow(() => Elapsed, () => CurrentSegmentNumber);
        _controlWindow = control;

        control.StopRequested += async () =>
        {
            try
            {
                await StopAsync();
            }
            finally
            {
                control.CloseWindow();
            }
        };

        control.PauseRequested += () =>
        {
            if (State == RecordingState.Recording)
            {
                Pause();
                control.ShowPaused();
            }
            else if (State == RecordingState.Paused)
            {
                Resume();
                control.ShowRecording();
            }
        };

        control.Closed += (_, _) =>
        {
            if (State is RecordingState.Recording or RecordingState.Paused)
            {
                _ = StopAsync();
            }
        };

        position(control);
    }

    private static double GetMainWindowDpiScale()
    {
        try
        {
            if (App.MainWindow?.Content is FrameworkElement root)
            {
                return root.XamlRoot?.RasterizationScale ?? 1.0;
            }
        }
        catch
        {
            // Fall back to 100% scaling.
        }

        return 1.0;
    }

    private static void MinimizeMainWindow()
    {
        try
        {
            if (App.MainWindow is null)
            {
                return;
            }

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            ShowWindow(hwnd, 6 /* SW_MINIMIZE */);
        }
        catch
        {
            // Ignore window state failures.
        }
    }

    private static void RestoreMainWindow()
    {
        try
        {
            if (App.MainWindow is null)
            {
                return;
            }

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            ShowWindow(hwnd, 9 /* SW_RESTORE */);
        }
        catch
        {
            // Ignore window state failures.
        }
    }
}
