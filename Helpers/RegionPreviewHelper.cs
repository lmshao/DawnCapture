// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DawnCapture.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;

namespace DawnCapture.Helpers;

public static class RegionPreviewHelper
{
    private const int DefaultMaxWidth = 480;

    public static async Task<ImageSource?> CaptureThumbnailAsync(
        RectInt32 region,
        int maxWidth = DefaultMaxWidth,
        CancellationToken cancellationToken = default)
    {
        ThumbnailPixelData? pixels = await Task.Run(
            () => CapturePixels(region, maxWidth, cancellationToken),
            cancellationToken);

        if (pixels is null)
        {
            return null;
        }

        ThumbnailPixelData data = pixels.Value;
        DispatcherQueue? dispatcher = App.MainWindow?.DispatcherQueue;
        if (dispatcher is null)
        {
            Log.Info("Region preview bitmap skipped: main window dispatcher is unavailable.");
            return null;
        }

        var completion = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.TryEnqueue(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetResult(null);
                return;
            }

            try
            {
                completion.TrySetResult(RecordingThumbnailHelper.CreateWriteableBitmap(data));
            }
            catch (Exception ex)
            {
                Log.Info($"Region preview bitmap creation failed: {ex.Message}");
                completion.TrySetResult(null);
            }
        });

        return await completion.Task.WaitAsync(cancellationToken);
    }

    private static ThumbnailPixelData? CapturePixels(
        RectInt32 region,
        int maxWidth,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (region.Width <= 0 || region.Height <= 0)
        {
            return null;
        }

        try
        {
            using var full = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(full))
            {
                graphics.CopyFromScreen(
                    region.X,
                    region.Y,
                    0,
                    0,
                    new Size(region.Width, region.Height),
                    CopyPixelOperation.SourceCopy);
            }

            int thumbnailWidth = Math.Min(region.Width, maxWidth);
            int thumbnailHeight = Math.Max(1, (int)Math.Round(region.Height * (double)thumbnailWidth / region.Width));
            if (thumbnailWidth == region.Width && thumbnailHeight == region.Height)
            {
                return ExtractPixelData(full);
            }

            using var thumbnail = new Bitmap(thumbnailWidth, thumbnailHeight, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(thumbnail))
            {
                graphics.DrawImage(full, 0, 0, thumbnailWidth, thumbnailHeight);
            }

            return ExtractPixelData(thumbnail);
        }
        catch (Exception ex)
        {
            Log.Info($"Region preview capture failed: {ex.Message}");
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
}
