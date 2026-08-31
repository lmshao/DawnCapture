using System;
using System.Runtime.InteropServices;
using DawnCapture.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace DawnCapture.Views;

public sealed partial class RecordingControlWindow : Window
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;
    private const uint WdaExcludeFromCapture = 0x00000011;

    private readonly Func<TimeSpan> _elapsedProvider;
    private readonly DispatcherTimer _timer;

    public RecordingControlWindow(Func<TimeSpan> elapsedProvider)
    {
        _elapsedProvider = elapsedProvider;
        InitializeComponent();

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
    }

    public event Action? StartRequested;

    public event Action? StopRequested;

    public event Action? CancelRequested;

    public void ShowWaiting(RectInt32 region)
    {
        PositionNear(region);
        RecordButton.Visibility = Visibility.Visible;
        StopButton.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Visible;
        RecDot.Visibility = Visibility.Visible;
        TimeText.Text = "00:00";
        Activate();
    }

    public void ShowRecording()
    {
        RecordButton.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Collapsed;
        StopButton.Visibility = Visibility.Visible;
        RecDot.Visibility = Visibility.Visible;
        _timer.Start();
        UpdateTime();
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
        int width = 190;
        int height = 36;
        int gap = 8;

        int x = region.X;
        int y = region.Y - height - gap;
        if (y < 0)
        {
            y = region.Y + region.Height + gap;
        }

        AppWindow.MoveAndResize(new RectInt32
        {
            X = x,
            Y = y,
            Width = width,
            Height = height
        });
    }

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

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        CancelRequested?.Invoke();
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
