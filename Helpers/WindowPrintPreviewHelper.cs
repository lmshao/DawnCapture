// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;

namespace DawnCapture.Helpers;

internal static class WindowPrintPreviewHelper
{
    private const int PwRenderFullContent = 0x00000002;

    public static ThumbnailPixelData? TryCapture(GraphicsCaptureItem item, int maxWidth)
    {
        if (item.Size.Width <= 0 || item.Size.Height <= 0)
        {
            return null;
        }

        IntPtr hwnd = WindowCaptureHelper.TryResolveWindowHandle(item);
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out NativeRect rect))
        {
            return null;
        }

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        try
        {
            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                IntPtr hdc = graphics.GetHdc();
                try
                {
                    if (!PrintWindow(hwnd, hdc, PwRenderFullContent))
                    {
                        return null;
                    }
                }
                finally
                {
                    graphics.ReleaseHdc(hdc);
                }
            }

            int thumbnailWidth = Math.Min(width, maxWidth);
            int thumbnailHeight = Math.Max(1, (int)Math.Round(height * (double)thumbnailWidth / width));
            using var thumbnail = new Bitmap(thumbnailWidth, thumbnailHeight, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(thumbnail))
            {
                graphics.DrawImage(bitmap, 0, 0, thumbnailWidth, thumbnailHeight);
            }

            return ExtractPixelData(thumbnail);
        }
        catch
        {
            return null;
        }
    }

    private static ThumbnailPixelData ExtractPixelData(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        BitmapData data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new byte[data.Stride * bitmap.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            return new ThumbnailPixelData(bitmap.Width, bitmap.Height, pixels);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint nFlags);
}
