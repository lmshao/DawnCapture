using DawnCapture.Models;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;

namespace DawnCapture.Services;

public interface IMonitorService
{
    IReadOnlyList<MonitorDisplay> GetMonitors();

    void RefreshThumbnail(MonitorDisplay monitor);
}

public class MonitorService : IMonitorService
{
    private const int MaxThumbnailWidth = 640;

    public IReadOnlyList<MonitorDisplay> GetMonitors()
    {
        var raw = new List<MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT rect, IntPtr lParam) =>
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(hMonitor, ref info))
            {
                raw.Add(new MonitorInfo
                {
                    Handle = hMonitor,
                    IsPrimary = (info.dwFlags & 1) != 0,
                    X = rect.Left,
                    Y = rect.Top,
                    Width = rect.Right - rect.Left,
                    Height = rect.Bottom - rect.Top
                });
            }

            return true;
        }, IntPtr.Zero);

        var ordered = raw.OrderBy(m => m.X).ThenBy(m => m.Y).ToList();
        var result = new List<MonitorDisplay>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var m = ordered[i];
            result.Add(new MonitorDisplay
            {
                Handle = m.Handle,
                Index = i + 1,
                X = m.X,
                Y = m.Y,
                Width = m.Width,
                Height = m.Height,
                Name = $"Display {i + 1}",
                Resolution = $"{m.Width} x {m.Height}",
                Detail = m.IsPrimary ? $"Primary / {m.Width} x {m.Height}" : $"{m.Width} x {m.Height}",
                IsPrimary = m.IsPrimary,
                Thumbnail = CaptureThumbnail(m.X, m.Y, m.Width, m.Height)
            });
        }

        return result;
    }

    public void RefreshThumbnail(MonitorDisplay monitor)
    {
        monitor.Thumbnail = CaptureThumbnail(monitor.X, monitor.Y, monitor.Width, monitor.Height);
    }

    private static ImageSource? CaptureThumbnail(int x, int y, int width, int height)
    {
        try
        {
            using var full = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(full))
            {
                graphics.CopyFromScreen(x, y, 0, 0, new Size(width, height));
            }

            var thumbnailWidth = Math.Min(width, MaxThumbnailWidth);
            var thumbnailHeight = Math.Max(1, (int)Math.Round(height * (double)thumbnailWidth / width));
            using var thumbnail = new Bitmap(thumbnailWidth, thumbnailHeight, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(thumbnail))
            {
                graphics.DrawImage(full, 0, 0, thumbnailWidth, thumbnailHeight);
            }

            return ToWriteableBitmap(thumbnail);
        }
        catch
        {
            return null;
        }
    }

    private static WriteableBitmap ToWriteableBitmap(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[data.Stride * bitmap.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);

            var writeableBitmap = new WriteableBitmap(bitmap.Width, bitmap.Height);
            using (var stream = writeableBitmap.PixelBuffer.AsStream())
            {
                stream.Write(bytes, 0, bytes.Length);
            }

            return writeableBitmap;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private sealed class MonitorInfo
    {
        public IntPtr Handle { get; init; }
        public bool IsPrimary { get; init; }
        public int X { get; init; }
        public int Y { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }
}
