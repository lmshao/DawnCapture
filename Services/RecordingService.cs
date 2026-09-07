using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DawnCapture.Models;
using DawnCapture.Services.Audio;
using DawnCapture.Views;
using DawnCapture.Helpers;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.Streams;

namespace DawnCapture.Services;

public sealed class RecordingService : IRecordingService
{
    private static readonly Guid IidDirect3DDxgiInterfaceAccess = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
    private static readonly Guid IidD3D11Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

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

            _state = value;
            Log.Debug($"Recording state changed: {_state} -> {value}");
            RaiseStateChanged(value);
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

            AttachRecordingControlWindow(bounds, dpiScale);
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

            AttachRecordingControlWindow(bounds, dpiScale);
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
            AttachRecordingControlWindow(recordedRegion, dpiScale);
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
            AttachAudioRecordingControlWindow();
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
                        : RecordingSettingsHelper.VideoCodecLabel(_settings.Current.VideoCodecIndex),
                    AudioCodec = _audioOptions.HasAnySource ? "AAC" : null,
                    FrameRate = sourceKind == RecordingSourceKind.Audio ? null : _settings.Current.FrameRate,
                    BitrateKbps = sourceKind == RecordingSourceKind.Audio
                        ? _audioOptions.BitrateKbps
                        : _settings.Current.BitrateKbps
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

    private async Task<bool> CreateMediaObjectsAsync(SizeInt32 size, RecordingAudioOptions audioOptions)
    {
        Log.Debug($"Creating media objects: OutputSize={size.Width}x{size.Height}, FrameRate={_settings.Current.FrameRate}, Bitrate={_settings.Current.BitrateKbps}Kbps, AudioMic={audioOptions.EnableMicrophone}, AudioSystem={audioOptions.EnableSystemAudio}, AudioBitrate={audioOptions.BitrateKbps}Kbps");
        try
        {
            var videoProperties = VideoEncodingProperties.CreateUncompressed(
                MediaEncodingSubtypes.Bgra8,
                (uint)size.Width,
                (uint)size.Height);

            var descriptor = new VideoStreamDescriptor(videoProperties);
            AudioStreamDescriptor? audioDescriptor = null;
            if (audioOptions.HasAnySource)
            {
                var pcmProperties = AudioEncodingProperties.CreatePcm(
                    (uint)audioOptions.SampleRate,
                    (uint)audioOptions.Channels,
                    (uint)AudioFormat.BitsPerSample);
                audioDescriptor = new AudioStreamDescriptor(pcmProperties);
            }

            _mediaStreamSource = audioDescriptor is null
                ? new MediaStreamSource(descriptor)
                : new MediaStreamSource(descriptor, audioDescriptor);

            _startingStreamCount = 0;
            _mediaStreamSource.BufferTime = TimeSpan.Zero;
            _mediaStreamSource.Starting += OnMediaStreamSourceStarting;
            _mediaStreamSource.SampleRequested += OnMediaStreamSourceSampleRequested;

            var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
            profile.Video.Width = (uint)size.Width;
            profile.Video.Height = (uint)size.Height;
            profile.Video.Bitrate = (uint)(_settings.Current.BitrateKbps * 1000);
            profile.Video.FrameRate.Numerator = (uint)_settings.Current.FrameRate;
            profile.Video.FrameRate.Denominator = 1;
            profile.Video.PixelAspectRatio.Numerator = 1;
            profile.Video.PixelAspectRatio.Denominator = 1;

            if (_settings.Current.VideoCodecIndex == 1)
            {
                profile.Video.Subtype = MediaEncodingSubtypes.Hevc;
            }

            if (audioOptions.HasAnySource)
            {
                profile.Audio = AudioEncodingProperties.CreateAac(
                    (uint)audioOptions.SampleRate,
                    (uint)audioOptions.Channels,
                    (uint)(audioOptions.BitrateKbps * 1000));
            }

            var folder = OutputFolderHelper.Resolve(_settings.Current.OutputFolder);

            Directory.CreateDirectory(folder);
            var outputPath = Path.Combine(folder, $"DawnCapture_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
            _currentOutputPath = outputPath;

            // StorageFile.GetFileFromPathAsync requires the file to exist.
            File.Create(outputPath).Dispose();
            var outputFile = await StorageFile.GetFileFromPathAsync(outputPath);
            _outputStream = await outputFile.OpenAsync(FileAccessMode.ReadWrite);

            _transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };

            _transcodeTask = Task.Run(async () =>
            {
                var prepared = await _transcoder.PrepareMediaStreamSourceTranscodeAsync(
                    _mediaStreamSource,
                    _outputStream,
                    profile);
                await prepared.TranscodeAsync();
            });

            _ = _transcodeTask.ContinueWith(
                t =>
                {
                    if (t.IsFaulted && t.Exception is not null)
                    {
                        RaiseFailed(t.Exception.GetBaseException().Message);
                    }
                },
                TaskScheduler.Default);

            return true;
        }
        catch (Exception ex)
        {
            RaiseFailed(ex.Message);
            return false;
        }
    }

    private async Task<bool> PrepareAudioOnlyMediaObjectsAsync(RecordingAudioOptions audioOptions)
    {
        Log.Debug(
            $"Creating audio-only media objects: AudioMic={audioOptions.EnableMicrophone}, AudioSystem={audioOptions.EnableSystemAudio}, AudioBitrate={audioOptions.BitrateKbps}Kbps");
        try
        {
            _framesWritten = 0;
            _audioSamplesWritten = 0;
            _isPaused = false;
            _closedEvent.Reset();

            var pcmProperties = AudioEncodingProperties.CreatePcm(
                (uint)audioOptions.SampleRate,
                (uint)audioOptions.Channels,
                (uint)AudioFormat.BitsPerSample);
            var audioDescriptor = new AudioStreamDescriptor(pcmProperties);

            _mediaStreamSource = new MediaStreamSource(audioDescriptor);
            _startingStreamCount = 0;
            _mediaStreamSource.BufferTime = TimeSpan.Zero;
            _mediaStreamSource.Starting += OnMediaStreamSourceStarting;
            _mediaStreamSource.SampleRequested += OnMediaStreamSourceSampleRequested;

            var profile = MediaEncodingProfile.CreateM4a(AudioEncodingQuality.High);
            profile.Audio = AudioEncodingProperties.CreateAac(
                (uint)audioOptions.SampleRate,
                (uint)audioOptions.Channels,
                (uint)(audioOptions.BitrateKbps * 1000));
            _audioEncodingProfile = profile;

            var folder = OutputFolderHelper.Resolve(_settings.Current.OutputFolder);
            Directory.CreateDirectory(folder);
            var outputPath = Path.Combine(folder, $"DawnCapture_{DateTime.Now:yyyyMMdd_HHmmss}.m4a");
            _currentOutputPath = outputPath;

            File.Create(outputPath).Dispose();
            var outputFile = await StorageFile.GetFileFromPathAsync(outputPath);
            _outputStream = await outputFile.OpenAsync(FileAccessMode.ReadWrite);

            return true;
        }
        catch (Exception ex)
        {
            RaiseFailed(ex.Message);
            return false;
        }
    }

    private void StartAudioOnlyTranscode()
    {
        if (_mediaStreamSource is null || _outputStream is null || _audioEncodingProfile is null)
        {
            Log.Error("StartAudioOnlyTranscode called before audio media objects were prepared.");
            RaiseFailed(LocalizationService.GetString("Failure_NoAudioSamples"));
            return;
        }

        _transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };

        _transcodeTask = Task.Run(async () =>
        {
            var prepared = await _transcoder.PrepareMediaStreamSourceTranscodeAsync(
                _mediaStreamSource,
                _outputStream,
                _audioEncodingProfile);
            await prepared.TranscodeAsync();
        });

        _ = _transcodeTask.ContinueWith(
            t =>
            {
                if (t.IsFaulted && t.Exception is not null)
                {
                    RaiseFailed(t.Exception.GetBaseException().Message);
                }
            },
            TaskScheduler.Default);
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

    private void OnMediaStreamSourceStarting(MediaStreamSource sender, MediaStreamSourceStartingEventArgs args)
    {
        _startingStreamCount++;
        args.Request.SetActualStartPosition(TimeSpan.Zero);
    }

    private void OnMediaStreamSourceSampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
    {
        if (!_isRecording && !_isStopping)
        {
            args.Request.Sample = null;
            return;
        }

        if (args.Request.StreamDescriptor is AudioStreamDescriptor)
        {
            args.Request.Sample = _audioPipeline?.TryCreateSample();
            if (args.Request.Sample is not null)
            {
                _audioSamplesWritten++;
            }

            return;
        }

        if (_isPaused && !_isStopping)
        {
            args.Request.Sample = null;
            return;
        }

        try
        {
            if (!WaitForVideoSample(out var frame, out var timestamp, out var duration) || frame is null)
            {
                args.Request.Sample = null;
                return;
            }

            using (frame)
            {
                var surface = frame.Surface;
                var croppedSurface = default(IDirect3DSurface);
                var compositeSurface = default(IDirect3DSurface);

                if (_windowLetterboxActive)
                {
                    compositeSurface = CompositeWindowFrame(surface, frame.ContentSize);
                    if (compositeSurface is null)
                    {
                        args.Request.Sample = null;
                        return;
                    }

                    surface = compositeSurface;
                }
                else if (_cropRect is { } crop)
                {
                    croppedSurface = CropSurface(surface, crop);
                    if (croppedSurface is null)
                    {
                        args.Request.Sample = null;
                        return;
                    }

                    surface = croppedSurface;
                }

                var sample = MediaStreamSample.CreateFromDirect3D11Surface(surface, timestamp);
                sample.Duration = duration;
                args.Request.Sample = sample;
                _framesWritten++;

                if (croppedSurface is not null)
                {
                    // The sample owns a surface reference, so release the extra reference held by this method.
                    croppedSurface.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            RaiseFailed(ex.Message);
            args.Request.Sample = null;
        }
    }

    private IDirect3DSurface? CropSurface(IDirect3DSurface sourceSurface, RectInt32 crop)
    {
        if (_d3dDevice is null || _d3dContext is null)
        {
            return null;
        }

        var pSource = GetTexture2DPointer(sourceSurface);
        if (pSource == IntPtr.Zero)
        {
            return null;
        }

        // The Vortice wrapper owns the reference returned by GetInterface.
        using var source = new ID3D11Texture2D(pSource);

        var description = new Texture2DDescription
        {
            Width = (uint)crop.Width,
            Height = (uint)crop.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None
        };

        using var destination = _d3dDevice.CreateTexture2D(description);
        var box = new Box(crop.X, crop.Y, 0, crop.X + crop.Width, crop.Y + crop.Height, 1);
        _d3dContext.CopySubresourceRegion(destination, 0, 0, 0, 0, source, 0, box);

        using var dxgiSurface = destination.QueryInterface<IDXGISurface>();
        int hr = CreateDirect3D11SurfaceFromDXGISurface(dxgiSurface.NativePointer, out var pWinrtSurface);
        if (hr != 0)
        {
            return null;
        }

        try
        {
            return WinRT.MarshalInterface<IDirect3DSurface>.FromAbi(pWinrtSurface);
        }
        finally
        {
            Marshal.Release(pWinrtSurface);
        }
    }

    private IntPtr GetTexture2DPointer(IDirect3DSurface surface)
    {
        var inspectable = ((WinRT.IWinRTObject)surface).NativeObject.ThisPtr;
        var iidAccess = IidDirect3DDxgiInterfaceAccess;
        int hr = Marshal.QueryInterface(inspectable, ref iidAccess, out var pAccess);
        if (hr != 0)
        {
            return IntPtr.Zero;
        }

        try
        {
            var access = (IDirect3DDxgiInterfaceAccess)Marshal.GetObjectForIUnknown(pAccess);
            var iidTexture = IidD3D11Texture2D;
            hr = access.GetInterface(ref iidTexture, out var pTexture);
            return hr == 0 ? pTexture : IntPtr.Zero;
        }
        finally
        {
            Marshal.Release(pAccess);
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

    private bool EnsureWindowCompositeResources(SizeInt32 encodeSize)
    {
        if (_d3dDevice is null || _d3dContext is null)
        {
            return false;
        }

        ReleaseWindowCompositeResources();

        var description = new Texture2DDescription
        {
            Width = (uint)encodeSize.Width,
            Height = (uint)encodeSize.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None
        };

        _windowCompositeTexture = _d3dDevice.CreateTexture2D(description);
        _windowCompositeRtv = _d3dDevice.CreateRenderTargetView(_windowCompositeTexture);

        using var dxgiSurface = _windowCompositeTexture.QueryInterface<IDXGISurface>();
        int hr = CreateDirect3D11SurfaceFromDXGISurface(dxgiSurface.NativePointer, out var pWinrtSurface);
        if (hr != 0)
        {
            ReleaseWindowCompositeResources();
            return false;
        }

        _windowCompositeSurface = WinRT.MarshalInterface<IDirect3DSurface>.FromAbi(pWinrtSurface);
        Marshal.Release(pWinrtSurface);
        return true;
    }

    private IDirect3DSurface? CompositeWindowFrame(IDirect3DSurface sourceSurface, SizeInt32 contentSize)
    {
        if (_d3dDevice is null ||
            _d3dContext is null ||
            _windowCompositeTexture is null ||
            _windowCompositeRtv is null ||
            _windowCompositeSurface is null)
        {
            return null;
        }

        if (contentSize.Width <= 0 || contentSize.Height <= 0)
        {
            return null;
        }

        var pSource = GetTexture2DPointer(sourceSurface);
        if (pSource == IntPtr.Zero)
        {
            return null;
        }

        using var source = new ID3D11Texture2D(pSource);
        var sourceDesc = source.Description;

        int copyWidth = Math.Min(contentSize.Width, (int)sourceDesc.Width);
        int copyHeight = Math.Min(contentSize.Height, (int)sourceDesc.Height);
        copyWidth = Math.Min(copyWidth, _windowEncodeSize.Width);
        copyHeight = Math.Min(copyHeight, _windowEncodeSize.Height);
        if (copyWidth <= 0 || copyHeight <= 0)
        {
            return null;
        }

        _d3dContext.ClearRenderTargetView(_windowCompositeRtv, new Color4(0f, 0f, 0f, 1f));

        var box = new Box(0, 0, 0, copyWidth, copyHeight, 1);
        _d3dContext.CopySubresourceRegion(
            _windowCompositeTexture,
            0,
            0,
            0,
            0,
            source,
            0,
            box);

        return _windowCompositeSurface;
    }

    private void ReleaseWindowCompositeResources()
    {
        _windowCompositeSurface?.Dispose();
        _windowCompositeSurface = null;
        _windowCompositeRtv?.Dispose();
        _windowCompositeRtv = null;
        _windowCompositeTexture?.Dispose();
        _windowCompositeTexture = null;
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

    private void AttachRecordingControlWindow(RectInt32 bounds, double dpiScale)
    {
        var control = new RecordingControlWindow(() => Elapsed);
        _controlWindow = control;

        control.StopRequested += async () =>
        {
            try
            {
                await StopAsync();
            }
            finally
            {
                control.CloseWindow();
            }
        };

        control.PauseRequested += () =>
        {
            if (State == RecordingState.Recording)
            {
                Pause();
                control.ShowPaused();
            }
            else if (State == RecordingState.Paused)
            {
                Resume();
                control.ShowRecording();
            }
        };

        control.Closed += (_, _) =>
        {
            if (State is RecordingState.Recording or RecordingState.Paused)
            {
                _ = StopAsync();
            }
        };

        control.ShowRecordingTopLeft(bounds, dpiScale);
    }

    private void AttachAudioRecordingControlWindow()
    {
        var control = new RecordingControlWindow(() => Elapsed);
        _controlWindow = control;

        control.StopRequested += async () =>
        {
            try
            {
                await StopAsync();
            }
            finally
            {
                control.CloseWindow();
            }
        };

        control.PauseRequested += () =>
        {
            if (State == RecordingState.Recording)
            {
                Pause();
                control.ShowPaused();
            }
            else if (State == RecordingState.Paused)
            {
                Resume();
                control.ShowRecording();
            }
        };

        control.Closed += (_, _) =>
        {
            if (State is RecordingState.Recording or RecordingState.Paused)
            {
                _ = StopAsync();
            }
        };

        control.ShowRecordingFloating(GetMainWindowDpiScale());
    }

    private static double GetMainWindowDpiScale()
    {
        try
        {
            if (App.MainWindow?.Content is FrameworkElement root)
            {
                return root.XamlRoot?.RasterizationScale ?? 1.0;
            }
        }
        catch
        {
            // Fall back to 100% scaling.
        }

        return 1.0;
    }

    private static void MinimizeMainWindow()
    {
        try
        {
            if (App.MainWindow is null)
            {
                return;
            }

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            ShowWindow(hwnd, 6 /* SW_MINIMIZE */);
        }
        catch
        {
            // Ignore window state failures.
        }
    }

    private static void RestoreMainWindow()
    {
        try
        {
            if (App.MainWindow is null)
            {
                return;
            }

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            ShowWindow(hwnd, 9 /* SW_RESTORE */);
        }
        catch
        {
            // Ignore window state failures.
        }
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(
        IntPtr dxgiDevice,
        out IntPtr graphicsDevice);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11SurfaceFromDXGISurface(
        IntPtr dxgiSurface,
        out IntPtr graphicsSurface);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr lprcClip,
        MonitorEnumProc lpfnEnum,
        IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string sourceString,
        int length,
        out IntPtr hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoGetActivationFactory(
        IntPtr activatableClassId,
        ref Guid iid,
        out IntPtr factory);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint pt, uint dwFlags);

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        [PreserveSig]
        int GetInterface(ref Guid iid, out IntPtr p);
    }

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        [PreserveSig]
        int CreateForWindow(IntPtr window, ref Guid riid, out IntPtr result);

        [PreserveSig]
        int CreateForMonitor(IntPtr monitor, ref Guid riid, out IntPtr result);
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref NativeRect lprcMonitor, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int cbSize;
        public NativeRect rcMonitor;
        public NativeRect rcWork;
        public uint dwFlags;
    }
}
