using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DawnCapture.Models;
using DawnCapture.Services;
using System;
using System.Collections.Generic;

namespace DawnCapture.ViewModels;

public partial class CaptureViewModel : ObservableObject
{
    public CaptureViewModel(ISettingsService settingsService, MainViewModel mainViewModel)
    {
        _settingsService = settingsService;
        _mainViewModel = mainViewModel;
        DestinationFolderSummary = _mainViewModel.OutputFolderSummary;
        RecordButtonLabel = LocalizationService.GetString("Dock_StartRecording");
        VideoQualityLabel = LocalizationService.GetString("Dock_Quality");
        AudioQualityLabel = LocalizationService.GetString("Dock_AudioQuality");
        VideoCodecSummary = LocalizationService.GetString("Dock_Codec_Avc");
        AudioCodecSummary = LocalizationService.GetString("Dock_Codec_Audio");
        ApplySelectedMode();
        UpdateHeaderCopy();
        UpdatePreviewCopy();
    }

    private readonly ISettingsService _settingsService;
    private readonly MainViewModel _mainViewModel;

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

        HasSource = SelectedMode switch
        {
            CaptureModeKind.FullScreen => false,
            CaptureModeKind.Window => false,
            CaptureModeKind.Region => true,
            CaptureModeKind.AudioOnly => true,
            _ => false
        };
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
    private void ToggleRecording()
    {
        IsRecording = !IsRecording;
        if (IsRecording)
        {
            _mainViewModel.AppStatusText = string.Format(
                LocalizationService.GetString("AppStatus_Recording"),
                ControllerSource);
        }
        else
        {
            IsPaused = false;
            TimerText = "00:00";
            _mainViewModel.AppStatusText = LocalizationService.GetString("Status_Ready");
        }
    }

    [RelayCommand]
    private void TogglePause() => IsPaused = !IsPaused;

    [RelayCommand]
    private void PreviewAction()
    {
        // UI shell only.
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
            CaptureModeKind.FullScreen when HasSource => "Display 1 / 2560 x 1440",
            _ => LocalizationService.GetString("Capture_Source_None")
        };

        if (SelectedMode == CaptureModeKind.AudioOnly)
        {
            PreviewLabel = LocalizationService.GetString("Capture_Preview_AudioSummary");
            PreviewCaption = LocalizationService.GetString("Capture_Preview_AudioLevels");
            ShowPreviewAction = false;
            ShowMonitorGrid = false;
            ShowEmptySource = false;
            ShowRegionPreview = false;
            ShowWindowPreview = false;
            return;
        }

        ShowMonitorGrid = SelectedMode == CaptureModeKind.FullScreen && !HasSource;
        ShowEmptySource = (SelectedMode == CaptureModeKind.Window || SelectedMode == CaptureModeKind.Region) && !HasSource;
        ShowRegionPreview = SelectedMode == CaptureModeKind.Region && HasSource;
        ShowWindowPreview = SelectedMode == CaptureModeKind.Window && HasSource;

        if (SelectedMode == CaptureModeKind.FullScreen)
        {
            PreviewLabel = HasSource ? "Display 1 / 2560 x 1440" : LocalizationService.GetString("Capture_Preview_ChooseDisplay");
            PreviewCaption = HasSource
                ? LocalizationService.GetString("Capture_Preview_FullScreenCaption")
                : LocalizationService.GetString("Capture_Preview_SelectDisplayHint");
            ShowPreviewAction = HasSource;
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
