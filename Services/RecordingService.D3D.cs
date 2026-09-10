using System;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Windows.Graphics;
using Windows.Graphics.DirectX.Direct3D11;

namespace DawnCapture.Services;

public sealed partial class RecordingService
{
    private static readonly Guid IidDirect3DDxgiInterfaceAccess = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
    private static readonly Guid IidD3D11Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

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
