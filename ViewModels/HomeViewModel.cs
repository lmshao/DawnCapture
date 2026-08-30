using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DawnCapture.Models;
using DawnCapture.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DawnCapture.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly IRecordingService _recordingService;
    private readonly DispatcherTimer _timer;

    public HomeViewModel(IRecordingService recordingService)
    {
        _recordingService = recordingService;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => OnPropertyChanged(nameof(ElapsedText));

        _recordingService.StateChanged += OnRecordingStateChanged;
        _recordingService.RecordingFailed += OnRecordingFailed;
    }

    [ObservableProperty]
    private string _statusText = "准备就绪";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResumeCommand))]
    private bool _isRecording;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResumeCommand))]
    private bool _isPaused;

    public string ElapsedText => _recordingService.Elapsed.ToString(@"hh\:mm\:ss");

    private bool CanStart() => !IsRecording;

    private bool CanStop() => IsRecording;

    private bool CanPause() => IsRecording && !IsPaused;

    private bool CanResume() => IsRecording && IsPaused;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        var mode = await PickRecordingModeAsync();
        switch (mode)
        {
            case RecordingMode.Desktop:
                StatusText = "正在开始桌面录制…";
                await _recordingService.StartDesktopAsync();
                break;
            case RecordingMode.Window:
                StatusText = "正在选择要录制的窗口…";
                await _recordingService.PickAndStartAsync();
                break;
            case RecordingMode.Region:
                StatusText = "正在选择录制区域…";
                await _recordingService.StartRegionAsync();
                break;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        await _recordingService.StopAsync();
    }

    [RelayCommand(CanExecute = nameof(CanPause))]
    private void Pause()
    {
        _recordingService.Pause();
    }

    [RelayCommand(CanExecute = nameof(CanResume))]
    private void Resume()
    {
        _recordingService.Resume();
    }

    private async Task<RecordingMode> PickRecordingModeAsync()
    {
        var tcs = new TaskCompletionSource<RecordingMode>();

        var dialog = new ContentDialog
        {
            Title = "选择录制方式",
            XamlRoot = App.MainWindow?.Content?.XamlRoot
        };

        var panel = new StackPanel { Spacing = 12 };
        var desktopButton = new Button { Content = "桌面录制", HorizontalAlignment = HorizontalAlignment.Stretch };
        var windowButton = new Button { Content = "窗口录制", HorizontalAlignment = HorizontalAlignment.Stretch };
        var regionButton = new Button { Content = "区域录制", HorizontalAlignment = HorizontalAlignment.Stretch };
        var cancelButton = new Button { Content = "取消", HorizontalAlignment = HorizontalAlignment.Stretch };

        desktopButton.Click += (_, _) => { tcs.TrySetResult(RecordingMode.Desktop); dialog.Hide(); };
        windowButton.Click += (_, _) => { tcs.TrySetResult(RecordingMode.Window); dialog.Hide(); };
        regionButton.Click += (_, _) => { tcs.TrySetResult(RecordingMode.Region); dialog.Hide(); };
        cancelButton.Click += (_, _) => { tcs.TrySetResult(RecordingMode.None); dialog.Hide(); };

        panel.Children.Add(desktopButton);
        panel.Children.Add(windowButton);
        panel.Children.Add(regionButton);
        panel.Children.Add(cancelButton);
        dialog.Content = panel;

        dialog.Closed += (_, _) => tcs.TrySetResult(RecordingMode.None);
        await dialog.ShowAsync();
        return await tcs.Task;
    }

    private void OnRecordingStateChanged(object? sender, RecordingState state)
    {
        IsRecording = state is RecordingState.Recording or RecordingState.Paused;
        IsPaused = state == RecordingState.Paused;

        StatusText = state switch
        {
            RecordingState.Idle => "准备就绪",
            RecordingState.PickingSource => "正在准备录制…",
            RecordingState.Recording => "录制中",
            RecordingState.Paused => "已暂停",
            RecordingState.Stopping => "正在停止…",
            _ => StatusText
        };

        if (state == RecordingState.Recording)
        {
            _timer.Start();
        }
        else if (state == RecordingState.Idle)
        {
            _timer.Stop();
        }

        OnPropertyChanged(nameof(ElapsedText));
    }

    private void OnRecordingFailed(object? sender, string message)
    {
        StatusText = $"录制失败：{message}";
    }

    private enum RecordingMode
    {
        None,
        Desktop,
        Window,
        Region
    }
}
