// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using DawnCapture.Services;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Media.Editing;
using Windows.Storage;
using Windows.Storage.Streams;

namespace DawnCapture.Helpers;

internal readonly record struct ThumbnailPixelData(int Width, int Height, byte[] Pixels);

internal static class RecordingThumbnailHelper
{
    private const int ShellThumbnailSize = 256;

    public static async Task<ThumbnailPixelData?> LoadPixelDataAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        ThumbnailPixelData? shellPixels = await Task.Run(
            () => TryLoadShellPixelData(filePath),
            cancellationToken);
        if (shellPixels is not null)
        {
            return shellPixels;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await TryLoadMediaCompositionPixelDataAsync(filePath, cancellationToken);
    }

    public static WriteableBitmap CreateWriteableBitmap(ThumbnailPixelData data)
    {
        var writeableBitmap = new WriteableBitmap(data.Width, data.Height);
        using var stream = writeableBitmap.PixelBuffer.AsStream();
        stream.Write(data.Pixels, 0, data.Pixels.Length);
        return writeableBitmap;
    }

    public static byte[] CreatePngBytes(ThumbnailPixelData data)
    {
        using var bitmap = new Bitmap(data.Width, data.Height, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, data.Width, data.Height);
        BitmapData bitmapData = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            Marshal.Copy(data.Pixels, 0, bitmapData.Scan0, data.Pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    public static WriteableBitmap CreateBitmapFromPng(byte[] pngBytes)
    {
        using var stream = new MemoryStream(pngBytes);
        using var bitmap = new Bitmap(stream);
        using var normalized = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(normalized))
        {
            graphics.DrawImage(bitmap, 0, 0, normalized.Width, normalized.Height);
        }

        var rect = new Rectangle(0, 0, normalized.Width, normalized.Height);
        BitmapData locked = normalized.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new byte[locked.Stride * normalized.Height];
            Marshal.Copy(locked.Scan0, pixels, 0, pixels.Length);
            return CreateWriteableBitmap(new ThumbnailPixelData(normalized.Width, normalized.Height, pixels));
        }
        finally
        {
            normalized.UnlockBits(locked);
        }
    }

    private static ThumbnailPixelData? TryLoadShellPixelData(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        IntPtr hBitmap = IntPtr.Zero;
        try
        {
            Guid shellItemGuid = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
            int hr = SHCreateItemFromParsingName(
                filePath,
                IntPtr.Zero,
                ref shellItemGuid,
                out IShellItem shellItem);
            if (hr != 0)
            {
                Log.Debug($"Shell thumbnail unavailable for '{filePath}': hr=0x{hr:X8}.");
                return null;
            }

            var factory = (IShellItemImageFactory)shellItem;
            var size = new NativeSize
            {
                cx = ShellThumbnailSize,
                cy = ShellThumbnailSize
            };

            factory.GetImage(
                size,
                Siigbf.ResizeToFit | Siigbf.BiggerSizeOk | Siigbf.ThumbnailOnly,
                out hBitmap);

            if (hBitmap == IntPtr.Zero)
            {
                Log.Debug($"Shell thumbnail unavailable for '{filePath}': empty bitmap handle.");
                return null;
            }

            using var shellBitmap = Image.FromHbitmap(hBitmap);
            using var normalized = new Bitmap(shellBitmap.Width, shellBitmap.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(normalized))
            {
                graphics.DrawImage(shellBitmap, 0, 0, normalized.Width, normalized.Height);
            }

            Log.Debug($"Shell thumbnail loaded for '{filePath}': {normalized.Width}x{normalized.Height}.");
            return ExtractPixelData(normalized);
        }
        catch (Exception ex)
        {
            Log.Debug($"Shell thumbnail failed for '{filePath}': {ex.Message}");
            return null;
        }
        finally
        {
            if (hBitmap != IntPtr.Zero)
            {
                DeleteObject(hBitmap);
            }
        }
    }

    private static async Task<ThumbnailPixelData?> TryLoadMediaCompositionPixelDataAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(filePath);
            MediaClip clip = await MediaClip.CreateFromFileAsync(file);
            cancellationToken.ThrowIfCancellationRequested();

            var composition = new MediaComposition();
            composition.Clips.Add(clip);

            IRandomAccessStreamWithContentType stream = await composition.GetThumbnailAsync(
                TimeSpan.Zero,
                ShellThumbnailSize,
                144,
                VideoFramePrecision.NearestFrame);

            if (stream.Size == 0)
            {
                Log.Debug($"MediaComposition thumbnail empty for '{filePath}'.");
                return null;
            }

            var bytes = new byte[stream.Size];
            using (var reader = new DataReader(stream))
            {
                await reader.LoadAsync((uint)stream.Size);
                reader.ReadBytes(bytes);
            }

            cancellationToken.ThrowIfCancellationRequested();

            using var bitmap = new Bitmap(new MemoryStream(bytes));
            using var normalized = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(normalized))
            {
                graphics.DrawImage(bitmap, 0, 0, normalized.Width, normalized.Height);
            }

            Log.Debug($"MediaComposition thumbnail loaded for '{filePath}': {normalized.Width}x{normalized.Height}.");
            return ExtractPixelData(normalized);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Debug($"MediaComposition thumbnail failed for '{filePath}': {ex.Message}");
            return null;
        }
    }

    private static ThumbnailPixelData ExtractPixelData(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        string path,
        IntPtr bindContext,
        ref Guid riid,
        out IShellItem shellItem);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80a4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        void GetImage(
            NativeSize size,
            Siigbf flags,
            out IntPtr bitmapHandle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int cx;
        public int cy;
    }

    [Flags]
    private enum Siigbf
    {
        ResizeToFit = 0x0,
        BiggerSizeOk = 0x1,
        MemoryOnly = 0x2,
        IconOnly = 0x4,
        ThumbnailOnly = 0x8,
        InCacheOnly = 0x10
    }
}
