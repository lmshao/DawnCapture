using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DawnCapture.Models;
using DawnCapture.Services;
using Microsoft.UI.Xaml;

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
        StatusText = "正在选择要录制的屏幕或窗口…";
        await _recordingService.PickAndStartAsync();
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

    private void OnRecordingStateChanged(object? sender, RecordingState state)
    {
        IsRecording = state is RecordingState.Recording or RecordingState.Paused;
        IsPaused = state == RecordingState.Paused;

        StatusText = state switch
        {
            RecordingState.Idle => "准备就绪",
            RecordingState.PickingSource => "正在选择录制目标…",
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
}
