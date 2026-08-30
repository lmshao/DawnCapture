using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;

namespace DawnCapture.Views;

public sealed partial class RegionPickerWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExLayered = 0x00080000;
    private const int WsExTransparent = 0x20;
    private const int WsExNoActivate = 0x08000000;
    private const uint LwaColorKey = 0x00000001;
    private const int WmEraseBkgnd = 0x0014;
    private const int MagentaColorRef = 0x00FF00FF;

    private readonly TaskCompletionSource<RectInt32?> _tcs = new();
    private readonly WindowSubclassProc _subclassProc;

    private Point _start;
    private bool _isDragging;
    private bool _isIndicatorMode;

    public RegionPickerWindow(RectInt32 virtualScreenBounds)
    {
        InitializeComponent();

        Title = "选择录制区域";
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }

        AppWindow.MoveAndResize(virtualScreenBounds);

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _subclassProc = WindowSubClass;
        SetWindowSubclass(hwnd, _subclassProc, 0, 0);
        EnableAcrylic();
    }

    public Task<RectInt32?> PickAsync()
    {
        Activate();
        return _tcs.Task;
    }

    private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Root.Focus(FocusState.Programmatic);
        _start = e.GetCurrentPoint(Root).Position;
        _isDragging = true;

        FullShade.Visibility = Visibility.Collapsed;
        UpdateSelection(_start, _start);
    }

    private void Root_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        UpdateSelection(_start, e.GetCurrentPoint(Root).Position);
    }

    private void Root_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        UpdateSelection(_start, e.GetCurrentPoint(Root).Position);
        CompleteSelection();
    }

    private void UpdateSelection(Point a, Point b)
    {
        double x = Math.Min(a.X, b.X);
        double y = Math.Min(a.Y, b.Y);
        double width = Math.Abs(a.X - b.X);
        double height = Math.Abs(a.Y - b.Y);

        Canvas.SetLeft(SelectionBorder, x);
        Canvas.SetTop(SelectionBorder, y);
        SelectionBorder.Width = width;
        SelectionBorder.Height = height;
        SelectionBorder.Visibility = Visibility.Visible;

        UpdateShades(x, y, width, height);
    }

    private void UpdateShades(double x, double y, double width, double height)
    {
        double rootWidth = Root.ActualWidth;
        double rootHeight = Root.ActualHeight;

        ShadeTop.Visibility = Visibility.Visible;
        Canvas.SetLeft(ShadeTop, 0);
        Canvas.SetTop(ShadeTop, 0);
        ShadeTop.Width = rootWidth;
        ShadeTop.Height = Math.Max(0, y);

        ShadeBottom.Visibility = Visibility.Visible;
        Canvas.SetLeft(ShadeBottom, 0);
        Canvas.SetTop(ShadeBottom, y + height);
        ShadeBottom.Width = rootWidth;
        ShadeBottom.Height = Math.Max(0, rootHeight - y - height);

        ShadeLeft.Visibility = Visibility.Visible;
        Canvas.SetLeft(ShadeLeft, 0);
        Canvas.SetTop(ShadeLeft, y);
        ShadeLeft.Width = Math.Max(0, x);
        ShadeLeft.Height = height;

        ShadeRight.Visibility = Visibility.Visible;
        Canvas.SetLeft(ShadeRight, x + width);
        Canvas.SetTop(ShadeRight, y);
        ShadeRight.Width = Math.Max(0, rootWidth - x - width);
        ShadeRight.Height = height;
    }

    private void CompleteSelection()
    {
        double scale = Root.XamlRoot?.RasterizationScale ?? 1.0;
        double x = Canvas.GetLeft(SelectionBorder);
        double y = Canvas.GetTop(SelectionBorder);

        int px = Math.Max(0, (int)Math.Round(x * scale));
        int py = Math.Max(0, (int)Math.Round(y * scale));
        int width = (int)Math.Round(SelectionBorder.Width * scale);
        int height = (int)Math.Round(SelectionBorder.Height * scale);

        if (width < 8 || height < 8)
        {
            Cancel();
            return;
        }

        var region = new RectInt32
        {
            X = px,
            Y = py,
            Width = width,
            Height = height
        };

        EnterIndicatorMode(region);
        _tcs.TrySetResult(region);
    }

    private void EnterIndicatorMode(RectInt32 region)
    {
        _isIndicatorMode = true;

        // 关闭 Acrylic，改用 ColorKey 让窗口除虚线框外完全透明且点击穿透。
        SetAccentPolicy(AccentState.AccentDisabled);
        EnableColorKeyTransparency();

        AppWindow.MoveAndResize(region);

        Root.IsHitTestVisible = false;
        Root.Background = new SolidColorBrush(Colors.Transparent);
        HintText.Visibility = Visibility.Collapsed;
        FullShade.Visibility = Visibility.Collapsed;
        ShadeTop.Visibility = Visibility.Collapsed;
        ShadeLeft.Visibility = Visibility.Collapsed;
        ShadeRight.Visibility = Visibility.Collapsed;
        ShadeBottom.Visibility = Visibility.Collapsed;
        SelectionBorder.Visibility = Visibility.Collapsed;
        IndicatorRectangle.Visibility = Visibility.Visible;
    }

    private void EnableAcrylic()
    {
        // Windows 11 优先 Acrylic，失败回退到 Windows 10 的 BlurBehind。
        if (!SetAccentPolicy(AccentState.AccentEnableAcrylicBlurBehind))
        {
            SetAccentPolicy(AccentState.AccentEnableBlurBehind);
        }
    }

    private bool SetAccentPolicy(AccentState state)
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var accent = new AccentPolicy
            {
                AccentState = state,
                AccentFlags = 0,
                GradientColor = 0x44000000,
                AnimationId = 0
            };

            int size = Marshal.SizeOf<AccentPolicy>();
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WindowCompositionAttribute.WcaAccentPolicy,
                    SizeOfData = size,
                    Data = ptr
                };

                return SetWindowCompositionAttribute(hwnd, ref data) != 0;
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        catch
        {
            return false;
        }
    }

    private void EnableColorKeyTransparency()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        int exStyle = GetWindowLong(hwnd, GwlExStyle);
        SetWindowLong(hwnd, GwlExStyle, exStyle | WsExLayered | WsExTransparent | WsExNoActivate);
        SetLayeredWindowAttributes(hwnd, MagentaColorRef, 255, LwaColorKey);
    }

    private int WindowSubClass(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, uint dwRefData)
    {
        if (uMsg == WmEraseBkgnd && _isIndicatorMode)
        {
            GetClientRect(hWnd, out var rect);
            IntPtr brush = CreateSolidBrush(MagentaColorRef);
            FillRect(wParam, ref rect, brush);
            DeleteObject(brush);
            return 1;
        }

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private void OnEscapeInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Cancel();
    }

    private void Cancel()
    {
        _tcs.TrySetResult(null);
        Close();
    }

    private enum AccentState
    {
        AccentDisabled = 0,
        AccentEnableGradient = 1,
        AccentEnableTransparentGradient = 2,
        AccentEnableBlurBehind = 3,
        AccentEnableAcrylicBlurBehind = 4,
        AccentInvalidState = 5
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public uint AccentFlags;
        public uint GradientColor;
        public uint AnimationId;
    }

    private enum WindowCompositionAttribute
    {
        WcaAccentPolicy = 19
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate int WindowSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, uint dwRefData);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("comctl32.dll")]
    private static extern bool SetWindowSubclass(IntPtr hWnd, WindowSubclassProc pfnSubclass, uint uIdSubclass, uint dwRefData);

    [DllImport("comctl32.dll")]
    private static extern int DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr hdc, ref Rect rect, IntPtr hbrush);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(int crColor);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
