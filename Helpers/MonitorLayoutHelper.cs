using DawnCapture.Models;
using System;
using System.Collections.Generic;
using Windows.Graphics;

namespace DawnCapture.Helpers;

public static class MonitorLayoutHelper
{
    /// <summary>
    /// Bounding box of all monitors in virtual desktop coordinates (same as Win32 rcMonitor).
    /// </summary>
    public static RectInt32 GetDesktopBounds(IReadOnlyList<MonitorDisplay> monitors)
    {
        if (monitors.Count == 0)
        {
            return ScreenBoundsHelper.GetVirtualScreenBounds();
        }

        int left = int.MaxValue;
        int top = int.MaxValue;
        int right = int.MinValue;
        int bottom = int.MinValue;
        foreach (var monitor in monitors)
        {
            left = Math.Min(left, monitor.X);
            top = Math.Min(top, monitor.Y);
            right = Math.Max(right, monitor.X + monitor.Width);
            bottom = Math.Max(bottom, monitor.Y + monitor.Height);
        }

        return new RectInt32
        {
            X = left,
            Y = top,
            Width = Math.Max(right - left, 1),
            Height = Math.Max(bottom - top, 1)
        };
    }

    public static (double Left, double Top) GetCanvasPosition(MonitorDisplay monitor, RectInt32 desktopBounds) =>
        (monitor.X - desktopBounds.X, monitor.Y - desktopBounds.Y);

    /// <summary>
    /// Stroke width in canvas DIPs so monitor edges stay visible after Viewbox scaling.
    /// </summary>
    public static double GetPreviewEdgeStrokeThickness(RectInt32 desktopBounds)
    {
        double basis = Math.Min(desktopBounds.Width, desktopBounds.Height);
        return Math.Clamp(basis * 0.006, 6, 22);
    }
}
