// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using DawnCapture.Services;
using Vortice.Direct3D11;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace DawnCapture.Helpers;

/// <summary>
/// Long-lived Windows Graphics Capture preview session for the capture page.
///
/// The session is intentionally split from the device/frame-pool: stopping the
/// preview only disposes <see cref="GraphicsCaptureSession"/> (the actively
/// capturing part) while the D3D device and frame pool are cached so resuming
/// from the background is cheap. <see cref="Dispose"/> releases everything.
/// </summary>
internal sealed class WindowLivePreviewSession : IDisposable
{
    private const DirectXPixelFormat PixelFormat = DirectXPixelFormat.B8G8R8A8UIntNormalized;
    private const int BufferCount = 2;

    private readonly object _frameLock = new();
    private readonly object _lifecycleLock = new();

    private ID3D11Device? _d3dDevice;
    private ID3D11DeviceContext? _d3dContext;
    private IDirect3DDevice? _winrtDevice;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFrame? _latestFrame;
    private SizeInt32 _poolSize;
    private int _activeExtractions;
    private bool _disposeRequested;
    private bool _resourcesReleased;

    public bool IsRunning => _session is not null;

    public void Start(GraphicsCaptureItem item)
    {
        if (_resourcesReleased)
        {
            throw new ObjectDisposedException(nameof(WindowLivePreviewSession));
        }

        EnsureDevice();

        if (!ReferenceEquals(_item, item))
        {
            Stop();
            _item = item;
        }

        EnsureFramePool(item.Size);

        if (_session is null)
        {
            _session = _framePool!.CreateCaptureSession(item);
            _session.StartCapture();
            Log.Info($"Window live preview session started for '{item.DisplayName}'.");
        }
    }

    public void Stop()
    {
        if (_session is not null)
        {
            _session.Dispose();
            _session = null;
            Log.Info("Window live preview session stopped.");
        }

        lock (_frameLock)
        {
            _latestFrame?.Dispose();
            _latestFrame = null;
        }
    }

    public Task<ThumbnailPixelData?> TryExtractLatestPixelsAsync(int maxWidth)
    {
        Direct3D11CaptureFrame? frame;
        lock (_frameLock)
        {
            frame = _latestFrame;
            _latestFrame = null;
        }

        if (frame is null || _d3dDevice is null || _d3dContext is null)
        {
            frame?.Dispose();
            return Task.FromResult<ThumbnailPixelData?>(null);
        }

        lock (_lifecycleLock)
        {
            if (_disposeRequested)
            {
                frame.Dispose();
                return Task.FromResult<ThumbnailPixelData?>(null);
            }

            _activeExtractions++;
        }

        ID3D11Device device = _d3dDevice;
        ID3D11DeviceContext context = _d3dContext;

        return Task.Run(() =>
        {
            try
            {
                return GraphicsCaptureSnapshotHelper.ExtractPixelsFromFrame(
                    frame,
                    device,
                    context,
                    maxWidth);
            }
            catch (Exception ex)
            {
                Log.Info($"Window live preview frame extraction failed: {ex.Message}");
                return null;
            }
            finally
            {
                frame.Dispose();
                lock (_lifecycleLock)
                {
                    _activeExtractions--;
                    if (_disposeRequested && _activeExtractions == 0)
                    {
                        ReleaseDeviceResources();
                    }
                }
            }
        });
    }

    private void EnsureDevice()
    {
        if (_d3dDevice is not null)
        {
            return;
        }

        Direct3D11Interop.CreateDevice(out _d3dDevice, out _d3dContext, out _winrtDevice);
    }

    private void EnsureFramePool(SizeInt32 size)
    {
        if (_framePool is null)
        {
            _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _winrtDevice!,
                PixelFormat,
                BufferCount,
                size);
            _framePool.FrameArrived += OnFrameArrived;
            _poolSize = size;
            return;
        }

        if (_poolSize.Width == size.Width && _poolSize.Height == size.Height)
        {
            return;
        }

        _framePool.Recreate(
            _winrtDevice!,
            PixelFormat,
            BufferCount,
            size);
        _poolSize = size;
        Log.Info($"Window live preview frame pool recreated: {size.Width}x{size.Height}.");
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        var frame = sender.TryGetNextFrame();
        if (frame is null)
        {
            return;
        }

        var contentSize = frame.ContentSize;
        if (contentSize.Width > 0 &&
            contentSize.Height > 0 &&
            (contentSize.Width != _poolSize.Width || contentSize.Height != _poolSize.Height))
        {
            frame.Dispose();
            try
            {
                sender.Recreate(
                    _winrtDevice!,
                    PixelFormat,
                    BufferCount,
                    contentSize);
                _poolSize = contentSize;
                Log.Info($"Window live preview frame pool resized from frame: {contentSize.Width}x{contentSize.Height}.");
            }
            catch (Exception ex)
            {
                Log.Info($"Window live preview frame pool recreate failed: {ex.Message}");
            }

            return;
        }

        lock (_frameLock)
        {
            _latestFrame?.Dispose();
            _latestFrame = frame;
        }
    }

    public void Dispose()
    {
        Stop();

        lock (_lifecycleLock)
        {
            _disposeRequested = true;
            if (_activeExtractions == 0)
            {
                ReleaseDeviceResources();
            }
        }
    }

    private void ReleaseDeviceResources()
    {
        if (_resourcesReleased)
        {
            return;
        }

        _resourcesReleased = true;

        if (_framePool is not null)
        {
            _framePool.FrameArrived -= OnFrameArrived;
            _framePool.Dispose();
            _framePool = null;
        }

        _winrtDevice?.Dispose();
        _winrtDevice = null;
        _d3dContext?.Dispose();
        _d3dContext = null;
        _d3dDevice?.Dispose();
        _d3dDevice = null;
        _item = null;
        _poolSize = default;
        Log.Info("Window live preview resources released.");
    }
}
