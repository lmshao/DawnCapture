using System;
using System.Threading.Tasks;
using DawnCapture.Models;
using Windows.Graphics;
using Windows.Graphics.Capture;

namespace DawnCapture.Services;

public interface IRecordingService
{
    RecordingState State { get; }

    TimeSpan Elapsed { get; }

    /// <summary>Smoothed audio peak level in the range 0..1 for UI metering.</summary>
    double AudioMeterLevel { get; }

    event EventHandler<RecordingState>? StateChanged;

    event EventHandler<string>? RecordingFailed;

    /// <summary>Non-fatal recording notices such as resize warnings.</summary>
    event EventHandler<string>? RecordingNotice;

    /// <summary>Records the selected window. Returns whether recording started successfully.</summary>
    Task<bool> StartWindowAsync(GraphicsCaptureItem item, RecordingAudioOptions audioOptions);

    /// <summary>Records the selected display. Returns whether recording started successfully.</summary>
    Task<bool> StartFullScreenAsync(MonitorDisplay display, RecordingAudioOptions audioOptions);

    /// <summary>Records the pre-selected screen region. Returns whether recording started successfully.</summary>
    Task<bool> StartRegionAsync(RectInt32 screenRegion, RecordingAudioOptions audioOptions);

    /// <summary>Records microphone and/or system audio to an M4A file.</summary>
    Task<bool> StartAudioOnlyAsync(RecordingAudioOptions audioOptions);

    Task StopAsync();

    void Pause();

    void Resume();
}
