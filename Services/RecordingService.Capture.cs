using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DawnCapture.Helpers;
using DawnCapture.Models;
using DawnCapture.Services.Audio;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace DawnCapture.Services;

public sealed partial class RecordingService
{
    private async Task<bool> StartCaptureAsync(GraphicsCaptureItem item, RectInt32? crop)
    {
        try
        {
            EnsureDevice();

            _frameDuration = 10_000_000L / Math.Max(1, _settings.Current.FrameRate);
            _framesWritten = 0;
            _isPaused = false;
            _cropRect = crop;
            Log.Debug($"Capture started: DisplayName={item.DisplayName}, Size={item.Size.Width}x{item.Size.Height}, Crop={crop?.Width}x{crop?.Height}");
            _closedEvent.Reset();
            _frameEvent.Reset();

            lock (_frameLock)
            {
                ResetVideoPacingLocked();
            }

            _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _winrtDevice!,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                item.Size);

            _session = _framePool.CreateCaptureSession(item);
            _session.IsCursorCaptureEnabled = _settings.Current.CaptureCursor;
            Log.Debug($"Cursor capture enabled: {_session.IsCursorCaptureEnabled}");
            _framePool.FrameArrived += OnFrameArrived;

            _item = item;
            _item.Closed += OnItemClosed;

            var captureSize = crop is { } c
                ? new SizeInt32 { Width = c.Width, Height = c.Height }
                : item.Size;

            if (_currentSourceKind == RecordingSourceKind.Window)
            {
                _windowLetterboxActive = true;
                _windowEncodeSize = captureSize;
                _poolContentSize = item.Size;
                _windowResizeWarned = false;
                if (!EnsureWindowCompositeResources(_windowEncodeSize))
                {
                    CleanupCapture();
                    State = RecordingState.Idle;
                    RaiseFailed(LocalizationService.GetString("Failure_CreateWindowComposite"));
                    return false;
                }
            }
            else
            {
                _windowLetterboxActive = false;
            }

            if (!await CreateMediaObjectsAsync(captureSize, _audioOptions))
            {
                CleanupCapture();
                return false;
            }

            if (_audioOptions.HasAnySource)
            {
                _audioCancellation = new CancellationTokenSource();
                _audioPipeline = new AudioCapturePipeline();
            }

            _stopwatch.Restart();
            _isRecording = true;

            if (_audioPipeline is not null)
            {
                try
                {
                    await _audioPipeline.StartAsync(_audioOptions, _stopwatch, _audioCancellation!.Token);
                    RaiseAudioStartupWarnings();
                }
                catch (Exception ex)
                {
                    _isRecording = false;
                    CleanupCapture();
                    State = RecordingState.Idle;
                    RaiseFailed(ex.Message);
                    return false;
                }
            }

            _session.StartCapture();

            State = RecordingState.Recording;
            return true;
        }
        catch (Exception ex)
        {
            CleanupCapture();
            State = RecordingState.Idle;
            RaiseFailed(ex.Message);
            return false;
        }
    }

    private void EnsureDevice()
    {
        if (_winrtDevice is not null)
        {
            return;
        }

        var result = D3D11.D3D11CreateDevice(
            null,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            null!,
            out _d3dDevice,
            out _,
            out _d3dContext);

        if (result.Failure)
        {
            result = D3D11.D3D11CreateDevice(
                null,
                DriverType.Warp,
                DeviceCreationFlags.BgraSupport,
                null!,
                out _d3dDevice,
                out _,
                out _d3dContext);
        }

        if (result.Failure)
        {
            throw new InvalidOperationException(
                string.Format(LocalizationService.GetString("Error_CreateD3DDevice"), result));
        }

        using var dxgiDevice = _d3dDevice.QueryInterface<IDXGIDevice>();
        int hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var pWinrtDevice);
        if (hr != 0)
        {
            throw new InvalidOperationException(
                string.Format(LocalizationService.GetString("Error_CreateDirect3DDevice"), hr));
        }

        _winrtDevice = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(pWinrtDevice);
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        var frame = sender.TryGetNextFrame();
        if (frame is null)
        {
            return;
        }

        if (!_isRecording || _isPaused)
        {
            frame.Dispose();
            return;
        }

        if (_windowLetterboxActive)
        {
            var contentSize = frame.ContentSize;
            if (contentSize.Width <= 0 || contentSize.Height <= 0)
            {
                frame.Dispose();
                return;
            }

            if (contentSize.Width != _poolContentSize.Width ||
                contentSize.Height != _poolContentSize.Height)
            {
                if (!_windowResizeWarned)
                {
                    _windowResizeWarned = true;
                    RaiseNotice(LocalizationService.GetString("AppStatus_WindowResizedDuringRecording"));
                }

                frame.Dispose();
                try
                {
                    if (_winrtDevice is not null)
                    {
                        sender.Recreate(
                            _winrtDevice,
                            DirectXPixelFormat.B8G8R8A8UIntNormalized,
                            2,
                            contentSize);
                        _poolContentSize = contentSize;
                        Log.Debug($"Window capture pool recreated: {contentSize.Width}x{contentSize.Height}");
                    }
                }
                catch (Exception ex)
                {
                    Log.Info($"Window capture pool recreate failed: {ex.Message}");
                }

                return;
            }
        }

        lock (_frameLock)
        {
            _pendingFrame?.Dispose();
            _pendingFrame = frame;
            _pendingFrameSequence++;
        }

        _frameEvent.Set();
    }

    private void OnItemClosed(GraphicsCaptureItem sender, object args)
    {
        if (_isRecording)
        {
            _ = StopAsync();
        }
    }

    private bool WaitForVideoSample(
        out Direct3D11CaptureFrame? frame,
        out TimeSpan timestamp,
        out TimeSpan duration)
    {
        frame = null;
        timestamp = default;
        duration = default;
        var targetInterval = TimeSpan.FromTicks(_frameDuration);

        while (true)
        {
            if (_closedEvent.WaitOne(0))
            {
                return false;
            }

            if (!_isRecording && !_isStopping)
            {
                return false;
            }

            var elapsed = _stopwatch.Elapsed;

            if (!_isStopping && elapsed > _nextVideoOutputAt + targetInterval)
            {
                _nextVideoOutputAt = elapsed;
            }

            lock (_frameLock)
            {
                if (_isStopping)
                {
                    if (_pendingFrame is null)
                    {
                        return false;
                    }

                    return TryEmitVideoSampleLocked(
                        elapsed,
                        targetInterval,
                        requireSchedule: false,
                        out frame,
                        out timestamp,
                        out duration);
                }

                if (elapsed + TimeSpan.FromMilliseconds(2) < _nextVideoOutputAt)
                {
                    // Wait for the next target output slot on the master clock.
                }
                else if (_pendingFrame is null ||
                         _pendingFrameSequence <= _lastEmittedFrameSequence)
                {
                    _nextVideoOutputAt += targetInterval;
                    if (elapsed > _nextVideoOutputAt)
                    {
                        _nextVideoOutputAt = elapsed;
                    }
                }
                else if (TryEmitVideoSampleLocked(
                             elapsed,
                             targetInterval,
                             requireSchedule: true,
                             out frame,
                             out timestamp,
                             out duration))
                {
                    return true;
                }
            }

            if (WaitHandle.WaitAny(new[] { _closedEvent, _frameEvent }, 10) == 0)
            {
                return false;
            }
        }
    }

    private bool TryEmitVideoSampleLocked(
        TimeSpan elapsed,
        TimeSpan targetInterval,
        bool requireSchedule,
        out Direct3D11CaptureFrame? frame,
        out TimeSpan timestamp,
        out TimeSpan duration)
    {
        frame = null;
        timestamp = default;
        duration = targetInterval;

        if (_pendingFrame is null || _pendingFrameSequence <= _lastEmittedFrameSequence)
        {
            return false;
        }

        if (requireSchedule && elapsed + TimeSpan.FromMilliseconds(2) < _nextVideoOutputAt)
        {
            return false;
        }

        frame = _pendingFrame;
        _pendingFrame = null;
        _lastEmittedFrameSequence = _pendingFrameSequence;

        timestamp = elapsed;
        duration = _hasLastVideoPts ? timestamp - _lastVideoPts : targetInterval;
        if (duration <= TimeSpan.Zero)
        {
            duration = targetInterval;
        }

        _lastVideoPts = timestamp;
        _hasLastVideoPts = true;
        _nextVideoOutputAt = elapsed + targetInterval;
        return true;
    }

    private void ResetVideoPacingLocked()
    {
        DrainVideoFramesLocked();
        _pendingFrameSequence = 0;
        _lastEmittedFrameSequence = 0;
        _lastVideoPts = TimeSpan.Zero;
        _hasLastVideoPts = false;
        _nextVideoOutputAt = TimeSpan.Zero;
    }

    private void DrainVideoFramesLocked()
    {
        _pendingFrame?.Dispose();
        _pendingFrame = null;
    }

    private static RectInt32 ClampAndMakeEven(RectInt32 region, SizeInt32 itemSize)
    {
        int maxX = Math.Max(0, itemSize.Width - 2);
        int maxY = Math.Max(0, itemSize.Height - 2);

        int x = Math.Clamp(region.X, 0, maxX);
        int y = Math.Clamp(region.Y, 0, maxY);
        int width = RegionBoundsHelper.NormalizeDimension(Math.Clamp(region.Width, RegionBoundsHelper.MinimumEncodeSize, itemSize.Width - x));
        int height = RegionBoundsHelper.NormalizeDimension(Math.Clamp(region.Height, RegionBoundsHelper.MinimumEncodeSize, itemSize.Height - y));

        return new RectInt32
        {
            X = x,
            Y = y,
            Width = width,
            Height = height
        };
    }

    private static RectInt32? GetMonitorBounds(IntPtr hMonitor)
    {
        var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(hMonitor, ref info))
        {
            return null;
        }

        return new RectInt32
        {
            X = info.rcMonitor.Left,
            Y = info.rcMonitor.Top,
            Width = info.rcMonitor.Right - info.rcMonitor.Left,
            Height = info.rcMonitor.Bottom - info.rcMonitor.Top
        };
    }

    private static double GetMonitorDpiScale(IntPtr hMonitor)
    {
        try
        {
            int hr = GetDpiForMonitor(hMonitor, 0 /* MDT_EFFECTIVE_DPI */, out uint dpiX, out _);
            if (hr == 0 && dpiX > 0)
            {
                return dpiX / 96.0;
            }
        }
        catch
        {
            // Fall back to 100% scaling.
        }

        return 1.0;
    }

    private static GraphicsCaptureItem? CreateCaptureItemForMonitor(IntPtr hMonitor)
    {
        const string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
        var classId = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");

        int hr = WindowsCreateString(className, className.Length, out var hString);
        if (hr != 0)
        {
            return null;
        }

        try
        {
            var interopId = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
            hr = RoGetActivationFactory(hString, ref interopId, out var pFactory);
            if (hr != 0)
            {
                return null;
            }

            try
            {
                var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(pFactory);
                hr = interop.CreateForMonitor(hMonitor, ref classId, out var pItem);
                return hr == 0 ? GraphicsCaptureItem.FromAbi(pItem) : null;
            }
            finally
            {
                Marshal.Release(pFactory);
            }
        }
        finally
        {
            WindowsDeleteString(hString);
        }
    }
}
