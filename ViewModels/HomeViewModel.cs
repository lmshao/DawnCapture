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
    private string _statusText = LocalizationService.GetString("Status_Ready");

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResumeCommand))]
    private bool _isRecording;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResumeCommand))]
    private bool _isPaused;

    public string ElapsedText => _recordingService.Elapsed.ToString(@"hh\:mm\:ss");

    private bool CanStop() => IsRecording;

    private bool CanPause() => IsRecording && !IsPaused;

    private bool CanResume() => IsRecording && IsPaused;

    public async Task StartRecordingAsync(RecordingMode mode)
    {
        Log.Info($"开始录制请求：模式={mode}");
        switch (mode)
        {
            case RecordingMode.Desktop:
                StatusText = LocalizationService.GetString("Status_StartingDesktop");
                await _recordingService.StartDesktopAsync();
                break;
            case RecordingMode.Window:
                StatusText = LocalizationService.GetString("Status_StartingWindow");
                await _recordingService.PickAndStartWindowAsync();
                break;
            case RecordingMode.Region:
                StatusText = LocalizationService.GetString("Status_SelectingRegion");
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

    private void OnRecordingStateChanged(object? sender, RecordingState state)
    {
        IsRecording = state is RecordingState.Recording or RecordingState.Paused;
        IsPaused = state == RecordingState.Paused;

        StatusText = state switch
        {
            RecordingState.Idle => LocalizationService.GetString("Status_Ready"),
            RecordingState.PickingSource => LocalizationService.GetString("Status_Preparing"),
            RecordingState.Recording => LocalizationService.GetString("Status_Recording"),
            RecordingState.Paused => LocalizationService.GetString("Status_Paused"),
            RecordingState.Stopping => LocalizationService.GetString("Status_Stopping"),
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
        Log.Error($"录制失败：{message}");
        StatusText = string.Format(LocalizationService.GetString("Error_StartRecording"), message);
    }
}
