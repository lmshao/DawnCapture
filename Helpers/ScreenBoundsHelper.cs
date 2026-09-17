// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

using DawnCapture.Models;
using DawnCapture.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace DawnCapture.Helpers;

public static class ScreenBoundsHelper
{
    private const uint MonitorDefaultToNearest = 2;
    private const int MonitorInfoPrimaryFlag = 1;
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

    public static RectInt32 FromMonitorDisplay(MonitorDisplay monitor) =>
        new()
        {
            X = monitor.X,
            Y = monitor.Y,
            Width = monitor.Width,
            Height = monitor.Height
        };

    public static RectInt32? TryGetMonitorBounds(IntPtr hMonitor)
    {
        var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(hMonitor, ref info))
        {
            return null;
        }

        return FromNativeRect(info.rcMonitor);
    }

    public static RectInt32 GetMonitorBoundsFromPoint(int x, int y)
    {
        var monitor = MonitorFromPoint(new NativePoint { X = x, Y = y }, MonitorDefaultToNearest);
        return TryGetMonitorBounds(monitor)
               ?? new RectInt32 { X = 0, Y = 0, Width = 1920, Height = 1080 };
    }

    public static RectInt32 GetMonitorBoundsFromWindow(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
        {
            var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (TryGetMonitorBounds(monitor, out RectInt32? bounds) && bounds is not null)
            {
                return bounds.Value;
            }
        }

        return GetPrimaryMonitorBounds() ?? GetVirtualScreenBounds();
    }

    public static RectInt32? GetPrimaryMonitorBounds()
    {
        RectInt32? primary = null;
        EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            delegate (IntPtr hMonitor, IntPtr hdcMonitor, ref NativeRect rcMonitor, IntPtr data)
            {
                var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
                if (GetMonitorInfo(hMonitor, ref info) &&
                    (info.dwFlags & MonitorInfoPrimaryFlag) != 0)
                {
                    primary = FromNativeRect(info.rcMonitor);
                    return false;
                }

                primary ??= FromNativeRect(rcMonitor);
                return true;
            },
            IntPtr.Zero);

        return primary;
    }

    private static bool TryGetMonitorBounds(IntPtr hMonitor, out RectInt32? bounds)
    {
        bounds = TryGetMonitorBounds(hMonitor);
        return bounds is not null;
    }

    private static RectInt32 FromNativeRect(NativeRect rect) =>
        new()
        {
            X = rect.Left,
            Y = rect.Top,
            Width = rect.Right - rect.Left,
            Height = rect.Bottom - rect.Top
        };

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref NativeRect lprcMonitor, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int cbSize;
        public NativeRect rcMonitor;
        public NativeRect rcWork;
        public int dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr lprcClip,
        MonitorEnumProc lpfnEnum,
        IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint pt, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);
}
