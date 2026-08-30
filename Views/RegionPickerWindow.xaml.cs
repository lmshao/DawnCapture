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
    private const int WsExTransparent = 0x20;
    private const int WsExNoActivate = 0x08000000;

    private readonly TaskCompletionSource<RectInt32?> _tcs = new();
    private Point _start;
    private bool _isDragging;

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
        // 把窗口缩小到选区，只保留虚线框作为录制区域提示。
        AppWindow.MoveAndResize(region);
        Root.Background = new SolidColorBrush(Colors.Transparent);
        Root.IsHitTestVisible = false;
        HintText.Visibility = Visibility.Collapsed;
        SelectionBorder.Visibility = Visibility.Collapsed;
        IndicatorRectangle.Visibility = Visibility.Visible;
        MakeClickThrough();
    }

    private void MakeClickThrough()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            int exStyle = GetWindowLong(hwnd, GwlExStyle);
            SetWindowLong(hwnd, GwlExStyle, exStyle | WsExTransparent | WsExNoActivate);
        }
        catch
        {
            // 点击穿透失败时至少保持虚线框显示。
        }
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

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
