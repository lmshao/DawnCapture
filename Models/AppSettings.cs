using System;
using System.IO;

namespace DawnCapture.Models;

public sealed class AppSettings
{
    public string OutputFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "DawnCapture");

    public int FrameRate { get; set; } = 30;

    public int BitrateKbps { get; set; } = 8000;

    public VideoBitrateMode BitrateMode { get; set; } = VideoBitrateMode.Adaptive;

    public bool CaptureCursor { get; set; } = true;

    public string Language { get; set; } = "system";

    public int QualityIndex { get; set; } = 1;

    public int AudioQualityIndex { get; set; } = 1;

    /// <summary>
    /// The user's intent, not the live state: a source that is switched on while its device is
    /// missing stays on, so it becomes effective again once the device is back. Defaults match
    /// the capture page's previous hardcoded values.
    /// </summary>
    public bool MicrophoneEnabled { get; set; }

    public bool SystemAudioEnabled { get; set; } = true;

    /// <summary>
    /// WASAPI endpoint id (MMDevice.ID) to capture from; null follows the system default. The
    /// id is stored rather than the friendly name because names change and can collide.
    /// </summary>
    public string? MicrophoneDeviceId { get; set; }

    public string? SystemAudioDeviceId { get; set; }

    public int VideoCodecIndex { get; set; }

    public bool CountdownEnabled { get; set; } = true;

    public bool NotificationEnabled { get; set; } = true;

    public HotkeyBinding HotkeyToggleRecording { get; set; } = HotkeyBinding.ToggleRecordingDefault;

    public HotkeyBinding HotkeyTogglePause { get; set; } = HotkeyBinding.TogglePauseDefault;

    public CloseMainWindowAction CloseMainWindowAction { get; set; } = CloseMainWindowAction.MinimizeToTray;

    public bool SegmentEnabled { get; set; } = false;

    public SegmentLimitMode SegmentLimitMode { get; set; } = SegmentLimitMode.Duration;

    public int SegmentMinutes { get; set; } = 120;

    public int SegmentSizeMb { get; set; } = 2048;

    /// <summary>0 means no limit.</summary>
    public int MaxRecordingMinutes { get; set; } = 0;
}
