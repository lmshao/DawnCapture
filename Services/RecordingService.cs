// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DawnCapture.Helpers;
using DawnCapture.Models;
using DawnCapture.Services.Audio;
using DawnCapture.Views;
using Microsoft.UI.Dispatching;
using Vortice.Direct3D11;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage.Streams;

namespace DawnCapture.Services;

public sealed partial class RecordingService : IRecordingService
{
    private readonly ISettingsService _settings;
    private readonly IRecordingCatalogService _catalogService;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly Stopwatch _stopwatch = new();
    private readonly object _frameLock = new();
    private readonly AutoResetEvent _frameEvent = new(false);
    private readonly ManualResetEvent _closedEvent = new(false);

    /// <summary>
    /// Upper bound for the transcode finalization awaited by <see cref="StopAsync"/>.
    /// The encoder has to flush both streams and write the container index before the
    /// task completes; if it stalls, the stop path must stay responsive instead of
    /// parking in <see cref="RecordingState.Stopping"/> forever.
    /// </summary>
    private static readonly TimeSpan TranscodeFinalizeTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Upper bound for the second wait performed during teardown, after the transcode
    /// cancellation token has been signalled.
    /// </summary>
    private static readonly TimeSpan TranscodeTeardownTimeout = TimeSpan.FromSeconds(5);

    private ID3D11Device? _d3dDevice;
    private ID3D11DeviceContext? _d3dContext;
    private IDirect3DDevice? _winrtDevice;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFrame? _pendingFrame;
    private long _pendingFrameSequence;
    private long _lastEmittedFrameSequence;
    private TimeSpan _lastVideoPts;
    private bool _hasLastVideoPts;

    /// <summary>
    /// Last audio timestamp handed to the encoder. Used to refuse an out of order sample,
    /// which a muxer would answer by discarding the rest of the track. MinValue means "no
    /// sample yet", which is also the state right after a segment hand-over.
    /// </summary>
    private TimeSpan _lastAudioPts = TimeSpan.MinValue;
    private TimeSpan _nextVideoOutputAt;
    private TimeSpan _lastFrameArrivedAt;
    private static readonly TimeSpan FrameGapLogThreshold = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long the screen may deliver no frames before the pump switches to repeating the
    /// last known picture. Well above any sensible frame interval, so normal recording never
    /// takes this path.
    /// </summary>
    private static readonly TimeSpan FrameStarvationThreshold = TimeSpan.FromMilliseconds(500);

    private MediaStreamSource? _mediaStreamSource;
    private MediaTranscoder? _transcoder;
    private IRandomAccessStream? _outputStream;
    private Task? _transcodeTask;
    private CancellationTokenSource? _transcodeCancellation;

    private long _frameDuration = 333_333;
    private long _framesWritten;
    private long _audioSamplesWritten;
    private int _startingStreamCount;
    private RectInt32? _cropRect;
    private RegionMarkerWindow? _regionMarker;
    private RecordingControlWindow? _controlWindow;
    private IAudioCapturePipeline? _audioPipeline;
    private RecordingAudioOptions _audioOptions = new();
    private CancellationTokenSource? _audioCancellation;
    private bool _isRecording;
    private bool _isPaused;
    private bool _isStopping;
    private int _stopGuard;
    private string? _currentOutputPath;
    private int _effectiveVideoBitrateKbps;
    private int _effectiveVideoCodecIndex;
    private RecordingSourceKind _currentSourceKind = RecordingSourceKind.Screen;
    private SizeInt32 _windowEncodeSize;
    private SizeInt32 _poolContentSize;
    private SizeInt32 _encodeSize;
    private bool _windowLetterboxActive;
    private bool _windowResizeWarned;
    private ID3D11Texture2D? _windowCompositeTexture;
    private ID3D11RenderTargetView? _windowCompositeRtv;
    private IDirect3DSurface? _windowCompositeSurface;

    private RecordingState _state = RecordingState.Idle;

    public RecordingService(ISettingsService settings, IRecordingCatalogService catalogService)
    {
        _settings = settings;
        _catalogService = catalogService;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    public RecordingState State
    {
        get => _state;
        private set
        {
            if (_state == value)
            {
                return;
            }

            RecordingState previous = _state;
            UpdateSleepPrevention(value);

            _state = value;
            Log.Debug($"Recording state changed: {previous} -> {value}");

            // Record that a file is being written, so a forced kill, a log off or a power
            // loss can be explained on the next launch instead of leaving a file that simply
            // will not open. Resuming from pause is not a new recording.
            if (value == RecordingState.Recording &&
                previous is not (RecordingState.Recording or RecordingState.Paused) &&
                _currentOutputPath is { Length: > 0 } outputPath)
            {
                CrashMarkerHelper.WriteRecording(outputPath);
            }

            RaiseStateChanged(value);
        }
    }

    private static void UpdateSleepPrevention(RecordingState next)
    {
        if (next == RecordingState.Recording)
        {
            PowerStateHelper.PreventSleepDuringRecording();
        }
        else if (next is not (RecordingState.Recording or RecordingState.Paused))
        {
            PowerStateHelper.AllowSleep();
        }
    }

    public TimeSpan Elapsed => _stopwatch.Elapsed;

    public double AudioMeterLevel => _audioPipeline?.PeakLevel ?? 0;

    public bool IsAudioClipping => _audioPipeline?.IsClipping ?? false;

    public event EventHandler<RecordingState>? StateChanged;

    public event EventHandler<string>? RecordingFailed;

    public event EventHandler<string>? RecordingNotice;

    public async Task<bool> StartWindowAsync(GraphicsCaptureItem item, RecordingAudioOptions audioOptions)
    {
        if (State != RecordingState.Idle)
        {
            return false;
        }

        _audioOptions = audioOptions;
        State = RecordingState.PickingSource;

        try
        {
            var bounds = WindowCaptureHelper.GetWindowBounds(item);
            var dpiScale = WindowCaptureHelper.GetWindowDpiScale(item);
            Log.Debug($"Window recording: DisplayName={item.DisplayName}, Bounds={bounds.Width}x{bounds.Height} @({bounds.X},{bounds.Y})");
            _currentSourceKind = RecordingSourceKind.Window;

            if (!await StartCaptureAsync(item, null))
            {
                return false;
            }

            AttachRecordingControlWindow(control => control.ShowRecordingTopLeft(bounds, dpiScale));
            MinimizeMainWindow();
            return true;
        }
        catch (Exception ex)
        {
            await CleanupCaptureAsync();
            State = RecordingState.Idle;
            RaiseFailed(ex.Message);
            return false;
        }
    }

    public async Task<bool> StartFullScreenAsync(MonitorDisplay display, RecordingAudioOptions audioOptions)
    {
        if (State != RecordingState.Idle)
        {
            return false;
        }

        _audioOptions = audioOptions;

        if (display.Handle == IntPtr.Zero)
        {
            RaiseFailed(LocalizationService.GetString("Failure_CreateFullScreenCapture"));
            return false;
        }

        State = RecordingState.PickingSource;
        try
        {
            var monitor = display.Handle;
            var bounds = new Windows.Graphics.RectInt32
            {
                X = display.X,
                Y = display.Y,
                Width = display.Width,
                Height = display.Height
            };
            var dpiScale = GetMonitorDpiScale(monitor);
            Log.Debug($"Full-screen recording: Monitor=0x{monitor:X}, Bounds={bounds.Width}x{bounds.Height} @({bounds.X},{bounds.Y})");
            _currentSourceKind = RecordingSourceKind.Screen;
            var item = CreateCaptureItemForMonitor(monitor);
            if (item is null)
            {
                RaiseFailed(LocalizationService.GetString("Failure_CreateFullScreenCapture"));
                State = RecordingState.Idle;
                return false;
            }

            if (!await StartCaptureAsync(item, null))
            {
                return false;
            }

            AttachRecordingControlWindow(control => control.ShowRecordingTopLeft(bounds, dpiScale));
            Log.Debug($"Full-screen control shown at ({bounds.X + 12},{bounds.Y + 12}).");
            MinimizeMainWindow();
            return true;
        }
        catch (Exception ex)
        {
            await CleanupCaptureAsync();
            State = RecordingState.Idle;
            RaiseFailed(ex.Message);
            return false;
        }
    }

    public async Task<bool> StartRegionAsync(RectInt32 screenRegion, RecordingAudioOptions audioOptions)
    {
        if (State != RecordingState.Idle)
        {
            return false;
        }

        if (screenRegion.Width <= 0 || screenRegion.Height <= 0)
        {
            RaiseFailed(LocalizationService.GetString("Failure_CreateRegionCapture"));
            return false;
        }

        _audioOptions = audioOptions;
        State = RecordingState.PickingSource;

        try
        {
            Log.Debug($"Region recording: {screenRegion.Width}x{screenRegion.Height} @({screenRegion.X},{screenRegion.Y})");

            var point = new NativePoint
            {
                X = screenRegion.X + screenRegion.Width / 2,
                Y = screenRegion.Y + screenRegion.Height / 2
            };
            var monitor = MonitorFromPoint(point, 2 /* MONITOR_DEFAULTTONEAREST */);
            var monitorBounds = GetMonitorBounds(monitor);
            var dpiScale = GetMonitorDpiScale(monitor);
            var item = CreateCaptureItemForMonitor(monitor);
            if (item is null || monitorBounds is null)
            {
                RaiseFailed(LocalizationService.GetString("Failure_CreateRegionCapture"));
                State = RecordingState.Idle;
                return false;
            }

            var crop = new RectInt32
            {
                X = screenRegion.X - monitorBounds.Value.X,
                Y = screenRegion.Y - monitorBounds.Value.Y,
                Width = screenRegion.Width,
                Height = screenRegion.Height
            };
            crop = ClampAndMakeEven(crop, item.Size);
            Log.Debug($"Crop region: {crop.Width}x{crop.Height} @({crop.X},{crop.Y}), CaptureSize={item.Size.Width}x{item.Size.Height}");
            _currentSourceKind = RecordingSourceKind.Region;

            if (!await StartCaptureAsync(item, crop))
            {
                return false;
            }

            var recordedRegion = new RectInt32
            {
                X = monitorBounds.Value.X + crop.X,
                Y = monitorBounds.Value.Y + crop.Y,
                Width = crop.Width,
                Height = crop.Height
            };
            _regionMarker = new RegionMarkerWindow(recordedRegion);

            // Recording starts immediately (industry convention). The dock is
            // placed above the region so it never covers the recorded content.
            AttachRecordingControlWindow(control => control.ShowRecordingNear(recordedRegion, dpiScale));
            MinimizeMainWindow();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("StartRegionAsync failed", ex);
            await CleanupCaptureAsync();
            State = RecordingState.Idle;
            RaiseFailed(ex.Message);
            return false;
        }
    }

    public async Task<bool> StartAudioOnlyAsync(RecordingAudioOptions audioOptions)
    {
        if (State != RecordingState.Idle)
        {
            return false;
        }

        if (!audioOptions.HasAnySource)
        {
            RaiseFailed(LocalizationService.GetString("AppStatus_EnableOneAudio"));
            return false;
        }

        _audioOptions = audioOptions;
        _currentSourceKind = RecordingSourceKind.Audio;
        CaptureRecordingLimits();

        try
        {
            Log.Debug(
                $"Audio-only recording: Mic={audioOptions.EnableMicrophone}, System={audioOptions.EnableSystemAudio}, Bitrate={audioOptions.BitrateKbps}kbps");

            var pipeline = await BuildAudioPipelineAsync(audioOptions, segmentNumber: 1);
            if (pipeline is null)
            {
                RaiseFailed(LocalizationService.GetString("Failure_TranscodePrepare"));
                State = RecordingState.Idle;
                return false;
            }

            // Hand it to the service before the audio pipeline starts, so a failure on
            // the way still releases the file.
            _activePipeline = pipeline;

            AudioCapturePipeline audioPipeline = ActivateAudioPipeline();

            _stopwatch.Restart();
            _isRecording = true;

            try
            {
                await audioPipeline.StartAsync(_audioOptions, _stopwatch, _audioCancellation!.Token);
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

            ActivatePipeline(pipeline, TimeSpan.Zero);
            State = RecordingState.Recording;
            AttachRecordingControlWindow(control => control.ShowRecordingFloating(GetMainWindowDpiScale()));
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("StartAudioOnlyAsync failed", ex);
            await CleanupCaptureAsync();
            State = RecordingState.Idle;
            RaiseFailed(ex.Message);
            return false;
        }
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopGuard, 1) != 0)
        {
            return;
        }

        bool gateHeld = false;

        try
        {
            if (State is not (RecordingState.Recording or RecordingState.Paused))
            {
                return;
            }

            // A segment switch moves the media objects and the output path, so the
            // snapshot below has to wait for one that is still in flight.
            await _pipelineGate.WaitAsync();
            gateHeld = true;

            bool transcodeTimedOut = false;

            State = RecordingState.Stopping;
            _stopwatch.Stop();

            if (_lastFrameArrivedAt != TimeSpan.Zero)
            {
                var frameAge = _stopwatch.Elapsed - _lastFrameArrivedAt;
                if (frameAge >= FrameGapLogThreshold)
                {
                    Log.Info($"Frames had already stopped {frameAge.TotalSeconds:0.0}s before the recording was stopped.");
                }
            }

            _isStopping = true;

            // Stop feeding the encoder before waiting for finalize (P0-5): WGC must
            // not enqueue frames while MediaTranscoder drains audio/video to EOS.
            _isRecording = false;
            StopGraphicsCaptureSession();
            _audioPipeline?.BeginFlush();
            _closedEvent.Set();

            if (_transcodeTask is not null)
            {
                try
                {
                    await _transcodeTask.WaitAsync(TranscodeFinalizeTimeout);
                }
                catch (TimeoutException)
                {
                    transcodeTimedOut = true;
                    _transcodeCancellation?.Cancel();
                }
                catch
                {
                    // Finalization errors are reported by the task continuation.
                }
            }

            // Files retired by earlier segment switches are finalized by their own
            // workers; the recording is only complete once none of them is still writing.
            await WaitForRetirementsAsync();

            string? completedOutputPath = _currentOutputPath;
            TimeSpan recordedDuration = SegmentElapsed;
            RecordingSourceKind sourceKind = _currentSourceKind;
            long framesWritten = _framesWritten;
            long audioSamplesWritten = _audioSamplesWritten;
            long segmentFrames = _framesWritten - _segmentStartFrames;
            long segmentSamples = _audioSamplesWritten - _segmentStartAudioSamples;

            _isStopping = false;
            await CleanupCaptureAsync();
            State = RecordingState.Idle;
            RestoreMainWindow();

            bool isAudioOnly = sourceKind == RecordingSourceKind.Audio;
            bool hasOutput = isAudioOnly ? segmentSamples > 0 : segmentFrames > 0;
            if (transcodeTimedOut)
            {
                Log.Error(
                    $"Recording aborted: transcode finalize timed out after {framesWritten} video frames / {audioSamplesWritten} audio samples.");
                TryDeleteOutputFile(completedOutputPath);
                RaiseFailed(LocalizationService.GetString("Failure_TranscodeTimeout"));
            }
            else if (!hasOutput)
            {
                TryDeleteOutputFile(completedOutputPath);
                if (_segmentIndex > 1)
                {
                    // The segment was opened but never received a sample. The earlier
                    // segments are already registered, so this is not a failure.
                    Log.Info($"Trailing empty segment {_segmentIndex} discarded.");
                }
                else
                {
                    RaiseFailed(LocalizationService.GetString(
                        isAudioOnly ? "Failure_NoAudioSamples" : "Failure_NoFrames"));
                }
            }
            else
            {
                if (isAudioOnly)
                {
                    Log.Info($"Audio-only recording stopped: {audioSamplesWritten} audio samples written.");
                }
                else
                {
                    Log.Info($"Recording stopped: {framesWritten} frames written.");
                }

                await TryRegisterRecordingAsync(completedOutputPath, recordedDuration, sourceKind);
            }
        }
        finally
        {
            if (gateHeld)
            {
                _pipelineGate.Release();
            }

            Interlocked.Exchange(ref _stopGuard, 0);
        }
    }

    private async Task TryRegisterRecordingAsync(
        string? outputPath,
        TimeSpan duration,
        RecordingSourceKind sourceKind,
        bool notify = true)
    {
        if (string.IsNullOrWhiteSpace(outputPath) || !File.Exists(outputPath))
        {
            return;
        }

        try
        {
            var fileInfo = new FileInfo(outputPath);
            DateTime recordedUtc = fileInfo.LastWriteTimeUtc > fileInfo.CreationTimeUtc
                ? fileInfo.LastWriteTimeUtc
                : fileInfo.CreationTimeUtc;

            await _catalogService.RegisterRecordingAsync(
                outputPath,
                new DateTimeOffset(recordedUtc),
                duration,
                sourceKind == RecordingSourceKind.Audio ? RecordingMediaKind.Audio : RecordingMediaKind.Video,
                sourceKind,
                new RecordingEncodingInfo
                {
                    VideoCodec = sourceKind == RecordingSourceKind.Audio
                        ? null
                        : RecordingSettingsHelper.VideoCodecLabel(_effectiveVideoCodecIndex),
                    AudioCodec = _audioOptions.HasAnySource ? "AAC" : null,
                    FrameRate = sourceKind == RecordingSourceKind.Audio ? null : _settings.Current.FrameRate,
                    BitrateKbps = sourceKind == RecordingSourceKind.Audio
                        ? _audioOptions.BitrateKbps
                        : _effectiveVideoBitrateKbps > 0
                            ? _effectiveVideoBitrateKbps
                            : _settings.Current.BitrateKbps,
                    AudioBitrateKbps = sourceKind == RecordingSourceKind.Audio
                        ? _audioOptions.BitrateKbps
                        : _audioOptions.HasAnySource ? _audioOptions.BitrateKbps : null
                });

            if (notify && _settings.Current.NotificationEnabled)
            {
                RecordingNotificationHelper.TryShowSaved(outputPath);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to register recording '{outputPath}'", ex);
        }
    }

    /// <summary>
    /// Finishes the files that are being written without touching anything that needs the UI
    /// thread or the database, so it can run inside a session end notification. Segments that
    /// are still being finalized are included: they are separate files that would otherwise
    /// be left without their index. Returns whether everything finished within the budget.
    /// </summary>
    public bool EmergencyFinalize(TimeSpan budget)
    {
        if (State is not (RecordingState.Recording or RecordingState.Paused))
        {
            return true;
        }

        // A stop that is already running owns the teardown; this call then only waits for it.
        bool ownsStop = Interlocked.Exchange(ref _stopGuard, 1) == 0;
        bool finalized = false;

        try
        {
            Log.Info($"Emergency finalize started (budget {budget.TotalSeconds:0.0}s).");

            if (ownsStop)
            {
                // Same sequence as StopAsync, minus every step that needs the UI thread.
                _stopwatch.Stop();
                _isStopping = true;
                _isRecording = false;
                StopGraphicsCaptureSession();
                _audioPipeline?.BeginFlush();
                _closedEvent.Set();
            }

            var pending = new List<Task>();
            if (_transcodeTask is not null)
            {
                pending.Add(_transcodeTask);
            }

            pending.AddRange(SnapshotRetirements());
            finalized = pending.Count == 0 || Task.WhenAll(pending).Wait(budget);
        }
        catch (Exception ex)
        {
            Log.Error("Emergency finalize failed", ex);
        }
        finally
        {
            if (ownsStop)
            {
                Interlocked.Exchange(ref _stopGuard, 0);
            }
        }

        if (!finalized)
        {
            // The marker stays behind on purpose: the next launch tells the user this file
            // may not play, which is better than a file that silently will not open.
            Log.Error("Emergency finalize timed out; the file may not be playable.");
            return false;
        }

        CrashMarkerHelper.ClearRecording();
        if (ownsStop)
        {
            State = RecordingState.Idle;
        }

        Log.Info("Emergency finalize completed.");
        return true;
    }

    public void Pause()
    {
        if (State != RecordingState.Recording)
        {
            return;
        }

        _isPaused = true;
        _stopwatch.Stop();
        _audioPipeline?.SetPaused(true);
        State = RecordingState.Paused;
    }

    public void Resume()
    {
        if (State != RecordingState.Paused)
        {
            return;
        }

        _isPaused = false;
        _stopwatch.Start();
        _audioPipeline?.SetPaused(false);
        lock (_frameLock)
        {
            _nextVideoOutputAt = _stopwatch.Elapsed;
            _pendingFrame?.Dispose();
            _pendingFrame = null;
        }

        State = RecordingState.Recording;
    }

    private async Task CleanupCaptureAsync()
    {
        _isRecording = false;
        _closedEvent.Set();

        // The running transcode task still reads _mediaStreamSource and writes
        // _outputStream, so it has to be cancelled and awaited before either object
        // is disposed. A plain field reset here used to leave the task racing with
        // the releases below (for example when a start path fails after the transcode
        // has already been kicked off).
        var transcodeTask = _transcodeTask;
        if (transcodeTask is not null && !transcodeTask.IsCompleted)
        {
            _transcodeCancellation?.Cancel();
            try
            {
                await transcodeTask.WaitAsync(TranscodeTeardownTimeout);
            }
            catch (TimeoutException)
            {
                Log.Error(
                    $"Transcode task still running {TranscodeTeardownTimeout.TotalSeconds:0}s after cancellation; releasing its resources anyway.");
            }
            catch
            {
                // Finalization errors are reported by the task continuation.
            }
        }

        ReleaseMediaObjects();

        StopGraphicsCaptureSession();

        if (_regionMarker is not null)
        {
            try
            {
                _regionMarker.Close();
            }
            catch
            {
                // Ignore cleanup exceptions.
            }

            _regionMarker = null;
        }

        if (_controlWindow is not null)
        {
            try
            {
                _controlWindow.CloseWindow();
            }
            catch
            {
                // Ignore cleanup exceptions.
            }

            _controlWindow = null;
        }

        _audioCancellation?.Cancel();
        _audioCancellation?.Dispose();
        _audioCancellation = null;

        if (_audioPipeline is not null)
        {
            try
            {
                _audioPipeline.Stop();
                _audioPipeline.Dispose();
            }
            catch
            {
                // Ignore cleanup exceptions.
            }

            _audioPipeline = null;
        }



        _cropRect = null;
        ReleaseWindowCompositeResources();
        ReleaseReplicaResources();

        // The recording ended with the process alive, so it is no longer "unfinished".
        // Reaching this method at all means a crash marker would be the more useful one.
        CrashMarkerHelper.ClearRecording();

        _windowLetterboxActive = false;
        _windowResizeWarned = false;
        _stopwatch.Reset();
    }

    private bool _audioClippingNoticed;

    /// <summary>
    /// Creates the pipeline and wires the one-time clipping notice, so an overloaded mix tells the
    /// user while it is still happening instead of only showing up in the log. The notice itself
    /// arrives on the mixer thread and RaiseNotice marshals it to the UI.
    /// </summary>
    private AudioCapturePipeline ActivateAudioPipeline()
    {
        _audioCancellation = new CancellationTokenSource();
        var pipeline = new AudioCapturePipeline();
        pipeline.ClippingStarted += OnAudioClippingStarted;
        _audioClippingNoticed = false;
        _audioPipeline = pipeline;
        return pipeline;
    }

    private void OnAudioClippingStarted()
    {
        if (_audioClippingNoticed)
        {
            return;
        }

        _audioClippingNoticed = true;
        RaiseNotice(LocalizationService.GetString("Notice_AudioClipping"));
    }

    private void RaiseAudioStartupWarnings()
    {
        if (_audioPipeline is AudioCapturePipeline pipeline)
        {
            pipeline.ReportStartupWarnings(_audioOptions, RaiseNotice);
        }
    }

    private void RaiseStateChanged(RecordingState value)
    {
        var handler = StateChanged;
        if (handler is null)
        {
            return;
        }

        if (_dispatcherQueue is not null)
        {
            _dispatcherQueue.TryEnqueue(() => handler(this, value));
        }
        else
        {
            handler(this, value);
        }
    }

    private void RaiseFailed(string message)
    {
        Log.Error($"Recording failed: {message}");
        var handler = RecordingFailed;
        if (handler is null)
        {
            return;
        }

        if (_dispatcherQueue is not null)
        {
            _dispatcherQueue.TryEnqueue(() => handler(this, message));
        }
        else
        {
            handler(this, message);
        }
    }

    private void RaiseNotice(string message)
    {
        Log.Info($"Recording notice: {message}");
        var handler = RecordingNotice;
        if (handler is null)
        {
            return;
        }

        if (_dispatcherQueue is not null)
        {
            _dispatcherQueue.TryEnqueue(() => handler(this, message));
        }
        else
        {
            handler(this, message);
        }
    }
}
