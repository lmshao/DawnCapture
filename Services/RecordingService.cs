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
            RaiseStateChanged(value);
        }
    }

    public TimeSpan Elapsed => _stopwatch.Elapsed;

    public event EventHandler<RecordingState>? StateChanged;

    public event EventHandler<string>? RecordingFailed;

    public async Task<bool> PickAndStartAsync()
    {
        if (State != RecordingState.Idle)
        {
            return false;
        }

        if (App.MainWindow is null)
        {
            RaiseFailed("主窗口尚未准备好。");
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

    public async Task<bool> PickScreenAndStartRegionAsync()
    {
        if (State != RecordingState.Idle)
        {
            return false;
        }

        if (App.MainWindow is null)
        {
            RaiseFailed("主窗口尚未准备好。");
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

            var monitor = FindMonitorBounds(item.Size);
            if (monitor is null)
            {
                RaiseFailed("无法确定所选屏幕的坐标。区域录制请选择屏幕，而不是窗口。");
                State = RecordingState.Idle;
                return false;
            }

            var regionWindow = new RegionPickerWindow(monitor.Value);
            var region = await regionWindow.PickAsync();
            if (region is null)
            {
                State = RecordingState.Idle;
                return false;
            }

            var crop = ClampAndMakeEven(region.Value, item.Size);
            return await StartCaptureAsync(item, crop);
        }
        catch (Exception ex)
        {
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
                // 转码收尾异常已在任务续延中上报，这里不重复处理。
            }
        }

        CleanupCapture();
        State = RecordingState.Idle;

        if (_framesWritten == 0)
        {
            RaiseFailed("没有写入任何视频帧，生成的文件为空。请确认捕获目标仍然可见。");
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

            // StorageFile.GetFileFromPathAsync 要求文件已存在。
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
            throw new InvalidOperationException($"创建 D3D11 设备失败：{result}");
        }

        using var dxgiDevice = _d3dDevice.QueryInterface<IDXGIDevice>();
        int hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var pWinrtDevice);
        if (hr != 0)
        {
            throw new InvalidOperationException($"CreateDirect3D11DeviceFromDXGIDevice 失败：0x{hr:X8}");
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
                // 暂停后的第一帧：保存表面用于后续冻结帧，保持时间轴连续。
                _pauseSurface?.Dispose();
                _pauseSurface = surface;
                _pauseTimestamp = timestamp.Ticks;
            }

            args.Request.Sample = MediaStreamSample.CreateFromDirect3D11Surface(surface, timestamp);
            _framesWritten++;

            if (!_isPaused && croppedSurface is not null)
            {
                // 样本已持有该表面的引用，释放本方法持有的额外引用。
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

        try
        {
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

            return WinRT.MarshalInterface<IDirect3DSurface>.FromAbi(pWinrtSurface);
        }
        finally
        {
            Marshal.Release(pSource);
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

        // H.264 编码器通常要求偶数尺寸。
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

    private static RectInt32? FindMonitorBounds(SizeInt32 itemSize)
    {
        var monitors = new List<RectInt32>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr hMonitor, IntPtr hdcMonitor, ref NativeRect rcMonitor, IntPtr data)
        {
            var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(hMonitor, ref info))
            {
                monitors.Add(new RectInt32
                {
                    X = info.rcMonitor.Left,
                    Y = info.rcMonitor.Top,
                    Width = info.rcMonitor.Right - info.rcMonitor.Left,
                    Height = info.rcMonitor.Bottom - info.rcMonitor.Top
                });
            }

            return true;
        }, IntPtr.Zero);

        foreach (var monitor in monitors)
        {
            if (monitor.Width == itemSize.Width && monitor.Height == itemSize.Height)
            {
                return monitor;
            }
        }

        // 回退：使用光标所在的显示器。
        GetCursorPos(out var point);
        var hMonitor = MonitorFromPoint(point, 2 /* MONITOR_DEFAULTTONEAREST */);
        var fallback = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (GetMonitorInfo(hMonitor, ref fallback))
        {
            return new RectInt32
            {
                X = fallback.rcMonitor.Left,
                Y = fallback.rcMonitor.Top,
                Width = fallback.rcMonitor.Right - fallback.rcMonitor.Left,
                Height = fallback.rcMonitor.Bottom - fallback.rcMonitor.Top
            };
        }

        return null;
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
                // 忽略释放异常。
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
                // 忽略释放异常。
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

        if (_outputStream is not null)
        {
            try
            {
                _outputStream.Dispose();
            }
            catch
            {
                // 忽略释放异常。
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
