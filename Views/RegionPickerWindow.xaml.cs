using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using DawnCapture.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage.Streams;
using Point = Windows.Foundation.Point;

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
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpFrameChanged = 0x0020;
    private const uint RdwInvalidate = 0x0001;
    private const uint RdwUpdateNow = 0x0100;
    private const uint RdwFrame = 0x0400;

    private readonly TaskCompletionSource<RectInt32?> _tcs = new();
    private readonly WindowSubclassProc _subclassProc;
    private readonly Task _desktopImageTask;

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

        Log.Debug($"RegionPickerWindow 创建：虚拟屏幕 {virtualScreenBounds.Width}x{virtualScreenBounds.Height} @({virtualScreenBounds.X},{virtualScreenBounds.Y})");
        _desktopImageTask = LoadDesktopImageAsync(virtualScreenBounds);
    }

    public async Task<RectInt32?> PickAsync()
    {
        await _desktopImageTask;
        Activate();
        return await _tcs.Task;
    }

    private async Task LoadDesktopImageAsync(RectInt32 bounds)
    {
        try
        {
            var bitmap = await Task.Run(() => CaptureScreen(bounds));
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            bitmap.Dispose();

            var randomAccessStream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(randomAccessStream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(stream.ToArray());
                await writer.StoreAsync();
                await writer.FlushAsync();
            }

            randomAccessStream.Seek(0);
            var bitmapImage = new BitmapImage();
            await bitmapImage.SetSourceAsync(randomAccessStream);
            DesktopImage.Source = bitmapImage;
        }
        catch (Exception ex)
        {
            Log.Error("加载桌面截图失败", ex);
        }
    }

    private static Bitmap CaptureScreen(RectInt32 bounds)
    {
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(
            bounds.X,
            bounds.Y,
            0,
            0,
            new System.Drawing.Size(bounds.Width, bounds.Height),
            CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Root.Focus(FocusState.Programmatic);
        _start = e.GetCurrentPoint(Root).Position;
        _isDragging = true;
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

        Log.Debug($"区域选择完成：{region.Width}x{region.Height} @({region.X},{region.Y})");
        EnterIndicatorMode(region);
        _tcs.TrySetResult(region);
    }

    private void EnterIndicatorMode(RectInt32 region)
    {
        _isIndicatorMode = true;

        // 运行中切换 WS_EX_LAYERED 后必须 SetWindowPos 才生效，否则窗口会变白。
        EnableColorKeyTransparency();

        AppWindow.MoveAndResize(region);

        Root.IsHitTestVisible = false;
        Root.Background = new SolidColorBrush(Colors.Transparent);
        DesktopImage.Visibility = Visibility.Collapsed;
        FrostOverlay.Visibility = Visibility.Collapsed;
        HintText.Visibility = Visibility.Collapsed;
        ShadeTop.Visibility = Visibility.Collapsed;
        ShadeLeft.Visibility = Visibility.Collapsed;
        ShadeRight.Visibility = Visibility.Collapsed;
        ShadeBottom.Visibility = Visibility.Collapsed;
        SelectionBorder.Visibility = Visibility.Collapsed;
        IndicatorRectangle.Visibility = Visibility.Visible;
    }

    private void EnableColorKeyTransparency()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        int exStyle = GetWindowLong(hwnd, GwlExStyle);
        SetWindowLong(hwnd, GwlExStyle, exStyle | WsExLayered | WsExTransparent | WsExNoActivate);
        SetLayeredWindowAttributes(hwnd, MagentaColorRef, 255, LwaColorKey);

        SetWindowPos(
            hwnd,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged);

        RedrawWindow(hwnd, IntPtr.Zero, IntPtr.Zero, RdwInvalidate | RdwUpdateNow | RdwFrame);
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
        Log.Debug("区域选择已取消（Esc 或选区过小）。");
        _tcs.TrySetResult(null);
        Close();
    }

    private delegate int WindowSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, uint dwRefData);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

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
