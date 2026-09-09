using System;
using System.Runtime.InteropServices;
using DawnCapture.Helpers;
using DawnCapture.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;

namespace DawnCapture.Views;

public sealed partial class RecordingControlWindow : Window
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;
    private const uint WdaExcludeFromCapture = 0x00000011;
    private const double DockHeightDip = 40;

    private readonly Func<TimeSpan> _elapsedProvider;
    private readonly DispatcherTimer _timer;
    private Storyboard? _pulseStoryboard;
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

        // The whole dock is draggable; buttons and the REC chip opt out.
        DockBorder.PointerPressed += DockBorder_PointerPressed;
        DockBorder.PointerMoved += DockBorder_PointerMoved;
        DockBorder.PointerReleased += DockBorder_PointerReleased;
        DockBorder.PointerCanceled += DockBorder_PointerReleased;

        WireHover(PauseButton);
        WireHover(StopButton);

        ToolTipService.SetToolTip(PauseButton, LocalizationService.GetString("RecordingControl_PauseToolTip"));
        ToolTipService.SetToolTip(StopButton, LocalizationService.GetString("RecordingControl_StopToolTip"));

        // Re-measure when the window moves to a display with a different scale.
        Root.Loaded += (_, _) =>
        {
            if (Root.XamlRoot is not { } xamlRoot)
            {
                return;
            }

            _dpiScale = xamlRoot.RasterizationScale;
            RefreshSizeKeepPosition();
        };
    }

    public event Action? StopRequested;

    public event Action? PauseRequested;

    public void ShowRecording()
    {
        RecChip.Visibility = Visibility.Visible;
        RecDot.Visibility = Visibility.Visible;
        PauseButton.Visibility = Visibility.Visible;
        PauseGlyph.Visibility = Visibility.Visible;
        PlayGlyph.Visibility = Visibility.Collapsed;
        StopButton.Visibility = Visibility.Visible;
        _timer.Start();
        UpdateTime();
        StartPulse();
        RefreshSizeKeepPosition();
    }

    public void ShowPaused()
    {
        PauseGlyph.Visibility = Visibility.Collapsed;
        PlayGlyph.Visibility = Visibility.Visible;
        ToolTipService.SetToolTip(PauseButton, LocalizationService.GetString("RecordingControl_ResumeToolTip"));
        UpdateTime();
        StopPulse();
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

    /// <summary>
    /// Recording dock placed just above the selected region so it never
    /// covers the recorded content.
    /// </summary>
    public void ShowRecordingNear(RectInt32 region, double dpiScale)
    {
        _dpiScale = dpiScale;
        ShowRecording();
        PositionNear(region);
        Activate();
    }

    public void CloseWindow()
    {
        try
        {
            _timer.Stop();
            StopPulse();
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
        int width = CurrentPixelWidth();
        int height = ToPixels(DockHeightDip);

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
            Width = CurrentPixelWidth(),
            Height = ToPixels(DockHeightDip)
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
            Width = CurrentPixelWidth(),
            Height = ToPixels(DockHeightDip)
        });
    }

    private void RefreshSizeKeepPosition()
    {
        var position = AppWindow.Position;
        AppWindow.MoveAndResize(new RectInt32
        {
            X = position.X,
            Y = position.Y,
            Width = CurrentPixelWidth(),
            Height = ToPixels(DockHeightDip)
        });
    }

    /// <summary>
    /// Measures the content (Auto width) and converts DIP to physical pixels
    /// using the current XamlRoot rasterization scale.
    /// </summary>
    private void UpdateWindowSize()
    {
        if (Root.XamlRoot is not { } xamlRoot)
        {
            return;
        }

        _dpiScale = xamlRoot.RasterizationScale;
        RefreshSizeKeepPosition();
    }

    private int CurrentPixelWidth()
    {
        Root.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        double contentWidth = Root.DesiredSize.Width;
        return Math.Max(1, (int)Math.Ceiling(contentWidth * _dpiScale));
    }

    private int ToPixels(double dip) => Math.Max(1, (int)Math.Ceiling(dip * _dpiScale));

    private void UpdateTime()
    {
        TimeText.Text = FormatElapsed(_elapsedProvider());
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        return elapsed.TotalHours >= 1
            ? elapsed.ToString(@"hh\:mm\:ss")
            : elapsed.ToString(@"mm\:ss");
    }

    private void StartPulse()
    {
        StopPulse();
        var animation = new DoubleAnimation
        {
            From = 1.0,
            To = 0.35,
            Duration = new Duration(TimeSpan.FromMilliseconds(800)),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(animation, RecDot);
        Storyboard.SetTargetProperty(animation, "Opacity");
        _pulseStoryboard = new Storyboard();
        _pulseStoryboard.Children.Add(animation);
        _pulseStoryboard.Begin();
    }

    private void StopPulse()
    {
        _pulseStoryboard?.Stop();
        _pulseStoryboard = null;
        RecDot.Opacity = 1.0;
    }

    private void WireHover(Button button)
    {
        button.PointerEntered += (_, _) =>
        {
            bool dark = Root.ActualTheme == ElementTheme.Dark;
            button.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                dark ? Microsoft.UI.ColorHelper.FromArgb(255, 0x33, 0x38, 0x3E)
                     : Microsoft.UI.ColorHelper.FromArgb(255, 0xEB, 0xEE, 0xF2));
        };
        button.PointerExited += (_, _) =>
            button.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        StopRequested?.Invoke();
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        PauseRequested?.Invoke();
    }

    private void DockBorder_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (IsInsideInteractiveElement(e.OriginalSource as DependencyObject))
        {
            return;
        }

        GetCursorPos(out _dragStartCursor);
        _dragStartWindow = AppWindow.Position;
        _isDragging = true;
        DockBorder.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void DockBorder_PointerMoved(object sender, PointerRoutedEventArgs e)
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

    private void DockBorder_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        DockBorder.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    /// <summary>
    /// True when the pressed element belongs to a button (or its glyph), so
    /// dragging never starts from a click target.
    /// </summary>
    private static bool IsInsideInteractiveElement(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is Button)
            {
                return true;
            }

            node = VisualTreeHelper.GetParent(node);
        }

        return false;
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
