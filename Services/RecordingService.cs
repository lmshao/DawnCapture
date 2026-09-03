using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DawnCapture.Models;
using DawnCapture.Views;
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
    private Direct3D11CaptureFrame? _currentFrame;

    private MediaStreamSource? _mediaStreamSource;
    private MediaTranscoder? _transcoder;
    private IRandomAccessStream? _outputStream;
    private Task? _transcodeTask;

    private long _frameDuration = 333_333;
    private long _framesWritten;
    private long _pauseTimestamp;
    private IDirect3DSurface? _pauseSurface;
    private RectInt32? _cropRect;
    private RegionMarkerWindow? _regionMarker;
    private RecordingControlWindow? _controlWindow;
    private bool _isRecording;
    private bool _isPaused;

    private RecordingState _state = RecordingState.Idle;

    public RecordingService(ISettingsService settings)
    {
        _settings = settings;
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

    public event EventHandler<RecordingState>? StateChanged;

    public event EventHandler<string>? RecordingFailed;

    public async Task<bool> PickAndStartWindowAsync()
    {
        if (State != RecordingState.Idle)
        {
            return false;
        }

        if (App.MainWindow is null)
        {
            RaiseFailed(LocalizationService.GetString("Failure_MainWindowUnavailable"));
            return false;
        }

        State = RecordingState.PickingSource;
        try
        {
            var picker = new GraphicsCapturePicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var item = await picker.PickSingleItemAsync();
            if (item is null)
            {
                State = RecordingState.Idle;
                return false;
            }

            Log.Debug($"Window recording: DisplayName={item.DisplayName}, Size={item.Size.Width}x{item.Size.Height}");
            return await StartCaptureAsync(item, null);
        }
        catch (Exception ex)
        {
            CleanupCapture();
            State = RecordingState.Idle;
            RaiseFailed(ex.Message);
            return false;
        }
    }

    public async Task<bool> StartFullScreenAsync(MonitorDisplay display)
    {
        if (State != RecordingState.Idle)
        {
            return false;
        }

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

    public async Task<bool> StartRegionAsync()
    {
        if (State != RecordingState.Idle)
        {
            return false;
        }

        State = RecordingState.PickingSource;
        try
        {
            var virtualBounds = GetVirtualScreenBounds();
            Log.Debug($"Region recording: VirtualScreen={virtualBounds.Width}x{virtualBounds.Height} @({virtualBounds.X},{virtualBounds.Y})");
            var regionWindow = new RegionPickerWindow(virtualBounds);
            var region = await regionWindow.PickAsync();
            if (region is null)
            {
                Log.Debug("Region selection canceled.");
                regionWindow.Close();
                State = RecordingState.Idle;
                return false;
            }

            Log.Debug($"Region selected: {region.Value.Width}x{region.Value.Height} @({region.Value.X},{region.Value.Y})");

            var point = new NativePoint
            {
                X = region.Value.X + region.Value.Width / 2,
                Y = region.Value.Y + region.Value.Height / 2
            };
            var monitor = MonitorFromPoint(point, 2 /* MONITOR_DEFAULTTONEAREST */);
            var monitorBounds = GetMonitorBounds(monitor);
            var dpiScale = GetMonitorDpiScale(monitor);
            var item = CreateCaptureItemForMonitor(monitor);
            if (item is null || monitorBounds is null)
            {
                regionWindow.Close();
                RaiseFailed(LocalizationService.GetString("Failure_CreateRegionCapture"));
                State = RecordingState.Idle;
                return false;
            }

            var crop = new RectInt32
            {
                X = region.Value.X - monitorBounds.Value.X,
                Y = region.Value.Y - monitorBounds.Value.Y,
                Width = region.Value.Width,
                Height = region.Value.Height
            };
            crop = ClampAndMakeEven(crop, item.Size);
            Log.Debug($"Crop region: {crop.Width}x{crop.Height} @({crop.X},{crop.Y}), CaptureSize={item.Size.Width}x{item.Size.Height}");

            _regionMarker = new RegionMarkerWindow(region.Value);

            // Show the REC control above the selection and start recording when it is clicked.
            var control = new RecordingControlWindow(() => Elapsed);
            _controlWindow = control;

            var started = false;
            var tcs = new TaskCompletionSource<bool>();

            control.StartRequested += async () =>
            {
                try
                {
                    started = await StartCaptureAsync(item, crop);
                    if (started)
                    {
                        control.ShowRecording();
                    }
                    else
                    {
                        control.CloseWindow();
                    }
                }
                catch
                {
                    control.CloseWindow();
                }
            };

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

            control.CancelRequested += () =>
            {
                if (!started)
                {
                    control.CloseWindow();
                }
            };

            control.Closed += (_, _) =>
            {
                if (!started)
                {
                    _regionMarker?.Close();
                    _regionMarker = null;
                    if (State == RecordingState.PickingSource)
                    {
                        State = RecordingState.Idle;
                    }
                }

                tcs.TrySetResult(started);
            };

            control.ShowWaiting(region.Value, dpiScale);
            return await tcs.Task;
        }
        catch (Exception ex)
        {
            Log.Error("StartCaptureAsync failed", ex);
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
        _isRecording = false;
        _closedEvent.Set();

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

        CleanupCapture();
        State = RecordingState.Idle;
        RestoreMainWindow();

        Log.Info($"Recording stopped: {_framesWritten} frames written.");
        if (_framesWritten == 0)
        {
            RaiseFailed(LocalizationService.GetString("Failure_NoFrames"));
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
        State = RecordingState.Paused;
    }

    public void Resume()
    {
        if (State != RecordingState.Paused)
        {
            return;
        }

        _isPaused = false;
        _pauseSurface?.Dispose();
        _pauseSurface = null;
        _stopwatch.Start();
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

            _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _winrtDevice!,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                item.Size);

            _session = _framePool.CreateCaptureSession(item);
            _session.IsCursorCaptureEnabled = _settings.Current.CaptureCursor;
            _framePool.FrameArrived += OnFrameArrived;

            _item = item;
            _item.Closed += OnItemClosed;

            _session.StartCapture();

            var captureSize = crop is { } c
                ? new SizeInt32 { Width = c.Width, Height = c.Height }
                : item.Size;

            if (!await CreateMediaObjectsAsync(captureSize))
            {
                CleanupCapture();
                return false;
            }

            _isRecording = true;
            _stopwatch.Restart();
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

    private async Task<bool> CreateMediaObjectsAsync(SizeInt32 size)
    {
        Log.Debug($"Creating media objects: OutputSize={size.Width}x{size.Height}, FrameRate={_settings.Current.FrameRate}, Bitrate={_settings.Current.BitrateKbps}Kbps");
        try
        {
            var videoProperties = VideoEncodingProperties.CreateUncompressed(
                MediaEncodingSubtypes.Bgra8,
                (uint)size.Width,
                (uint)size.Height);

            var descriptor = new VideoStreamDescriptor(videoProperties);
            _mediaStreamSource = new MediaStreamSource(descriptor)
            {
                BufferTime = TimeSpan.Zero
            };
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

            var folder = string.IsNullOrWhiteSpace(_settings.Current.OutputFolder)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "DawnCapture")
                : _settings.Current.OutputFolder;

            Directory.CreateDirectory(folder);
            var outputPath = Path.Combine(folder, $"DawnCapture_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");

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

        lock (_frameLock)
        {
            _currentFrame?.Dispose();
            _currentFrame = frame;
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
        using var frame = WaitForNewFrame();
        if (frame is not null)
        {
            args.Request.SetActualStartPosition(frame.SystemRelativeTime);
        }
        else
        {
            args.Request.SetActualStartPosition(TimeSpan.Zero);
        }
    }

    private void OnMediaStreamSourceSampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
    {
        if (!_isRecording)
        {
            args.Request.Sample = null;
            return;
        }

        try
        {
            if (_isPaused && _pauseSurface is not null)
            {
                _pauseTimestamp += _frameDuration;
                args.Request.Sample = MediaStreamSample.CreateFromDirect3D11Surface(
                    _pauseSurface,
                    TimeSpan.FromTicks(_pauseTimestamp));
                _framesWritten++;
                return;
            }

            using var frame = WaitForNewFrame();
            if (frame is null)
            {
                args.Request.Sample = null;
                return;
            }

            var surface = frame.Surface;
            var croppedSurface = default(IDirect3DSurface);

            if (_cropRect is { } crop)
            {
                croppedSurface = CropSurface(surface, crop);
                if (croppedSurface is null)
                {
                    args.Request.Sample = null;
                    return;
                }

                surface = croppedSurface;
            }

            var timestamp = frame.SystemRelativeTime;

            if (_isPaused)
            {
                // Preserve the first frame after pausing for frozen frames to keep the timeline continuous.
                _pauseSurface?.Dispose();
                _pauseSurface = surface;
                _pauseTimestamp = timestamp.Ticks;
            }

            args.Request.Sample = MediaStreamSample.CreateFromDirect3D11Surface(surface, timestamp);
            _framesWritten++;

            if (!_isPaused && croppedSurface is not null)
            {
                // The sample owns a surface reference, so release the extra reference held by this method.
                croppedSurface.Dispose();
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

    private Direct3D11CaptureFrame? WaitForNewFrame()
    {
        _frameEvent.Reset();
        var handles = new[] { _closedEvent, _frameEvent };
        int signaled = WaitHandle.WaitAny(handles);
        if (signaled != 1)
        {
            return null;
        }

        lock (_frameLock)
        {
            var frame = _currentFrame;
            _currentFrame = null;
            return frame;
        }
    }

    private static RectInt32 ClampAndMakeEven(RectInt32 region, SizeInt32 itemSize)
    {
        int maxX = Math.Max(0, itemSize.Width - 2);
        int maxY = Math.Max(0, itemSize.Height - 2);

        int x = Math.Clamp(region.X, 0, maxX);
        int y = Math.Clamp(region.Y, 0, maxY);
        int width = Math.Clamp(region.Width, 2, itemSize.Width - x);
        int height = Math.Clamp(region.Height, 2, itemSize.Height - y);

        // H.264 encoders generally require even dimensions.
        width &= ~1;
        height &= ~1;
        width = Math.Max(2, width);
        height = Math.Max(2, height);

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

    private static RectInt32 GetVirtualScreenBounds()
    {
        var monitors = new List<RectInt32>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr hMonitor, IntPtr hdcMonitor, ref NativeRect rcMonitor, IntPtr data)
        {
            monitors.Add(new RectInt32
            {
                X = rcMonitor.Left,
                Y = rcMonitor.Top,
                Width = rcMonitor.Right - rcMonitor.Left,
                Height = rcMonitor.Bottom - rcMonitor.Top
            });
            return true;
        }, IntPtr.Zero);

        if (monitors.Count == 0)
        {
            return new RectInt32 { X = 0, Y = 0, Width = 1920, Height = 1080 };
        }

        int left = int.MaxValue;
        int top = int.MaxValue;
        int right = int.MinValue;
        int bottom = int.MinValue;
        foreach (var monitor in monitors)
        {
            left = Math.Min(left, monitor.X);
            top = Math.Min(top, monitor.Y);
            right = Math.Max(right, monitor.X + monitor.Width);
            bottom = Math.Max(bottom, monitor.Y + monitor.Height);
        }

        return new RectInt32
        {
            X = left,
            Y = top,
            Width = right - left,
            Height = bottom - top
        };
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
            _currentFrame?.Dispose();
            _currentFrame = null;
        }

        _pauseSurface?.Dispose();
        _pauseSurface = null;

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

        _cropRect = null;
        _stopwatch.Reset();
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
