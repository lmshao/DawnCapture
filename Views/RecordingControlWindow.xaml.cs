using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace DawnCapture.Views;

public sealed partial class RecordingControlWindow : Window
{
    private readonly Func<TimeSpan> _elapsedProvider;
    private readonly DispatcherTimer _timer;

    public RecordingControlWindow(Func<TimeSpan> elapsedProvider)
    {
        _elapsedProvider = elapsedProvider;
        InitializeComponent();

        Title = "录制控制";
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
            // 窗口可能已经关闭。
        }
    }

    private void PositionNear(RectInt32 region)
    {
        int width = 220;
        int height = 44;
        int gap = 8;

        int x = Math.Max(0, region.X);
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
}
