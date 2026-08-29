using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using DawnCapture.Models;
using Microsoft.UI.Xaml;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.MediaFoundation;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace DawnCapture.Services;

public sealed class RecordingService : IRecordingService
{
    private static readonly Guid IidDxgiSurface = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    private static readonly Guid IidDirect3DDxgiInterfaceAccess = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");

    private readonly ISettingsService _settings;
    private readonly Stopwatch _stopwatch = new();

    private ID3D11Device? _d3dDevice;
    private IDirect3DDevice? _winrtDevice;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private GraphicsCaptureItem? _item;
    private IMFSinkWriter? _sinkWriter;
    private int _streamIndex;
    private long _frameDuration = 333_333;
    private bool _mfStarted;
    private string? _outputFilePath;

    private RecordingState _state = RecordingState.Idle;

    public RecordingService(ISettingsService settings)
    {
        _settings = settings;
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
            StateChanged?.Invoke(this, value);
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

            return StartCapture(item);
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

        await Task.Run(() =>
        {
            try
            {
                FinalizeWriter();
            }
            catch
            {
                // 编码器收尾失败不应让停止流程崩溃。
            }
        });

        CleanupCapture();
        State = RecordingState.Idle;
    }

    public void Pause()
    {
        if (State != RecordingState.Recording)
        {
            return;
        }

        _stopwatch.Stop();
        State = RecordingState.Paused;
    }

    public void Resume()
    {
        if (State != RecordingState.Paused)
        {
            return;
        }

        _stopwatch.Start();
        State = RecordingState.Recording;
    }

    private bool StartCapture(GraphicsCaptureItem item)
    {
        try
        {
            EnsureDevice();

            _frameDuration = 10_000_000L / Math.Max(1, _settings.Current.FrameRate);

            _framePool = Direct3D11CaptureFramePool.Create(
                _winrtDevice!,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                item.Size);

            _session = _framePool.CreateCaptureSession(item);
            _session.IsCursorCaptureEnabled = _settings.Current.CaptureCursor;
            _framePool.FrameArrived += OnFrameArrived;

            _item = item;
            _item.Closed += OnItemClosed;

            if (!InitializeSinkWriter(item.Size))
            {
                CleanupCapture();
                return false;
            }

            _session.StartCapture();
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
            out _);

        if (result.Failure)
        {
            result = D3D11.D3D11CreateDevice(
                null,
                DriverType.Warp,
                DeviceCreationFlags.BgraSupport,
                null!,
                out _d3dDevice,
                out _,
                out _);
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

    private bool InitializeSinkWriter(Windows.Graphics.SizeInt32 size)
    {
        var hr = MediaFactory.MFStartup();
        if (hr.Failure)
        {
            RaiseFailed($"初始化 Media Foundation 失败：{hr}");
            return false;
        }

        _mfStarted = true;

        try
        {
            var folder = string.IsNullOrWhiteSpace(_settings.Current.OutputFolder)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "DawnCapture")
                : _settings.Current.OutputFolder;

            Directory.CreateDirectory(folder);
            _outputFilePath = Path.Combine(folder, $"DawnCapture_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");

            using var mediaType = MediaFactory.MFCreateMediaType();
            mediaType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            mediaType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.H264);
            mediaType.Set(MediaTypeAttributeKeys.AvgBitrate, (uint)(_settings.Current.BitrateKbps * 1000));
            mediaType.Set(MediaTypeAttributeKeys.InterlaceMode, 2u);
            mediaType.Set(MediaTypeAttributeKeys.FrameSize, ((ulong)size.Width << 32) | (uint)size.Height);
            mediaType.Set(MediaTypeAttributeKeys.FrameRate, ((ulong)_settings.Current.FrameRate << 32) | 1);
            mediaType.Set(MediaTypeAttributeKeys.PixelAspectRatio, ((ulong)1 << 32) | 1);

            _sinkWriter = MediaFactory.MFCreateSinkWriterFromURL(_outputFilePath, null!, null!);
            _streamIndex = _sinkWriter.AddStream(mediaType);
            _sinkWriter.SetInputMediaType(_streamIndex, mediaType, null!);
            _sinkWriter.BeginWriting();
            return true;
        }
        catch (Exception ex)
        {
            RaiseFailed(ex.Message);
            return false;
        }
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        if (State != RecordingState.Recording || _sinkWriter is null)
        {
            return;
        }

        using var frame = sender.TryGetNextFrame();
        if (frame is null)
        {
            return;
        }

        WriteFrame(frame.Surface, frame.SystemRelativeTime.Ticks);
    }

    private void WriteFrame(IDirect3DSurface surface, long timestamp)
    {
        var pDxgiSurface = GetDxgiSurfacePointer(surface);
        if (pDxgiSurface == IntPtr.Zero)
        {
            return;
        }

        try
        {
            using var dxgiSurface = new IDXGISurface(pDxgiSurface);
            using var buffer = MediaFactory.MFCreateDXGISurfaceBuffer(
                IidDxgiSurface,
                dxgiSurface,
                0,
                false);

            using var sample = MediaFactory.MFCreateSample();
            sample.AddBuffer(buffer);
            sample.SampleTime = timestamp;
            sample.SampleDuration = _frameDuration;

            _sinkWriter!.WriteSample(_streamIndex, sample);
        }
        catch (Exception ex)
        {
            RaiseFailed(ex.Message);
        }
        finally
        {
            Marshal.Release(pDxgiSurface);
        }
    }

    private IntPtr GetDxgiSurfacePointer(IDirect3DSurface surface)
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
            var iidSurface = IidDxgiSurface;
            access.GetInterface(ref iidSurface, out var pSurface);
            return pSurface;
        }
        finally
        {
            Marshal.Release(pAccess);
        }
    }

    private void OnItemClosed(GraphicsCaptureItem sender, object args)
    {
        if (State is RecordingState.Recording or RecordingState.Paused)
        {
            _ = StopAsync();
        }
    }

    private void FinalizeWriter()
    {
        if (_sinkWriter is null)
        {
            return;
        }

        _sinkWriter.Finalize();
        _sinkWriter.Dispose();
        _sinkWriter = null;
    }

    private void CleanupCapture()
    {
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

        if (_mfStarted)
        {
            try
            {
                MediaFactory.MFShutdown();
            }
            catch
            {
                // 忽略释放异常。
            }

            _mfStarted = false;
        }

        _stopwatch.Reset();
    }

    private void RaiseFailed(string message)
    {
        RecordingFailed?.Invoke(this, message);
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(
        IntPtr dxgiDevice,
        out IntPtr graphicsDevice);

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        [PreserveSig]
        int GetInterface(ref Guid iid, out IntPtr p);
    }
}
