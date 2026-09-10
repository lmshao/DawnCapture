using System;
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
    private readonly ManualResetEvent _frameEvent = new(false);
    private readonly ManualResetEvent _closedEvent = new(false);

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
    private TimeSpan _nextVideoOutputAt;

    private MediaStreamSource? _mediaStreamSource;
    private MediaTranscoder? _transcoder;
    private IRandomAccessStream? _outputStream;
    private Task? _transcodeTask;
    private MediaEncodingProfile? _audioEncodingProfile;

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
    private string? _currentOutputPath;
    private int _effectiveVideoBitrateKbps;
    private int _effectiveVideoCodecIndex;
    private RecordingSourceKind _currentSourceKind = RecordingSourceKind.Screen;
    private SizeInt32 _windowEncodeSize;
    private SizeInt32 _poolContentSize;
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

            UpdateSleepPrevention(value);

            _state = value;
            Log.Debug($"Recording state changed: {_state} -> {value}");
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
            CleanupCapture();
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
            CleanupCapture();
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
            CleanupCapture();
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

        try
        {
            Log.Debug(
                $"Audio-only recording: Mic={audioOptions.EnableMicrophone}, System={audioOptions.EnableSystemAudio}, Bitrate={audioOptions.BitrateKbps}kbps");

            if (!await PrepareAudioOnlyMediaObjectsAsync(audioOptions))
            {
                State = RecordingState.Idle;
                return false;
            }

            _audioCancellation = new CancellationTokenSource();
            _audioPipeline = new AudioCapturePipeline();

            _stopwatch.Restart();
            _isRecording = true;

            try
            {
                await _audioPipeline.StartAsync(_audioOptions, _stopwatch, _audioCancellation.Token);
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

            StartAudioOnlyTranscode();
            State = RecordingState.Recording;
            AttachRecordingControlWindow(control => control.ShowRecordingFloating(GetMainWindowDpiScale()));
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("StartAudioOnlyAsync failed", ex);
            CleanupCapture();
            State = RecordingState.Idle;
            RaiseFailed(ex.Message);
            return false;
        }
    }

    public async Task StopAsync()
    {
        if (State is not (RecordingState.Recording or RecordingState.Paused))
        {
            return;
        }

        State = RecordingState.Stopping;
        _stopwatch.Stop();
        _isStopping = true;
        _closedEvent.Set();
        _audioPipeline?.BeginFlush();

        if (_transcodeTask is not null)
        {
            try
            {
                await _transcodeTask;
            }
            catch
            {
                // Finalization errors are reported by the task continuation.
            }
        }

        string? completedOutputPath = _currentOutputPath;
        TimeSpan recordedDuration = _stopwatch.Elapsed;
        RecordingSourceKind sourceKind = _currentSourceKind;
        long framesWritten = _framesWritten;
        long audioSamplesWritten = _audioSamplesWritten;

        _isRecording = false;
        _isStopping = false;
        CleanupCapture();
        State = RecordingState.Idle;
        RestoreMainWindow();

        bool isAudioOnly = sourceKind == RecordingSourceKind.Audio;
        if (isAudioOnly)
        {
            Log.Info($"Audio-only recording stopped: {audioSamplesWritten} audio samples written.");
        }
        else
        {
            Log.Info($"Recording stopped: {framesWritten} frames written.");
        }

        bool hasOutput = isAudioOnly ? audioSamplesWritten > 0 : framesWritten > 0;
        if (!hasOutput)
        {
            TryDeleteOutputFile(completedOutputPath);
            RaiseFailed(LocalizationService.GetString(
                isAudioOnly ? "Failure_NoAudioSamples" : "Failure_NoFrames"));
        }
        else
        {
            await TryRegisterRecordingAsync(completedOutputPath, recordedDuration, sourceKind);
        }
    }

    private async Task TryRegisterRecordingAsync(
        string? outputPath,
        TimeSpan duration,
        RecordingSourceKind sourceKind)
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

            if (_settings.Current.NotificationEnabled)
            {
                RecordingNotificationHelper.TryShowSaved(outputPath);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to register recording '{outputPath}'", ex);
        }
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

    private void CleanupCapture()
    {
        _isRecording = false;
        _closedEvent.Set();

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

        if (_outputStream is not null)
        {
            try
            {
                _outputStream.Dispose();
            }
            catch
            {
                // Ignore cleanup exceptions.
            }

            _outputStream = null;
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

        if (_mediaStreamSource is not null)
        {
            _mediaStreamSource.Starting -= OnMediaStreamSourceStarting;
            _mediaStreamSource.SampleRequested -= OnMediaStreamSourceSampleRequested;
            _mediaStreamSource = null;
        }

        _transcodeTask = null;
        _transcoder = null;
        _audioEncodingProfile = null;
        _currentOutputPath = null;

        _cropRect = null;
        ReleaseWindowCompositeResources();
        _windowLetterboxActive = false;
        _windowResizeWarned = false;
        _stopwatch.Reset();
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
