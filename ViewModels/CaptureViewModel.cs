using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DawnCapture.Models;
using DawnCapture.Services;
using DawnCapture.Views;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace DawnCapture.ViewModels;

public partial class CaptureViewModel : ObservableObject
{
    public CaptureViewModel(
        ISettingsService settingsService,
        MainViewModel mainViewModel,
        IMonitorService monitorService,
        IRecordingService recordingService)
    {
        _settingsService = settingsService;
        _mainViewModel = mainViewModel;
        _monitorService = monitorService;
        _recordingService = recordingService;

        _recordingService.StateChanged += OnRecordingStateChanged;
        _recordingService.RecordingFailed += OnRecordingFailed;

        DestinationFolderSummary = _mainViewModel.OutputFolderSummary;
        RecordButtonLabel = LocalizationService.GetString("Dock_StartRecording");
        VideoQualityLabel = LocalizationService.GetString("Dock_Quality");
        AudioQualityLabel = LocalizationService.GetString("Dock_AudioQuality");
        VideoCodecSummary = LocalizationService.GetString("Dock_Codec_Avc");
        AudioCodecSummary = LocalizationService.GetString("Dock_Codec_Audio");
        InitializeMonitors();
        ApplySelectedMode();
        UpdateHeaderCopy();
        UpdatePreviewCopy();
    }

    private readonly ISettingsService _settingsService;
    private readonly MainViewModel _mainViewModel;
    private readonly IMonitorService _monitorService;
    private readonly IRecordingService _recordingService;

    public ObservableCollection<MonitorDisplay> Monitors { get; } = new();

    [ObservableProperty]
    private MonitorDisplay? _selectedDisplay;

    [ObservableProperty]
    private ImageSource? _selectedDisplayThumbnail;

    [ObservableProperty]
    private string _selectedDisplayBadge = string.Empty;

    [ObservableProperty]
    private bool _showDisplayPreview;

    [ObservableProperty]
    private CaptureModeKind _selectedMode = CaptureModeKind.FullScreen;

    [ObservableProperty]
    private string _eyebrow = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _sourceOptionsLabel = string.Empty;

    [ObservableProperty]
    private bool _showCursorOption = true;

    [ObservableProperty]
    private bool _microphoneEnabled = true;

    [ObservableProperty]
    private bool _systemAudioEnabled = true;

    [ObservableProperty]
    private bool _showCursor = true;

    [ObservableProperty]
    private bool _showVideoPreview = true;

    [ObservableProperty]
    private bool _showAudioPreview;

    [ObservableProperty]
    private string _previewLabel = string.Empty;

    [ObservableProperty]
    private string _previewCaption = string.Empty;

    [ObservableProperty]
    private bool _showPreviewAction;

    [ObservableProperty]
    private string _previewActionLabel = string.Empty;

    [ObservableProperty]
    private bool _hasSource = true;

    [ObservableProperty]
    private bool _showMonitorGrid;

    [ObservableProperty]
    private bool _showEmptySource;

    [ObservableProperty]
    private bool _showRegionPreview;

    [ObservableProperty]
    private bool _showWindowPreview;

    [ObservableProperty]
    private string _emptySourceTitle = string.Empty;

    [ObservableProperty]
    private string _emptySourceDescription = string.Empty;

    [ObservableProperty]
    private string _emptySourceButton = string.Empty;

    [ObservableProperty]
    private string _destinationFolderSummary = string.Empty;

    [ObservableProperty]
    private bool _showVideoQuality = true;

    [ObservableProperty]
    private string _videoQualityLabel = string.Empty;

    [ObservableProperty]
    private string _audioQualityLabel = string.Empty;

    [ObservableProperty]
    private string _videoCodecSummary = string.Empty;

    [ObservableProperty]
    private string _audioCodecSummary = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<string> _qualityOptions =
        new[] { "Compact / 30 FPS", "Balanced / 30 FPS", "Smooth / 60 FPS" };

    [ObservableProperty]
    private IReadOnlyList<string> _audioQualityOptions =
        new[] { "Standard / 128 kbps", "High / 192 kbps", "Best / 256 kbps" };

    [ObservableProperty]
    private int _qualityIndex = 1;

    [ObservableProperty]
    private int _audioQualityIndex;

    [ObservableProperty]
    private string _recordButtonLabel = string.Empty;

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private bool _showController;

    [ObservableProperty]
    private string _timerText = "00:00";

    [ObservableProperty]
    private string _controllerSource = string.Empty;

    [ObservableProperty]
    private bool _isPaused;

    public string OutputFolderFull => _mainViewModel.OutputFolderFull;

    partial void OnSelectedDisplayChanged(MonitorDisplay? value)
    {
        SelectedDisplayThumbnail = value?.Thumbnail;
        SelectedDisplayBadge = value?.BadgeLabel ?? string.Empty;
        SyncHasSource();
        UpdateHeaderCopy();
        UpdatePreviewCopy();
    }

    partial void OnSelectedModeChanged(CaptureModeKind value)
    {
        ApplySelectedMode();
        UpdateHeaderCopy();
        UpdatePreviewCopy();
    }

    private void ApplySelectedMode()
    {
        ShowAudioPreview = SelectedMode == CaptureModeKind.AudioOnly;
        ShowVideoPreview = SelectedMode != CaptureModeKind.AudioOnly;
        ShowCursorOption = SelectedMode != CaptureModeKind.AudioOnly;
        SourceOptionsLabel = SelectedMode == CaptureModeKind.AudioOnly
            ? LocalizationService.GetString("Capture_SourceLabel_Audio")
            : LocalizationService.GetString("Capture_SourceLabel_Video");
        ShowVideoQuality = SelectedMode != CaptureModeKind.AudioOnly;

        SyncHasSource();
    }

    private void SyncHasSource()
    {
        HasSource = SelectedMode switch
        {
            CaptureModeKind.FullScreen => SelectedDisplay != null,
            CaptureModeKind.Window => false,
            CaptureModeKind.Region => true,
            CaptureModeKind.AudioOnly => true,
            _ => false
        };
    }

    private void CommitDisplay(MonitorDisplay display)
    {
        var monitor = Monitors.FirstOrDefault(m => m.Handle == display.Handle) ?? display;
        _monitorService.RefreshThumbnail(monitor);
        SelectedDisplay = monitor;
        SelectedDisplayThumbnail = monitor.Thumbnail;
        SelectedDisplayBadge = monitor.BadgeLabel;
        Log.Info($"Display committed: {monitor.Name} ({monitor.Resolution}), Handle=0x{monitor.Handle:X}");
    }

    private void InitializeMonitors()
    {
        Monitors.Clear();
        foreach (var monitor in _monitorService.GetMonitors())
        {
            Monitors.Add(monitor);
        }

        // Single monitor: skip the picker and go straight to the live-area preview.
        // Multiple monitors: start from the card grid so the user explicitly picks.
        SelectedDisplay = Monitors.Count == 1 ? Monitors[0] : null;
        Log.Info($"Monitors initialized: count={Monitors.Count}, autoSelected={SelectedDisplay?.Name ?? "none"}");
    }

    partial void OnIsRecordingChanged(bool value)
    {
        RecordButtonLabel = value
            ? LocalizationService.GetString("Dock_StopRecording")
            : LocalizationService.GetString("Dock_StartRecording");
        ShowController = value;
        UpdateHeaderCopy();
    }

    [RelayCommand]
    private void SelectMode(CaptureModeKind mode) => SelectedMode = mode;

    [RelayCommand]
    private async Task ToggleRecording()
    {
        if (_recordingService.State == RecordingState.Idle)
        {
            switch (SelectedMode)
            {
                case CaptureModeKind.FullScreen:
                    if (SelectedDisplay is null)
                    {
                        Log.Info("Recording blocked: no display selected.");
                        _mainViewModel.AppStatusText = LocalizationService.GetString("AppStatus_SelectDisplay");
                        return;
                    }

                    Log.Info($"Recording requested: {SelectedDisplay.Name}, Handle=0x{SelectedDisplay.Handle:X}");
                    await _recordingService.StartFullScreenAsync(SelectedDisplay);
                    break;
                case CaptureModeKind.Window:
                    await _recordingService.PickAndStartWindowAsync();
                    break;
                case CaptureModeKind.Region:
                    await _recordingService.StartRegionAsync();
                    break;
                case CaptureModeKind.AudioOnly:
                    // Audio-only recording is not implemented yet.
                    break;
            }
        }
        else if (_recordingService.State is RecordingState.Recording or RecordingState.Paused)
        {
            await _recordingService.StopAsync();
        }
    }

    [RelayCommand]
    private void TogglePause()
    {
        if (_recordingService.State == RecordingState.Recording)
        {
            _recordingService.Pause();
        }
        else if (_recordingService.State == RecordingState.Paused)
        {
            _recordingService.Resume();
        }
    }

    private void OnRecordingStateChanged(object? sender, RecordingState state)
    {
        IsRecording = state is RecordingState.Recording or RecordingState.Paused;
        IsPaused = state == RecordingState.Paused;

        switch (state)
        {
            case RecordingState.Recording:
                _mainViewModel.AppStatusText = string.Format(
                    LocalizationService.GetString("AppStatus_Recording"),
                    ControllerSource);
                break;
            case RecordingState.Paused:
                _mainViewModel.AppStatusText = LocalizationService.GetString("Status_Paused");
                break;
            case RecordingState.Idle:
                TimerText = "00:00";
                _mainViewModel.AppStatusText = LocalizationService.GetString("Status_Ready");
                break;
        }
    }

    private void OnRecordingFailed(object? sender, string message)
    {
        _mainViewModel.AppStatusText = message;
    }

    [RelayCommand]
    private void SelectDisplay(MonitorDisplay? display)
    {
        if (display == null)
        {
            return;
        }

        CommitDisplay(display);
    }

    [RelayCommand]
    private async Task PreviewActionAsync()
    {
        if (SelectedMode != CaptureModeKind.FullScreen || Monitors.Count <= 1)
        {
            return;
        }

        _mainViewModel.AppStatusText = LocalizationService.GetString("AppStatus_ChoosingDisplay");
        foreach (var monitor in Monitors)
        {
            _monitorService.RefreshThumbnail(monitor);
        }

        var picked = await DisplayPickerDialog.ShowAsync(Monitors.ToList(), SelectedDisplay);
        _mainViewModel.AppStatusText = LocalizationService.GetString("Status_Ready");

        if (picked != null)
        {
            CommitDisplay(picked);
        }
    }

    [RelayCommand]
    private void ChooseSource()
    {
        // UI shell only.
    }

    private void UpdateHeaderCopy()
    {
        if (IsRecording)
        {
            Eyebrow = LocalizationService.GetString("Capture_Eyebrow_Recording");
            Title = LocalizationService.GetString("Capture_Title_Recording");
            Description = LocalizationService.GetString("Capture_Desc_Recording");
            return;
        }

        Eyebrow = HasSource || SelectedMode == CaptureModeKind.AudioOnly
            ? LocalizationService.GetString("Capture_Eyebrow_Ready")
            : LocalizationService.GetString("Capture_Eyebrow_SourceRequired");
        Title = HasSource || SelectedMode == CaptureModeKind.AudioOnly
            ? LocalizationService.GetString("Capture_Title_SourceReady")
            : LocalizationService.GetString("Capture_Title_Default");
        Description = HasSource || SelectedMode == CaptureModeKind.AudioOnly
            ? LocalizationService.GetString("Capture_Desc_Default")
            : LocalizationService.GetString("Capture_Desc_NoSource");
    }

    private void UpdatePreviewCopy()
    {
        ControllerSource = SelectedMode switch
        {
            CaptureModeKind.AudioOnly => LocalizationService.GetString("Capture_Source_AudioOnly"),
            CaptureModeKind.Region when HasSource => "Selected region / 1280 x 720",
            CaptureModeKind.Window when HasSource => "Visual Studio Code / 1600 x 900",
            CaptureModeKind.FullScreen when HasSource => $"{SelectedDisplay?.Name} / {SelectedDisplay?.Resolution}",
            _ => LocalizationService.GetString("Capture_Source_None")
        };

        if (SelectedMode == CaptureModeKind.AudioOnly)
        {
            PreviewLabel = LocalizationService.GetString("Capture_Preview_AudioSummary");
            PreviewCaption = LocalizationService.GetString("Capture_Preview_AudioLevels");
            ShowPreviewAction = false;
            ShowMonitorGrid = false;
            ShowDisplayPreview = false;
            ShowEmptySource = false;
            ShowRegionPreview = false;
            ShowWindowPreview = false;
            return;
        }

        ShowMonitorGrid = SelectedMode == CaptureModeKind.FullScreen && !HasSource;
        ShowDisplayPreview = SelectedMode == CaptureModeKind.FullScreen && HasSource;
        ShowEmptySource = (SelectedMode == CaptureModeKind.Window || SelectedMode == CaptureModeKind.Region) && !HasSource;
        ShowRegionPreview = SelectedMode == CaptureModeKind.Region && HasSource;
        ShowWindowPreview = SelectedMode == CaptureModeKind.Window && HasSource;

        if (SelectedMode == CaptureModeKind.FullScreen)
        {
            PreviewLabel = HasSource
                ? $"{SelectedDisplay?.Name} / {SelectedDisplay?.Resolution}"
                : LocalizationService.GetString("Capture_Preview_ChooseDisplay");
            PreviewCaption = HasSource
                ? LocalizationService.GetString("Capture_Preview_FullScreenCaption")
                : LocalizationService.GetString("Capture_Preview_SelectDisplayHint");
            ShowPreviewAction = HasSource && Monitors.Count > 1;
            PreviewActionLabel = LocalizationService.GetString("Capture_Action_ChangeDisplay");
        }
        else if (SelectedMode == CaptureModeKind.Window)
        {
            PreviewLabel = HasSource ? "Visual Studio Code / 1600 x 900" : LocalizationService.GetString("Capture_Preview_NoWindow");
            PreviewCaption = HasSource
                ? LocalizationService.GetString("Capture_Preview_WindowCaption")
                : LocalizationService.GetString("Capture_Preview_WindowPickerHint");
            ShowPreviewAction = HasSource;
            PreviewActionLabel = LocalizationService.GetString("Capture_Action_ChangeWindow");
            EmptySourceTitle = LocalizationService.GetString("Capture_EmptyWindow_Title");
            EmptySourceDescription = LocalizationService.GetString("Capture_EmptyWindow_Desc");
            EmptySourceButton = LocalizationService.GetString("Capture_Action_ChooseWindow");
        }
        else if (SelectedMode == CaptureModeKind.Region)
        {
            PreviewLabel = HasSource ? "Selected region / 1280 x 720 / X 640 / Y 310" : LocalizationService.GetString("Capture_Preview_NoRegion");
            PreviewCaption = HasSource
                ? LocalizationService.GetString("Capture_Preview_RegionCaption")
                : LocalizationService.GetString("Capture_Preview_SelectRegionFirst");
            ShowPreviewAction = HasSource;
            PreviewActionLabel = LocalizationService.GetString("Capture_Action_EditRegion");
            EmptySourceTitle = LocalizationService.GetString("Capture_EmptyRegion_Title");
            EmptySourceDescription = LocalizationService.GetString("Capture_EmptyRegion_Desc");
            EmptySourceButton = LocalizationService.GetString("Capture_Action_SelectRegion");
        }
    }
}
