using System;
using Microsoft.UI.Xaml.Media;

namespace DawnCapture.Models;

public sealed class MonitorDisplay
{
    public IntPtr Handle { get; init; }

    public int Index { get; init; }

    public int X { get; init; }

    public int Y { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Resolution { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;

    public bool IsPrimary { get; init; }

    public ImageSource? Thumbnail { get; set; }

    public string BadgeLabel =>
        IsPrimary
            ? $"{Name} · Primary"
            : Name;
}
