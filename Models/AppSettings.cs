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

    public int VideoCodecIndex { get; set; }

    public bool CountdownEnabled { get; set; } = true;

    public bool NotificationEnabled { get; set; } = true;
}
