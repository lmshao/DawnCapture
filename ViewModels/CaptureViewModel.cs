using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DawnCapture.Helpers;
using DawnCapture.Models;
using DawnCapture.Services;
using DawnCapture.Views;
using Microsoft.UI.Xaml.Media;
using NAudio.CoreAudioApi;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics;
using Windows.Graphics.Capture;

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
        _recordingService.RecordingNotice += OnRecordingNotice;

        DestinationFolderSummary = _mainViewModel.OutputFolderSummary;
        _mainViewModel.PropertyChanged += OnMainViewModelPropertyChanged;
        RecordButtonLabel = LocalizationService.GetString("Dock_StartRecording");
        VideoQualityLabel = LocalizationService.GetString("Dock_Quality");
        AudioQualityLabel = LocalizationService.GetString("Dock_AudioQuality");
        _settingsService.SettingsChanged += OnSettingsChanged;
        ApplyFromSettings(_settingsService.Current);
        InitializeMonitors();
        ApplySelectedMode();
        UpdateHeaderCopy();
        UpdatePreviewCopy();

        if (!HasMicrophoneDevice)
        {
            MicrophoneEnabled = false;
            MicrophoneUnavailable = true;
            Log.Info("No microphone device detected; microphone option disabled.");
        }
    }

    private readonly ISettingsService _settingsService;
    private readonly MainViewModel _mainViewModel;
    private readonly IMonitorService _monitorService;
    private readonly IRecordingService _recordingService;
    private bool _suppressSettingsSave;
    private CancellationTokenSource? _windowThumbnailCts;
    private CancellationTokenSource? _regionThumbnailCts;

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
    private bool _microphoneUnavailable;

    /// <summary>True when at least one active audio capture endpoint exists.</summary>
    public bool HasMicrophoneDevice { get; } = DetectMicrophoneDevice();

    private static bool DetectMicrophoneDevice()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).Count > 0;
        }
        catch (Exception ex)
        {
            Log.Info($"Microphone detection failed: {ex.Message}");
            return false;
        }
    }

    partial void OnShowCursorChanged(bool value)
    {
        if (_suppressSettingsSave)
        {
            return;
        }

        if (_settingsService.Current.CaptureCursor == value)
        {
            return;
        }

        _settingsService.Current.CaptureCursor = value;
        _settingsService.Save();
    }

    partial void OnQualityIndexChanged(int value)
    {
        if (_suppressSettingsSave)
        {
            return;
        }

        RecordingSettingsHelper.ApplyCaptureQualityPreset(_settingsService.Current, value);
        _settingsService.Save();
    }

    partial void OnAudioQualityIndexChanged(int value)
    {
        if (_suppressSettingsSave)
        {
            return;
        }

        _settingsService.Current.AudioQualityIndex = value;
        _settingsService.Save();
    }

    private void OnSettingsChanged(object? sender, EventArgs e) =>
        ApplyFromSettings(_settingsService.Current);

    private void ApplyFromSettings(AppSettings settings)
    {
        _suppressSettingsSave = true;
        try
        {
            ShowCursor = settings.CaptureCursor;
            QualityIndex = RecordingSettingsHelper.GetCaptureQualityIndex(settings);
            AudioQualityIndex = settings.AudioQualityIndex;
            UpdateCodecSummaries(settings.VideoCodecIndex);
        }
        finally
        {
            _suppressSettingsSave = false;
        }
    }

    private void UpdateCodecSummaries(int codecIndex)
    {
        VideoCodecSummary = codecIndex == 1
            ? LocalizationService.GetString("Dock_Codec_Hevc")
            : LocalizationService.GetString("Dock_Codec_Avc");
        AudioCodecSummary = LocalizationService.GetString("Dock_Codec_Audio");
    }

    partial void OnMicrophoneEnabledChanged(bool value)
    {
        if (value && !HasMicrophoneDevice)
        {
            MicrophoneEnabled = false;
            MicrophoneUnavailable = true;
        }
    }

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
    private RegionCaptureTarget? _selectedRegion;

    [ObservableProperty]
    private ImageSource? _regionThumbnail;

    [ObservableProperty]
    private bool _isLoadingRegionThumbnail;

    [ObservableProperty]
    private string _regionBadge = string.Empty;

    [ObservableProperty]
    private bool _showWindowPreview;

    [ObservableProperty]
    private WindowCaptureTarget? _selectedWindow;

    [ObservableProperty]
    private ImageSource? _windowThumbnail;

    [ObservableProperty]
    private bool _isLoadingWindowThumbnail;

    [ObservableProperty]
    private string _windowBadge = string.Empty;

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

    public string AudioOnlyModeLabel { get; } = LocalizationService.GetString("Capture_Mode_Audio");

    public IReadOnlyList<string> QualityOptions { get; } =
    [
        LocalizationService.GetString("Quality_Option_Compact"),
        LocalizationService.GetString("Quality_Option_Balanced"),
        LocalizationService.GetString("Quality_Option_Smooth")
    ];

    public IReadOnlyList<string> AudioQualityOptions { get; } =
    [
        LocalizationService.GetString("AudioQuality_Option_Standard"),
        LocalizationService.GetString("AudioQuality_Option_High"),
        LocalizationService.GetString("AudioQuality_Option_Best")
    ];

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
        if (value != CaptureModeKind.Window)
        {
            ClearSelectedWindow();
        }

        if (value != CaptureModeKind.Region)
        {
            ClearSelectedRegion();
        }

        ApplySelectedMode();
        UpdateHeaderCopy();
        UpdatePreviewCopy();
    }

    partial void OnSelectedRegionChanged(RegionCaptureTarget? value)
    {
        RegionBadge = value?.Resolution ?? string.Empty;
        SyncHasSource();
        UpdateHeaderCopy();
        UpdatePreviewCopy();
    }

    partial void OnSelectedWindowChanged(WindowCaptureTarget? value)
    {
        WindowBadge = value?.Resolution ?? string.Empty;
        SyncHasSource();
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
            CaptureModeKind.Window => SelectedWindow != null,
            CaptureModeKind.Region => SelectedRegion != null,
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
        UpdatePreviewCopy();
    }

    [RelayCommand]
    private void SelectMode(CaptureModeKind mode) => SelectedMode = mode;

    [RelayCommand]
    private async Task ToggleRecording()
    {
        if (_recordingService.State == RecordingState.Idle)
        {
            if (SelectedMode != CaptureModeKind.AudioOnly && !MicrophoneEnabled && !SystemAudioEnabled)
            {
                _mainViewModel.AppStatusText = LocalizationService.GetString("AppStatus_EnableOneAudio");
                return;
            }

            var audioOptions = BuildAudioOptions();

            if (_settingsService.Current.CountdownEnabled)
            {
                for (int seconds = 3; seconds >= 1; seconds--)
                {
                    _mainViewModel.AppStatusText = string.Format(
                        LocalizationService.GetString("Status_Countdown"),
                        seconds);
                    await Task.Delay(1000);
                }
            }

            switch (SelectedMode)
            {
                case CaptureModeKind.FullScreen:
                    if (SelectedDisplay is null)
                    {
                        Log.Info("Recording blocked: no display selected.");
                        _mainViewModel.AppStatusText = LocalizationService.GetString("AppStatus_SelectDisplay");
                        return;
                    }

                    Log.Info($"Recording requested: {SelectedDisplay.Name}, Handle=0x{SelectedDisplay.Handle:X}, Mic={audioOptions.EnableMicrophone}, System={audioOptions.EnableSystemAudio}");
                    await _recordingService.StartFullScreenAsync(SelectedDisplay, audioOptions);
                    break;
                case CaptureModeKind.Window:
                    if (SelectedWindow is null)
                    {
                        Log.Info("Recording blocked: no window selected.");
                        _mainViewModel.AppStatusText = LocalizationService.GetString("AppStatus_SelectWindow");
                        return;
                    }

                    Log.Info($"Recording requested: {SelectedWindow.DisplayName} ({SelectedWindow.Resolution}), Mic={audioOptions.EnableMicrophone}, System={audioOptions.EnableSystemAudio}");
                    _mainViewModel.AppStatusText = LocalizationService.GetString("AppStatus_WindowRecordingHint");
                    await _recordingService.StartWindowAsync(SelectedWindow.Item, audioOptions);
                    break;
                case CaptureModeKind.Region:
                    if (SelectedRegion is null)
                    {
                        Log.Info("Recording blocked: no region selected.");
                        _mainViewModel.AppStatusText = LocalizationService.GetString("AppStatus_SelectRegion");
                        return;
                    }

                    Log.Info($"Recording requested: region {SelectedRegion.Resolution} @({SelectedRegion.ScreenBounds.X},{SelectedRegion.ScreenBounds.Y})");
                    _mainViewModel.AppStatusText = LocalizationService.GetString("AppStatus_RegionRecordingHint");
                    await _recordingService.StartRegionAsync(SelectedRegion.ScreenBounds, audioOptions);
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

    private RecordingAudioOptions BuildAudioOptions()
    {
        return new RecordingAudioOptions
        {
            EnableMicrophone = MicrophoneEnabled,
            EnableSystemAudio = SystemAudioEnabled,
            BitrateKbps = RecordingAudioOptions.BitrateFromQualityIndex(
                _settingsService.Current.AudioQualityIndex)
        };
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

    private void OnRecordingNotice(object? sender, string message)
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
        if (SelectedMode == CaptureModeKind.Window)
        {
            await PickWindowAsync();
            return;
        }

        if (SelectedMode == CaptureModeKind.Region)
        {
            await PickRegionAsync();
            return;
        }

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
    private async Task ChooseSourceAsync()
    {
        if (SelectedMode == CaptureModeKind.Window)
        {
            await PickWindowAsync();
            return;
        }

        if (SelectedMode == CaptureModeKind.Region)
        {
            await PickRegionAsync();
        }
    }

    private async Task PickRegionAsync()
    {
        _mainViewModel.AppStatusText = LocalizationService.GetString("AppStatus_ChoosingRegion");
        var region = await RegionPickerHelper.PickRegionAsync();
        _mainViewModel.AppStatusText = LocalizationService.GetString("Status_Ready");

        if (region is null)
        {
            return;
        }

        await CommitRegionAsync(region.Value);
    }

    private async Task CommitRegionAsync(RectInt32 screenBounds)
    {
        ClearSelectedRegion();

        var target = new RegionCaptureTarget(screenBounds);
        SelectedRegion = target;
        Log.Info($"Region committed: {target.Summary}");

        _regionThumbnailCts?.Cancel();
        _regionThumbnailCts?.Dispose();
        _regionThumbnailCts = new CancellationTokenSource();
        var cts = _regionThumbnailCts;
        IsLoadingRegionThumbnail = true;
        RegionThumbnail = null;

        try
        {
            var thumbnail = await RegionPreviewHelper.CaptureThumbnailAsync(screenBounds, cancellationToken: cts.Token);
            if (!cts.IsCancellationRequested && ReferenceEquals(SelectedRegion, target))
            {
                RegionThumbnail = thumbnail;
                if (thumbnail is null)
                {
                    Log.Info($"Region preview thumbnail unavailable for {target.Summary}.");
                }
            }
        }
        finally
        {
            if (!cts.IsCancellationRequested)
            {
                IsLoadingRegionThumbnail = false;
            }
        }
    }

    private void ClearSelectedRegion()
    {
        _regionThumbnailCts?.Cancel();
        _regionThumbnailCts?.Dispose();
        _regionThumbnailCts = null;

        SelectedRegion = null;
        RegionThumbnail = null;
        RegionBadge = string.Empty;
        IsLoadingRegionThumbnail = false;
        SyncHasSource();
    }

    private async Task PickWindowAsync()
    {
        _mainViewModel.AppStatusText = LocalizationService.GetString("AppStatus_ChoosingWindow");
        var item = await WindowCaptureHelper.PickWindowAsync();
        _mainViewModel.AppStatusText = LocalizationService.GetString("Status_Ready");

        if (item is null)
        {
            return;
        }

        await CommitWindowAsync(item);
    }

    private async Task CommitWindowAsync(GraphicsCaptureItem item)
    {
        ClearSelectedWindow();

        var target = new WindowCaptureTarget(item);
        target.Closed += OnSelectedWindowClosed;
        SelectedWindow = target;
        Log.Info($"Window committed: {target.DisplayName} ({target.Resolution})");

        _windowThumbnailCts?.Cancel();
        _windowThumbnailCts?.Dispose();
        _windowThumbnailCts = new CancellationTokenSource();
        var cts = _windowThumbnailCts;
        IsLoadingWindowThumbnail = true;
        WindowThumbnail = null;

        try
        {
            var thumbnail = await WindowPreviewHelper.CaptureThumbnailAsync(item, cancellationToken: cts.Token);
            if (!cts.IsCancellationRequested && ReferenceEquals(SelectedWindow, target))
            {
                WindowThumbnail = thumbnail;
                if (thumbnail is null)
                {
                    Log.Info($"Window preview thumbnail unavailable for {target.DisplayName}.");
                }
            }
        }
        finally
        {
            if (!cts.IsCancellationRequested)
            {
                IsLoadingWindowThumbnail = false;
            }
        }
    }

    private void OnSelectedWindowClosed(object? sender, EventArgs e)
    {
        Log.Info("Selected window closed.");
        ClearSelectedWindow();
        _mainViewModel.AppStatusText = LocalizationService.GetString("AppStatus_WindowClosed");
        UpdateHeaderCopy();
        UpdatePreviewCopy();
    }

    private void ClearSelectedWindow()
    {
        _windowThumbnailCts?.Cancel();
        _windowThumbnailCts?.Dispose();
        _windowThumbnailCts = null;

        if (SelectedWindow is not null)
        {
            SelectedWindow.Closed -= OnSelectedWindowClosed;
            SelectedWindow.Dispose();
        }

        SelectedWindow = null;
        WindowThumbnail = null;
        WindowBadge = string.Empty;
        IsLoadingWindowThumbnail = false;
        SyncHasSource();
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        _mainViewModel.OpenOutputFolderCommand.Execute(null);
    }

    [RelayCommand]
    private async Task ChangeOutputFolderAsync()
    {
        await _mainViewModel.ChangeOutputFolderCommand.ExecuteAsync(null);
    }

    private void OnMainViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.OutputFolderSummary))
        {
            DestinationFolderSummary = _mainViewModel.OutputFolderSummary;
        }
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
            CaptureModeKind.Region when HasSource => SelectedRegion?.Summary ?? LocalizationService.GetString("Capture_Preview_RegionPlaceholder"),
            CaptureModeKind.Window when HasSource => $"{SelectedWindow?.DisplayName} / {SelectedWindow?.Resolution}",
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
            PreviewLabel = HasSource
                ? $"{SelectedWindow?.DisplayName} / {SelectedWindow?.Resolution}"
                : LocalizationService.GetString("Capture_Preview_NoWindow");
            PreviewCaption = HasSource
                ? LocalizationService.GetString("Capture_Preview_WindowCaption")
                : LocalizationService.GetString("Capture_Preview_WindowPickerHint");
            if (IsRecording)
            {
                PreviewCaption = LocalizationService.GetString("Capture_Preview_WindowRecordingHint");
            }
            ShowPreviewAction = HasSource;
            PreviewActionLabel = LocalizationService.GetString("Capture_Action_ChangeWindow");
            EmptySourceTitle = LocalizationService.GetString("Capture_EmptyWindow_Title");
            EmptySourceDescription = LocalizationService.GetString("Capture_EmptyWindow_Desc");
            EmptySourceButton = LocalizationService.GetString("Capture_Action_ChooseWindow");
        }
        else if (SelectedMode == CaptureModeKind.Region)
        {
            PreviewLabel = HasSource
                ? string.Format(
                    LocalizationService.GetString("Capture_Preview_RegionDetailFormat"),
                    SelectedRegion?.Resolution,
                    SelectedRegion?.ScreenBounds.X,
                    SelectedRegion?.ScreenBounds.Y)
                : LocalizationService.GetString("Capture_Preview_NoRegion");
            PreviewCaption = HasSource
                ? LocalizationService.GetString("Capture_Preview_RegionCaption")
                : LocalizationService.GetString("Capture_Preview_SelectRegionFirst");
            if (IsRecording)
            {
                PreviewCaption = LocalizationService.GetString("Capture_Preview_RegionRecordingHint");
            }
            ShowPreviewAction = HasSource;
            PreviewActionLabel = LocalizationService.GetString("Capture_Action_EditRegion");
            EmptySourceTitle = LocalizationService.GetString("Capture_EmptyRegion_Title");
            EmptySourceDescription = LocalizationService.GetString("Capture_EmptyRegion_Desc");
            EmptySourceButton = LocalizationService.GetString("Capture_Action_SelectRegion");
        }
    }
}
