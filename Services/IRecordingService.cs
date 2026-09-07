using System;
using System.Threading.Tasks;
using DawnCapture.Models;
using Windows.Graphics.Capture;

namespace DawnCapture.Services;

public interface IRecordingService
{
    RecordingState State { get; }

    TimeSpan Elapsed { get; }

    event EventHandler<RecordingState>? StateChanged;

    event EventHandler<string>? RecordingFailed;

    /// <summary>Non-fatal recording notices such as resize warnings.</summary>
    event EventHandler<string>? RecordingNotice;

    /// <summary>Records the selected window. Returns whether recording started successfully.</summary>
    Task<bool> StartWindowAsync(GraphicsCaptureItem item, RecordingAudioOptions audioOptions);

    /// <summary>Records the selected display. Returns whether recording started successfully.</summary>
    Task<bool> StartFullScreenAsync(MonitorDisplay display, RecordingAudioOptions audioOptions);

    /// <summary>Lets the user select a screen region and records the cropped area. Returns whether recording started successfully.</summary>
    Task<bool> StartRegionAsync(RecordingAudioOptions audioOptions);

    Task StopAsync();

    void Pause();

    void Resume();
}
