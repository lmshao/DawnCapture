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
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace DawnCapture.Helpers;

public static class GraphicsCaptureSnapshotHelper
{
    private const int DefaultMaxWidth = 480;
    private const int CaptureTimeoutMs = 5000;
    private const int PollIntervalMs = 16;

    public static async Task<ImageSource?> CaptureAsync(
        GraphicsCaptureItem item,
        int maxWidth = DefaultMaxWidth,
        CancellationToken cancellationToken = default)
    {
        if (!GraphicsCaptureSession.IsSupported())
        {
            Log.Info("Window preview snapshot skipped: WGC is not supported.");
            return await CreateBitmapOnUiThreadAsync(
                WindowPrintPreviewHelper.TryCapture(item, maxWidth),
                cancellationToken);
        }

        ThumbnailPixelData? pixels = await Task.Run(
            () => CapturePixelsCore(item, maxWidth, cancellationToken),
            cancellationToken);

        if (pixels is null)
        {
            Log.Info($"Window preview WGC snapshot failed for '{item.DisplayName}', trying PrintWindow fallback.");
            pixels = await Task.Run(
                () => WindowPrintPreviewHelper.TryCapture(item, maxWidth),
                cancellationToken);
        }

        if (pixels is null)
        {
            Log.Info($"Window preview capture failed for '{item.DisplayName}'.");
            return null;
        }

        return await CreateBitmapOnUiThreadAsync(pixels, cancellationToken);
    }

    private static async Task<ImageSource?> CreateBitmapOnUiThreadAsync(
        ThumbnailPixelData? pixels,
        CancellationToken cancellationToken)
    {
        if (pixels is null)
        {
            return null;
        }

        ThumbnailPixelData data = pixels.Value;
        DispatcherQueue? dispatcher = App.MainWindow?.DispatcherQueue;
        if (dispatcher is null)
        {
            Log.Info("Window preview bitmap skipped: main window dispatcher is unavailable.");
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
                WriteableBitmap bitmap = RecordingThumbnailHelper.CreateWriteableBitmap(data);
                completion.TrySetResult(bitmap);
            }
            catch (Exception ex)
            {
                Log.Info($"Window preview bitmap creation failed: {ex.Message}");
                completion.TrySetResult(null);
            }
        });

        return await completion.Task.WaitAsync(cancellationToken);
    }

    private static ThumbnailPixelData? CapturePixelsCore(
        GraphicsCaptureItem item,
        int maxWidth,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (item.Size.Width <= 0 || item.Size.Height <= 0)
        {
            Log.Info($"Window preview snapshot skipped: invalid item size {item.Size.Width}x{item.Size.Height}.");
            return null;
        }

        ID3D11Device? d3dDevice = null;
        ID3D11DeviceContext? d3dContext = null;
        IDirect3DDevice? winrtDevice = null;
        Direct3D11CaptureFramePool? framePool = null;
        GraphicsCaptureSession? session = null;
        Direct3D11CaptureFrame? capturedFrame = null;

        try
        {
            Direct3D11Interop.CreateDevice(out d3dDevice, out d3dContext, out winrtDevice);

            framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                winrtDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                item.Size);

            session = framePool.CreateCaptureSession(item);
            session.StartCapture();

            capturedFrame = WaitForFrame(framePool, cancellationToken);
            if (capturedFrame is null)
            {
                Log.Info("Window preview snapshot timed out waiting for the first frame.");
                return null;
            }

            var contentSize = capturedFrame.ContentSize;
            if (contentSize.Width > 0 &&
                contentSize.Height > 0 &&
                (contentSize.Width != item.Size.Width || contentSize.Height != item.Size.Height))
            {
                capturedFrame.Dispose();
                capturedFrame = null;
                framePool.Recreate(
                    winrtDevice,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    2,
                    contentSize);
                capturedFrame = WaitForFrame(framePool, cancellationToken);
            }

            if (capturedFrame is null)
            {
                Log.Info("Window preview snapshot timed out after pool recreate.");
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            return ExtractPixelsFromFrame(capturedFrame, d3dDevice, d3dContext, maxWidth);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Info($"Window preview snapshot failed: {ex.Message}");
            return null;
        }
        finally
        {
            capturedFrame?.Dispose();
            session?.Dispose();
            framePool?.Dispose();
            winrtDevice?.Dispose();
            d3dContext?.Dispose();
            d3dDevice?.Dispose();
        }
    }

    private static Direct3D11CaptureFrame? WaitForFrame(
        Direct3D11CaptureFramePool framePool,
        CancellationToken cancellationToken)
    {
        int attempts = Math.Max(1, CaptureTimeoutMs / PollIntervalMs);
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = framePool.TryGetNextFrame();
            if (frame is not null)
            {
                return frame;
            }

            Thread.Sleep(PollIntervalMs);
        }

        return null;
    }

    internal static ThumbnailPixelData? ExtractPixelsFromFrame(
        Direct3D11CaptureFrame frame,
        ID3D11Device device,
        ID3D11DeviceContext context,
        int maxWidth)
    {
        var contentSize = frame.ContentSize;
        if (contentSize.Width <= 0 || contentSize.Height <= 0)
        {
            return null;
        }

        var sourcePointer = Direct3D11Interop.GetTexture2DPointer(frame.Surface);
        if (sourcePointer == IntPtr.Zero)
        {
            Log.Info("Window preview snapshot failed: unable to access capture surface.");
            return null;
        }

        using var source = new ID3D11Texture2D(sourcePointer);
        var sourceDesc = source.Description;

        int copyWidth = Math.Min(contentSize.Width, (int)sourceDesc.Width);
        int copyHeight = Math.Min(contentSize.Height, (int)sourceDesc.Height);
        if (copyWidth <= 0 || copyHeight <= 0)
        {
            return null;
        }

        var stagingDesc = new Texture2DDescription
        {
            Width = (uint)copyWidth,
            Height = (uint)copyHeight,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None
        };

        using var staging = device.CreateTexture2D(stagingDesc);
        if (copyWidth == (int)sourceDesc.Width && copyHeight == (int)sourceDesc.Height)
        {
            context.CopyResource(staging, source);
        }
        else
        {
            var box = new Box(0, 0, 0, copyWidth, copyHeight, 1);
            context.CopySubresourceRegion(staging, 0, 0, 0, 0, source, 0, box);
        }

        var mapped = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            int rowPitch = (int)mapped.RowPitch;
            int dstStride = copyWidth * 4;
            var pixels = new byte[dstStride * copyHeight];
            for (int y = 0; y < copyHeight; y++)
            {
                Marshal.Copy(mapped.DataPointer + (y * rowPitch), pixels, y * dstStride, dstStride);
            }

            int thumbnailWidth = Math.Min(copyWidth, maxWidth);
            int thumbnailHeight = Math.Max(1, (int)Math.Round(copyHeight * (double)thumbnailWidth / copyWidth));
            if (thumbnailWidth == copyWidth && thumbnailHeight == copyHeight)
            {
                return new ThumbnailPixelData(copyWidth, copyHeight, pixels);
            }

            return ScalePixels(pixels, copyWidth, copyHeight, thumbnailWidth, thumbnailHeight);
        }
        finally
        {
            context.Unmap(staging, 0);
        }
    }

    private static ThumbnailPixelData ScalePixels(
        byte[] sourcePixels,
        int sourceWidth,
        int sourceHeight,
        int targetWidth,
        int targetHeight)
    {
        var scaled = new byte[targetWidth * targetHeight * 4];
        for (int y = 0; y < targetHeight; y++)
        {
            int sourceY = Math.Min(sourceHeight - 1, (int)((long)y * sourceHeight / targetHeight));
            for (int x = 0; x < targetWidth; x++)
            {
                int sourceX = Math.Min(sourceWidth - 1, (int)((long)x * sourceWidth / targetWidth));
                int srcIndex = (sourceY * sourceWidth + sourceX) * 4;
                int dstIndex = (y * targetWidth + x) * 4;
                scaled[dstIndex] = sourcePixels[srcIndex];
                scaled[dstIndex + 1] = sourcePixels[srcIndex + 1];
                scaled[dstIndex + 2] = sourcePixels[srcIndex + 2];
                scaled[dstIndex + 3] = sourcePixels[srcIndex + 3];
            }
        }

        return new ThumbnailPixelData(targetWidth, targetHeight, scaled);
    }
}
