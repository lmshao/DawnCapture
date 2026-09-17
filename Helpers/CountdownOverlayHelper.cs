// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

using DawnCapture.Models;
using DawnCapture.Services;
using DawnCapture.Views;
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Graphics;

namespace DawnCapture.Helpers;

public static class CountdownOverlayHelper
{
    public static RectInt32 ResolveTargetBounds(
        CaptureModeKind mode,
        MonitorDisplay? display,
        WindowCaptureTarget? window,
        RegionCaptureTarget? region,
        IMonitorService monitorService)
    {
        switch (mode)
        {
            case CaptureModeKind.FullScreen when display is not null:
                return ScreenBoundsHelper.FromMonitorDisplay(display);

            case CaptureModeKind.Window when window is not null:
                {
                    IntPtr hwnd = WindowCaptureHelper.TryResolveWindowHandle(window.Item);
                    return ScreenBoundsHelper.GetMonitorBoundsFromWindow(hwnd);
                }

            case CaptureModeKind.Region when region is not null:
                {
                    var bounds = region.ScreenBounds;
                    int centerX = bounds.X + bounds.Width / 2;
                    int centerY = bounds.Y + bounds.Height / 2;
                    return ScreenBoundsHelper.GetMonitorBoundsFromPoint(centerX, centerY);
                }

            case CaptureModeKind.AudioOnly:
                {
                    var monitors = monitorService.GetMonitors();
                    MonitorDisplay? primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault();
                    if (primary is not null)
                    {
                        return ScreenBoundsHelper.FromMonitorDisplay(primary);
                    }

                    break;
                }
        }

        return ScreenBoundsHelper.GetPrimaryMonitorBounds()
               ?? ScreenBoundsHelper.GetVirtualScreenBounds();
    }

    /// <summary>Counts down the given number of seconds, or not at all when it is zero.</summary>
    public static async Task<bool> RunAsync(RectInt32 targetBounds, int seconds)
    {
        if (seconds <= 0)
        {
            return true;
        }

        using var overlay = new CountdownOverlayNative(targetBounds);
        return await overlay.RunCountdownAsync(seconds);
    }
}
