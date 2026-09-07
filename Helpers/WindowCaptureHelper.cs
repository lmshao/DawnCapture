using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using DawnCapture;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using Windows.Graphics.Capture;
using WinRT.Interop;

namespace DawnCapture.Helpers;

public static class WindowCaptureHelper
{
    private const int MonitorDefaultToNearest = 2;
    private const uint MdtEffectiveDpi = 0;

    public static async Task<GraphicsCaptureItem?> PickWindowAsync()
    {
        if (App.MainWindow is null)
        {
            return null;
        }

        var picker = new GraphicsCapturePicker();
        var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);
        return await picker.PickSingleItemAsync();
    }

    public static RectInt32 GetWindowBounds(GraphicsCaptureItem item)
    {
        if (TryResolveWindowHandle(item) is IntPtr hwnd && hwnd != IntPtr.Zero &&
            TryGetWindowRect(hwnd, out var rect))
        {
            return new RectInt32
            {
                X = rect.Left,
                Y = rect.Top,
                Width = rect.Right - rect.Left,
                Height = rect.Bottom - rect.Top
            };
        }

        return GetFallbackBounds(item);
    }

    public static double GetWindowDpiScale(GraphicsCaptureItem item)
    {
        if (TryResolveWindowHandle(item) is IntPtr hwnd && hwnd != IntPtr.Zero)
        {
            var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (GetDpiForMonitor(monitor, MdtEffectiveDpi, out uint dpiX, out _) == 0 && dpiX > 0)
            {
                return dpiX / 96.0;
            }
        }

        return 1.0;
    }

    private static RectInt32 GetFallbackBounds(GraphicsCaptureItem item)
    {
        _ = GetCursorPos(out var point);
        var monitor = MonitorFromPoint(point, MonitorDefaultToNearest);
        var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (GetMonitorInfo(monitor, ref info))
        {
            return new RectInt32
            {
                X = info.rcMonitor.Left + 12,
                Y = info.rcMonitor.Top + 12,
                Width = item.Size.Width,
                Height = item.Size.Height
            };
        }

        return new RectInt32
        {
            X = 12,
            Y = 12,
            Width = item.Size.Width,
            Height = item.Size.Height
        };
    }

    public static IntPtr TryResolveWindowHandle(GraphicsCaptureItem picked)
    {
        IntPtr resolved = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
            {
                return true;
            }

            var candidate = CreateCaptureItemForWindow(hwnd);
            if (candidate is null)
            {
                return true;
            }

            if (string.Equals(candidate.DisplayName, picked.DisplayName, StringComparison.Ordinal) &&
                candidate.Size.Width == picked.Size.Width &&
                candidate.Size.Height == picked.Size.Height)
            {
                resolved = hwnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        if (resolved != IntPtr.Zero)
        {
            return resolved;
        }

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
            {
                return true;
            }

            var candidate = CreateCaptureItemForWindow(hwnd);
            if (candidate is null)
            {
                return true;
            }

            if (string.Equals(candidate.DisplayName, picked.DisplayName, StringComparison.Ordinal))
            {
                resolved = hwnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        return resolved;
    }

    private static GraphicsCaptureItem? CreateCaptureItemForWindow(IntPtr hwnd)
    {
        const string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
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
                var classId = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
                hr = interop.CreateForWindow(hwnd, ref classId, out var pItem);
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

    private static bool TryGetWindowRect(IntPtr hwnd, out NativeRect rect)
    {
        rect = default;
        return GetWindowRect(hwnd, out rect);
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int cbSize;
        public NativeRect rcMonitor;
        public NativeRect rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
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

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint pt, int dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hMonitor, uint dpiType, out uint dpiX, out uint dpiY);

    [DllImport("combase.dll")]
    private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string sourceString, int length, out IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);
}
