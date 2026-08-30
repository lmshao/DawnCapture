using System;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.Graphics;

namespace DawnCapture.Views;

public sealed partial class RegionPickerWindow : Window
{
    private readonly TaskCompletionSource<RectInt32?> _tcs = new();
    private Point _start;
    private bool _isDragging;

    public RegionPickerWindow(RectInt32 monitorBounds)
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

        AppWindow.MoveAndResize(monitorBounds);
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

        _tcs.TrySetResult(new RectInt32
        {
            X = px,
            Y = py,
            Width = width,
            Height = height
        });

        Close();
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
}
