using System;
using System.IO;

namespace DawnCapture.Models;

public sealed class AppSettings
{
    public string OutputFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "DawnCapture");

    public int FrameRate { get; set; } = 30;

    public int BitrateKbps { get; set; } = 8000;

    public bool CaptureCursor { get; set; } = true;
}
