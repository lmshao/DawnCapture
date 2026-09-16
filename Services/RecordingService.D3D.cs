using System;
using System.Runtime.InteropServices;
using DawnCapture.Helpers;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Windows.Graphics;
using Windows.Graphics.DirectX.Direct3D11;

namespace DawnCapture.Services;

public sealed partial class RecordingService
{
    /// <summary>
    /// Retained copy of the last encoded frame. It is what gets repeated while the screen
    /// delivers no frames at all, which is why it is our own texture rather than a
    /// reference to a capture frame: the capture pool owns at most two buffers and recycles
    /// them, so holding one of those for minutes would stall frame delivery.
    /// </summary>
    private ID3D11Texture2D? _replicaTexture;
    private IDirect3DSurface? _replicaSurface;
    private SizeInt32 _replicaSize;
    private TimeSpan _replicaCapturedAt;
    private bool _replicaValid;

    private static readonly TimeSpan ReplicaRefreshInterval = TimeSpan.FromSeconds(1);

    private IDirect3DSurface? CropSurface(IDirect3DSurface sourceSurface, RectInt32 crop)
    {
        if (_d3dDevice is null || _d3dContext is null)
        {
            return null;
        }

        var pSource = Direct3D11Interop.GetTexture2DPointer(sourceSurface);
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

        var pSource = Direct3D11Interop.GetTexture2DPointer(sourceSurface);
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

    /// <summary>
    /// Copies the finished (already cropped / composited) frame into the retained copy, at
    /// most once per <see cref="ReplicaRefreshInterval"/>: a still screen makes an older
    /// copy indistinguishable, so this keeps the full-frame GPU copy off the hot path.
    /// </summary>
    private void RefreshReplicaIfDue(IDirect3DSurface surface)
    {
        if (_d3dDevice is null || _d3dContext is null)
        {
            return;
        }

        if (!_replicaValid ||
            _replicaSurface is null ||
            _replicaSize.Width != _encodeSize.Width ||
            _replicaSize.Height != _encodeSize.Height)
        {
            if (!EnsureReplicaResources(_encodeSize))
            {
                return;
            }
        }

        var elapsed = _stopwatch.Elapsed;
        if (_replicaValid && elapsed - _replicaCapturedAt < ReplicaRefreshInterval)
        {
            return;
        }

        var pSource = Direct3D11Interop.GetTexture2DPointer(surface);
        if (pSource == IntPtr.Zero)
        {
            return;
        }

        try
        {
            using var source = new ID3D11Texture2D(pSource);
            var sourceDesc = source.Description;
            int copyWidth = Math.Min((int)sourceDesc.Width, _replicaSize.Width);
            int copyHeight = Math.Min((int)sourceDesc.Height, _replicaSize.Height);
            if (copyWidth <= 0 || copyHeight <= 0)
            {
                return;
            }

            var box = new Box(0, 0, 0, copyWidth, copyHeight, 1);
            _d3dContext.CopySubresourceRegion(_replicaTexture, 0, 0, 0, 0, source, 0, box);
            _replicaCapturedAt = elapsed;
            _replicaValid = true;
        }
        catch (Exception ex)
        {
            Log.Info($"Retained frame copy failed: {ex.Message}");
            _replicaValid = false;
        }
    }

    private bool EnsureReplicaResources(SizeInt32 size)
    {
        if (_d3dDevice is null || size.Width <= 0 || size.Height <= 0)
        {
            return false;
        }

        ReleaseReplicaResources();

        var description = new Texture2DDescription
        {
            Width = (uint)size.Width,
            Height = (uint)size.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None
        };

        _replicaTexture = _d3dDevice.CreateTexture2D(description);
        using var dxgiSurface = _replicaTexture.QueryInterface<IDXGISurface>();
        int hr = CreateDirect3D11SurfaceFromDXGISurface(dxgiSurface.NativePointer, out var pWinrtSurface);
        if (hr != 0)
        {
            ReleaseReplicaResources();
            return false;
        }

        try
        {
            _replicaSurface = WinRT.MarshalInterface<IDirect3DSurface>.FromAbi(pWinrtSurface);
        }
        finally
        {
            Marshal.Release(pWinrtSurface);
        }

        _replicaSize = size;
        return true;
    }

    private void ReleaseReplicaResources()
    {
        _replicaSurface?.Dispose();
        _replicaSurface = null;
        _replicaTexture?.Dispose();
        _replicaTexture = null;
        _replicaSize = default;
        _replicaCapturedAt = TimeSpan.Zero;
        _replicaValid = false;
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11SurfaceFromDXGISurface(
        IntPtr dxgiSurface,
        out IntPtr graphicsSurface);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int nCmdShow);

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
    private static extern IntPtr MonitorFromPoint(NativePoint pt, uint dwFlags);

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
