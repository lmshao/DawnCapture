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

    /// <summary>Lets the user pick a window and starts recording it. Returns whether recording started successfully.</summary>
    Task<bool> PickAndStartWindowAsync();

    /// <summary>Records the selected display. Returns whether recording started successfully.</summary>
    Task<bool> StartFullScreenAsync(MonitorDisplay display);

    /// <summary>Lets the user select a screen region and records the cropped area. Returns whether recording started successfully.</summary>
    Task<bool> StartRegionAsync();

    Task StopAsync();

    void Pause();

    void Resume();
}
