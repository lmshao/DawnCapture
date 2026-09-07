using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace DawnCapture.Helpers;

public static class ScreenBoundsHelper
{
    public static RectInt32 GetVirtualScreenBounds()
    {
        var monitors = new List<RectInt32>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate (IntPtr hMonitor, IntPtr hdcMonitor, ref NativeRect rcMonitor, IntPtr data)
        {
            monitors.Add(new RectInt32
            {
                X = rcMonitor.Left,
                Y = rcMonitor.Top,
                Width = rcMonitor.Right - rcMonitor.Left,
                Height = rcMonitor.Bottom - rcMonitor.Top
            });
            return true;
        }, IntPtr.Zero);

        if (monitors.Count == 0)
        {
            return new RectInt32 { X = 0, Y = 0, Width = 1920, Height = 1080 };
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
            Width = right - left,
            Height = bottom - top
        };
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref NativeRect lprcMonitor, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr lprcClip,
        MonitorEnumProc lpfnEnum,
        IntPtr dwData);
}
