// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DawnCapture.Helpers;
using DawnCapture.Models;
using DawnCapture.Services.Audio;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Media.Core;

namespace DawnCapture.Services;

public sealed partial class RecordingService
{
    // A failed start must leave State at Idle. The Start*Async entry points refuse to run
    // unless it is, and they return false rather than throw, so their catch blocks never see
    // it: one failed transcode preparation used to disable recording until the app restarted.
    private async Task<bool> StartCaptureAsync(GraphicsCaptureItem item, RectInt32? crop)
    {
        bool started = await StartCaptureCoreAsync(item, crop);
        if (!started)
        {
            State = RecordingState.Idle;
        }

        return started;
    }

    private async Task<bool> StartCaptureCoreAsync(GraphicsCaptureItem item, RectInt32? crop)
    {
        try
        {
            EnsureDevice();

            _frameDuration = 10_000_000L / Math.Max(1, _settings.Current.FrameRate);
            CaptureRecordingLimits();
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
            _encodeSize = captureSize;

            if (_currentSourceKind == RecordingSourceKind.Window)
            {
                _windowLetterboxActive = true;
                _windowEncodeSize = captureSize;
                _poolContentSize = item.Size;
                _windowResizeWarned = false;
                if (!EnsureWindowCompositeResources(_windowEncodeSize))
                {
                    await CleanupCaptureAsync();
                    State = RecordingState.Idle;
                    RaiseFailed(LocalizationService.GetString("Failure_CreateWindowComposite"));
                    return false;
                }
            }
            else
            {
                _windowLetterboxActive = false;
            }

            var pipeline = await BuildVideoPipelineAsync(
                captureSize,
                _audioOptions,
                segmentNumber: 1,
                reuseEffectiveCodec: false);
            if (pipeline is null)
            {
                await CleanupCaptureAsync();
                State = RecordingState.Idle;
                RaiseFailed(LocalizationService.GetString("Failure_TranscodePrepare"));
                return false;
            }

            ActivatePipeline(pipeline, TimeSpan.Zero);

            if (_audioOptions.HasAnySource)
            {
                ActivateAudioPipeline();
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
                    await CleanupCaptureAsync();
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
            await CleanupCaptureAsync();

            // A capture failure is the symptom of a lost device, so let the next attempt
            // build a fresh one instead of failing the same way forever.
            ResetDevice();

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

        Direct3D11Interop.CreateDevice(out _d3dDevice, out _d3dContext, out _winrtDevice);
    }

    /// <summary>
    /// Throws the Direct3D device away so the next recording builds a new one. A device that
    /// has been lost (driver update, GPU switch, remote session) makes every later capture
    /// fail, and because the device is created once and then reused there is no other way
    /// back short of restarting the application.
    /// </summary>
    private void ResetDevice()
    {
        StopGraphicsCaptureSession();
        ReleaseWindowCompositeResources();
        ReleaseReplicaResources();

        try
        {
            _d3dContext?.Dispose();
            _d3dDevice?.Dispose();
            _winrtDevice?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Info($"Releasing the Direct3D device failed: {ex.Message}");
        }

        _d3dContext = null;
        _d3dDevice = null;
        _winrtDevice = null;
        Log.Info("Direct3D device released; the next recording creates a new one.");
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        var frame = sender.TryGetNextFrame();
        if (frame is null)
        {
            return;
        }

        if (!_isRecording || _isPaused || _isStopping)
        {
            frame.Dispose();
            return;
        }

        var arrivedAt = _stopwatch.Elapsed;
        if (_lastFrameArrivedAt != TimeSpan.Zero && arrivedAt - _lastFrameArrivedAt >= FrameGapLogThreshold)
        {
            Log.Info($"Frame delivery resumed after a {(arrivedAt - _lastFrameArrivedAt).TotalSeconds:0.0}s gap.");
        }

        _lastFrameArrivedAt = arrivedAt;

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
        Log.Info($"Capture item closed by the system (recording={_isRecording}, stopping={_isStopping}).");
        if (_isRecording && !_isStopping)
        {
            // The source disappearing is not something the user did, so say so instead of
            // letting the recording end with no explanation.
            RaiseNotice(LocalizationService.GetString("Notice_CaptureSourceClosed"));
            _ = StopAsync();
        }
    }

    /// <summary>
    /// Tears down WGC delivery (session, pool, pending frames) without touching the
    /// transcode pipeline. Safe to call when already stopped.
    /// </summary>
    private void StopGraphicsCaptureSession()
    {
        if (_session is not null)
        {
            try
            {
                _session.Dispose();
            }
            catch
            {
                // Ignore cleanup exceptions.
            }

            _session = null;
        }

        if (_framePool is not null)
        {
            try
            {
                _framePool.FrameArrived -= OnFrameArrived;
                _framePool.Dispose();
            }
            catch
            {
                // Ignore cleanup exceptions.
            }

            _framePool = null;
        }

        if (_item is not null)
        {
            _item.Closed -= OnItemClosed;
            _item = null;
        }

        lock (_frameLock)
        {
            DrainVideoFramesLocked();
        }

        _frameEvent.Reset();
    }

    private bool WaitForVideoSample(
        MediaStreamSource source,
        out Direct3D11CaptureFrame? frame,
        out bool useReplica,
        out TimeSpan timestamp,
        out TimeSpan duration)
    {
        frame = null;
        useReplica = false;
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

            // Never park on a source that has been replaced. Its caller would never get an
            // answer, so the file it is writing could not drain and finish - and, because
            // requests for one source are serialized, every request queued behind this one
            // (including the audio stream) would be stuck with it.
            if (!ReferenceEquals(source, Volatile.Read(ref _mediaStreamSource)))
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
                    if (IsFrameDeliveryStalled(elapsed))
                    {
                        // The screen stopped producing frames (locked session, minimized or
                        // disconnected source). Answer immediately with the retained last
                        // frame so this source's audio stream keeps being served; when there
                        // is nothing to repeat yet, let the caller ask again shortly.
                        if (TryEmitReplicaSampleLocked(elapsed, targetInterval, out timestamp, out duration))
                        {
                            useReplica = true;
                            return true;
                        }

                        // Nothing to repeat yet: no frame has been encoded since the copy
                        // was created, or the copy itself failed. Ask again shortly
                        // instead of spinning.
                        Thread.Sleep(5);
                        return false;
                    }

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

            if (WaitHandle.WaitAny(new WaitHandle[] { _closedEvent, _frameEvent }, 10) == 0)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// True once no frame has arrived for a while - the session is locked, the captured
    /// window is minimized, or the display is disconnected. Frames are not required for the
    /// recording to stay correct, but the pump must keep answering requests. Both clocks
    /// start at zero, so a recording whose first frame is still outstanding measures from
    /// the moment the recording started.
    /// </summary>
    private bool IsFrameDeliveryStalled(TimeSpan elapsed)
    {
        return elapsed - _lastFrameArrivedAt >= FrameStarvationThreshold;
    }

    /// <summary>
    /// Emits the retained last frame as a normal, frame-length sample, so a locked session
    /// records exactly what it looks like: a still picture at the target frame rate, on the
    /// same timeline as every other part of the file.
    /// </summary>
    private bool TryEmitReplicaSampleLocked(
        TimeSpan elapsed,
        TimeSpan targetInterval,
        out TimeSpan timestamp,
        out TimeSpan duration)
    {
        timestamp = default;
        duration = targetInterval;

        if (!_replicaValid || _replicaSurface is null)
        {
            return false;
        }

        timestamp = elapsed - TimeSpan.FromTicks(Volatile.Read(ref _segmentBaseTicks));
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

        // The pacing above runs on the raw clock; only the reported timestamp is rebased,
        // so each segment file starts at zero.
        timestamp = elapsed - TimeSpan.FromTicks(Volatile.Read(ref _segmentBaseTicks));
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
        _lastFrameArrivedAt = TimeSpan.Zero;
        _pendingFrameSequence = 0;
        _lastEmittedFrameSequence = 0;
        _lastVideoPts = TimeSpan.Zero;
        _hasLastVideoPts = false;
        _lastAudioPts = TimeSpan.MinValue;
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
