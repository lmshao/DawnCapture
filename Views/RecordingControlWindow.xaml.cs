using System;
using System.Runtime.InteropServices;
using DawnCapture.Helpers;
using DawnCapture.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace DawnCapture.Views;

public sealed partial class RecordingControlWindow : Window
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;
    private const uint WdaExcludeFromCapture = 0x00000011;
    private const double WindowWidthDip = 220;
    private const double WindowHeightDip = 36;

    private readonly Func<TimeSpan> _elapsedProvider;
    private readonly DispatcherTimer _timer;
    private bool _isDragging;
    private NativePoint _dragStartCursor;
    private PointInt32 _dragStartWindow;
    private double _dpiScale = 1.0;

    public RecordingControlWindow(Func<TimeSpan> elapsedProvider)
    {
        _elapsedProvider = elapsedProvider;
        InitializeComponent();
        WindowIconHelper.Apply(this);

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        int cornerPreference = DwmwcpRound;
        DwmSetWindowAttribute(
            hwnd,
            DwmwaWindowCornerPreference,
            ref cornerPreference,
            sizeof(int));
        SetWindowDisplayAffinity(hwnd, WdaExcludeFromCapture);

        Title = LocalizationService.GetString("Window_RecordingControlTitle");
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => UpdateTime();

        DragHandle.PointerPressed += DragHandle_PointerPressed;
        DragHandle.PointerMoved += DragHandle_PointerMoved;
        DragHandle.PointerReleased += DragHandle_PointerReleased;
        DragHandle.PointerCanceled += DragHandle_PointerReleased;
    }

    public event Action? StartRequested;

    public event Action? StopRequested;

    public event Action? PauseRequested;

    public event Action? CancelRequested;

    public void ShowWaiting(RectInt32 region, double dpiScale = 1.0)
    {
        _dpiScale = dpiScale;
        RecordButton.Visibility = Visibility.Visible;
        StopButton.Visibility = Visibility.Collapsed;
        PauseButton.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Visible;
        RecDot.Visibility = Visibility.Visible;
        TimeText.Text = "00:00";
        PositionNear(region);
        Activate();
    }

    public void ShowRecording()
    {
        RecordButton.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Collapsed;
        StopButton.Visibility = Visibility.Visible;
        PauseButton.Visibility = Visibility.Visible;
        PauseButton.Content = "II";
        RecDot.Visibility = Visibility.Visible;
        _timer.Start();
        UpdateTime();
        RefreshSizeKeepPosition();
    }

    public void ShowPaused()
    {
        PauseButton.Content = "\uE768";
        UpdateTime();
    }

    public void ShowRecordingTopLeft(RectInt32 bounds, double dpiScale)
    {
        _dpiScale = dpiScale;
        ShowRecording();
        PositionTopLeft(bounds);
        Activate();
    }

    public void ShowRecordingFloating(double dpiScale = 1.0)
    {
        _dpiScale = dpiScale;
        ShowRecording();
        PositionFloatingTopLeft();
        Activate();
    }

    public void CloseWindow()
    {
        try
        {
            _timer.Stop();
            Close();
        }
        catch
        {
            // The window may already be closed.
        }
    }

    private void PositionNear(RectInt32 region)
    {
        const int gapDip = 8;
        int width = ToPixels(WindowWidthDip);
        int height = ToPixels(WindowHeightDip);

        int x = region.X;
        int y = region.Y - height - ToPixels(gapDip);
        if (y < 0)
        {
            y = region.Y + region.Height + ToPixels(gapDip);
        }

        AppWindow.MoveAndResize(new RectInt32
        {
            X = x,
            Y = y,
            Width = width,
            Height = height
        });
    }

    private void PositionTopLeft(RectInt32 bounds)
    {
        const int marginDip = 12;

        AppWindow.MoveAndResize(new RectInt32
        {
            X = bounds.X + ToPixels(marginDip),
            Y = bounds.Y + ToPixels(marginDip),
            Width = ToPixels(WindowWidthDip),
            Height = ToPixels(WindowHeightDip)
        });
    }

    private void PositionFloatingTopLeft()
    {
        var screen = ScreenBoundsHelper.GetVirtualScreenBounds();
        const int marginDip = 12;

        AppWindow.MoveAndResize(new RectInt32
        {
            X = screen.X + ToPixels(marginDip),
            Y = screen.Y + ToPixels(marginDip),
            Width = ToPixels(WindowWidthDip),
            Height = ToPixels(WindowHeightDip)
        });
    }

    private void RefreshSizeKeepPosition()
    {
        var position = AppWindow.Position;
        AppWindow.MoveAndResize(new RectInt32
        {
            X = position.X,
            Y = position.Y,
            Width = ToPixels(WindowWidthDip),
            Height = ToPixels(WindowHeightDip)
        });
    }

    private int ToPixels(double dip) => Math.Max(1, (int)Math.Ceiling(dip * _dpiScale));

    private void UpdateTime()
    {
        TimeText.Text = _elapsedProvider().ToString(@"mm\:ss");
    }

    private void RecordButton_Click(object sender, RoutedEventArgs e)
    {
        StartRequested?.Invoke();
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        StopRequested?.Invoke();
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        PauseRequested?.Invoke();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        CancelRequested?.Invoke();
    }

    private void DragHandle_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        GetCursorPos(out _dragStartCursor);
        _dragStartWindow = AppWindow.Position;
        _isDragging = true;
        DragHandle.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void DragHandle_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        GetCursorPos(out var cursor);
        var size = AppWindow.Size;
        AppWindow.MoveAndResize(new RectInt32
        {
            X = _dragStartWindow.X + (cursor.X - _dragStartCursor.X),
            Y = _dragStartWindow.Y + (cursor.Y - _dragStartCursor.Y),
            Width = size.Width,
            Height = size.Height
        });
        e.Handled = true;
    }

    private void DragHandle_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        DragHandle.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int attributeValue,
        int attributeSize);

    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
}
