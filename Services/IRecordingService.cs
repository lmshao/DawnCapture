using System;
using System.Threading.Tasks;
using DawnCapture.Models;

namespace DawnCapture.Services;

public interface IRecordingService
{
    RecordingState State { get; }

    TimeSpan Elapsed { get; }

    event EventHandler<RecordingState>? StateChanged;

    event EventHandler<string>? RecordingFailed;

    /// <summary>Records the specified window (HWND). Returns whether recording started successfully.</summary>
    Task<bool> StartWindowAsync(IntPtr window);

    /// <summary>Records the display containing the main window. Returns whether recording started successfully.</summary>
    Task<bool> StartDesktopAsync();

    /// <summary>Lets the user select a screen region and records the cropped area. Returns whether recording started successfully.</summary>
    Task<bool> StartRegionAsync();

    Task StopAsync();

    void Pause();

    void Resume();
}
