using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DawnCapture.Helpers;
using DawnCapture.Models;
using DawnCapture.Services;
using DawnCapture.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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

        DestinationFolderSummary = BuildDestinationFolderSummary(_mainViewModel.OutputFolderFull);
        _mainViewModel.PropertyChanged += OnMainViewModelPropertyChanged;
        RecordButtonLabel = LocalizationService.GetString("Dock_StartRecording");
        PresetLabel = LocalizationService.GetString("Dock_Preset");
        AudioQualityLabel = LocalizationService.GetString("Dock_AudioQuality");
        _settingsService.SettingsChanged += OnSettingsChanged;
        ApplyFromSettings(_settingsService.Current);
        InitializeMonitors();
        ApplySelectedMode();
        UpdateHeaderCopy();
        UpdatePreviewCopy();
        UpdateOutputSummary();

        InitializeAudioDevices();
        InitializeWaveformBars();
        PresetFlyout = CreatePresetFlyout();
        AudioQualityFlyout = CreateAudioQualityFlyout();
    }

    private MenuFlyout CreatePresetFlyout()
    {
        var flyout = new MenuFlyout();
        for (int i = 0; i < PresetOptions.Count; i++)
        {
            int index = i;
            flyout.Items.Add(new MenuFlyoutItem
            {
                Text = PresetOptions[index].FullLabel,
                Command = SelectCapturePresetCommand,
                CommandParameter = index
            });
        }

        return flyout;
    }

    private MenuFlyout CreateAudioQualityFlyout()
    {
        var flyout = new MenuFlyout();
        for (int i = 0; i < AudioQualityOptions.Count; i++)
        {
            int index = i;
            flyout.Items.Add(new MenuFlyoutItem
            {
                Text = AudioQualityOptions[index].FullLabel,
                Command = SelectAudioQualityCommand,
                CommandParameter = index
            });
        }

        return flyout;
    }

    private void InitializeWaveformBars()
    {
        AudioWaveformBars.Clear();
        foreach (double height in DefaultWaveformHeights)
        {
            AudioWaveformBars.Add(new AudioWaveformBar(height));
        }
    }

    private void InitializeAudioDevices()
    {
        if (!HasMicrophoneDevice)
        {
            MicrophoneEnabled = false;
            MicrophoneRowTooltip = LocalizationService.GetString("Capture_MicrophoneUnavailable");
            Log.Info("No microphone device detected; microphone option disabled.");
        }

        if (!HasSystemAudioDevice)
        {
            SystemAudioEnabled = false;
            SystemAudioRowTooltip = LocalizationService.GetString("Capture_SystemAudioUnavailable");
            Log.Info("No playback device detected; system audio option disabled.");
        }

        UpdateAudioStatusBadges();
    }

    private readonly ISettingsService _settingsService;
    private readonly MainViewModel _mainViewModel;
    private readonly IMonitorService _monitorService;
    private readonly IRecordingService _recordingService;
    private bool _suppressSettingsSave;
    private CancellationTokenSource? _windowThumbnailCts;
    private CancellationTokenSource? _regionThumbnailCts;
    private DispatcherTimer? _waveformTimer;
    private double _waveformPhase;

    private static readonly double[] DefaultWaveformHeights = AudioWaveformHelper.DefaultHeights;

    public ObservableCollection<AudioWaveformBar> AudioWaveformBars { get; } = new();

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
    private bool _microphoneEnabled = false;

    [ObservableProperty]
    private bool _systemAudioEnabled = true;

    [ObservableProperty]
    private string? _microphoneRowTooltip;

    [ObservableProperty]
    private string? _systemAudioRowTooltip;

    [ObservableProperty]
    private string _microphoneStatusBadge = string.Empty;

    [ObservableProperty]
    private string _systemAudioStatusBadge = string.Empty;

    [ObservableProperty]
    private bool _showMicrophoneStatusBadge;

    [ObservableProperty]
    private bool _showSystemAudioStatusBadge;

    public bool HasMicrophoneDevice { get; } = AudioDeviceHelper.HasActiveCaptureDevice();

    public bool HasSystemAudioDevice { get; } = AudioDeviceHelper.HasActiveRenderDevice();

    public bool MicrophoneToggleEnabled => HasMicrophoneDevice;

    public bool SystemAudioToggleEnabled => HasSystemAudioDevice;

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

    partial void OnCapturePresetIndexChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedPresetTierLabel));

        if (_suppressSettingsSave)
        {
            return;
        }

        if (value >= 3)
        {
            return;
        }

        RecordingSettingsHelper.ApplyCaptureQualityPreset(_settingsService.Current, value);
        _settingsService.Save();
        UpdateOutputSummary();
    }

    [RelayCommand]
    private void SelectCapturePreset(int index)
    {
        if (index < 0 || index >= PresetOptions.Count)
        {
            return;
        }

        CapturePresetIndex = index;
    }

    [RelayCommand]
    private void SelectAudioQuality(int index)
    {
        if (index < 0 || index >= AudioQualityOptions.Count)
        {
            return;
        }

        AudioQualityIndex = index;
    }

    partial void OnAudioQualityIndexChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedAudioQualityTierLabel));

        if (_suppressSettingsSave)
        {
            return;
        }

        _settingsService.Current.AudioQualityIndex = value;
        _settingsService.Save();
        UpdateOutputSummary();
    }

    private void OnSettingsChanged(object? sender, EventArgs e) =>
        ApplyFromSettings(_settingsService.Current);

    private void ApplyFromSettings(AppSettings settings)
    {
        _suppressSettingsSave = true;
        try
        {
            ShowCursor = settings.CaptureCursor;
            CapturePresetIndex = RecordingSettingsHelper.ResolveCapturePresetIndex(settings);
            OnPropertyChanged(nameof(SelectedPresetTierLabel));
            AudioQualityIndex = settings.AudioQualityIndex;
            OnPropertyChanged(nameof(SelectedAudioQualityTierLabel));
            UpdateCodecSummaries(settings.VideoCodecIndex);
            UpdateOutputSummary();
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
        }

        _mainViewModel.ClearCaptureNotice();
        UpdateAudioStatusBadges();
        if (SelectedMode == CaptureModeKind.AudioOnly)
        {
            UpdatePreviewCopy();
        }
    }

    partial void OnSystemAudioEnabledChanged(bool value)
    {
        if (value && !HasSystemAudioDevice)
        {
            SystemAudioEnabled = false;
        }

        _mainViewModel.ClearCaptureNotice();
        UpdateAudioStatusBadges();
        if (SelectedMode == CaptureModeKind.AudioOnly)
        {
            UpdatePreviewCopy();
        }
    }

    private void UpdateAudioStatusBadges()
    {
        ShowMicrophoneStatusBadge = HasMicrophoneDevice;
        ShowSystemAudioStatusBadge = HasSystemAudioDevice;
        MicrophoneStatusBadge = MicrophoneEnabled
            ? LocalizationService.GetString("Capture_MicrophoneOn")
            : LocalizationService.GetString("Capture_MicrophoneOff");
        SystemAudioStatusBadge = SystemAudioEnabled
            ? LocalizationService.GetString("Capture_SystemAudioOn")
            : LocalizationService.GetString("Capture_SystemAudioOff");
    }

    private string BuildAudioSourceSummary()
    {
        bool mic = MicrophoneEnabled && HasMicrophoneDevice;
        bool sys = SystemAudioEnabled && HasSystemAudioDevice;
        if (mic && sys)
        {
            return LocalizationService.GetString("Capture_Preview_AudioSummary");
        }

        if (mic)
        {
            return LocalizationService.GetString("Capture_Preview_AudioSummary_Mic");
        }

        if (sys)
        {
            return LocalizationService.GetString("Capture_Preview_AudioSummary_System");
        }

        return LocalizationService.GetString("Capture_Preview_AudioSummary_None");
    }

    private bool HasAnyAudioSourceEnabled()
    {
        return (MicrophoneEnabled && HasMicrophoneDevice)
            || (SystemAudioEnabled && HasSystemAudioDevice);
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
    private string _presetLabel = string.Empty;

    [ObservableProperty]
    private string _audioQualityLabel = string.Empty;

    [ObservableProperty]
    private string _outputFootnoteSpecs = string.Empty;

    [ObservableProperty]
    private string _outputFootnoteHint = string.Empty;

    [ObservableProperty]
    private string _outputFootnoteTooltip = string.Empty;

    [ObservableProperty]
    private string _videoCodecSummary = string.Empty;

    [ObservableProperty]
    private string _audioCodecSummary = string.Empty;

    public IReadOnlyList<DockComboOption> PresetOptions { get; } =
    [
        new(
            LocalizationService.GetString("Preset_Tier_SmallerFiles"),
            LocalizationService.GetString("Preset_Option_SmallerFiles")),
        new(
            LocalizationService.GetString("Preset_Tier_Balanced"),
            LocalizationService.GetString("Preset_Option_Balanced")),
        new(
            LocalizationService.GetString("Preset_Tier_SmootherMotion"),
            LocalizationService.GetString("Preset_Option_SmootherMotion")),
        new(
            LocalizationService.GetString("Preset_Tier_Custom"),
            LocalizationService.GetString("Preset_Option_Custom"))
    ];

    public MenuFlyout PresetFlyout { get; }

    public string SelectedPresetTierLabel =>
        PresetOptions[Math.Clamp(CapturePresetIndex, 0, PresetOptions.Count - 1)].TierLabel;

    public IReadOnlyList<DockComboOption> AudioQualityOptions { get; } =
    [
        new(
            LocalizationService.GetString("AudioQuality_Tier_Standard"),
            LocalizationService.GetString("AudioQuality_Option_Standard")),
        new(
            LocalizationService.GetString("AudioQuality_Tier_High"),
            LocalizationService.GetString("AudioQuality_Option_High")),
        new(
            LocalizationService.GetString("AudioQuality_Tier_Best"),
            LocalizationService.GetString("AudioQuality_Option_Best"))
    ];

    public MenuFlyout AudioQualityFlyout { get; }

    public string SelectedAudioQualityTierLabel =>
        AudioQualityOptions[Math.Clamp(AudioQualityIndex, 0, AudioQualityOptions.Count - 1)].TierLabel;

    public string AudioOnlyModeLabel { get; } = LocalizationService.GetString("Capture_Mode_Audio");

    [ObservableProperty]
    private int _capturePresetIndex = 1;

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
        _mainViewModel.ClearCaptureNotice();
        SelectedDisplayThumbnail = value?.Thumbnail;
        SelectedDisplayBadge = value?.BadgeLabel ?? string.Empty;
        SyncHasSource();
        UpdateHeaderCopy();
        UpdatePreviewCopy();
        UpdateOutputSummary();
    }

    partial void OnSelectedModeChanged(CaptureModeKind value)
    {
        _mainViewModel.ClearCaptureNotice();
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
        UpdateOutputSummary();
    }

    partial void OnSelectedRegionChanged(RegionCaptureTarget? value)
    {
        _mainViewModel.ClearCaptureNotice();
        RegionBadge = value?.Resolution ?? string.Empty;
        SyncHasSource();
        UpdateHeaderCopy();
        UpdatePreviewCopy();
        UpdateOutputSummary();
    }

    partial void OnSelectedWindowChanged(WindowCaptureTarget? value)
    {
        _mainViewModel.ClearCaptureNotice();
        WindowBadge = value?.Resolution ?? string.Empty;
        SyncHasSource();
        UpdateHeaderCopy();
        UpdatePreviewCopy();
        UpdateOutputSummary();
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
        UpdateOutputSummary();
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

        if (value && SelectedMode == CaptureModeKind.AudioOnly)
        {
            StartWaveformAnimation();
        }
        else if (!value)
        {
            StopWaveformAnimation();
        }
    }

    partial void OnIsPausedChanged(bool value)
    {
        UpdatePreviewCopy();
    }

    private void StartWaveformAnimation()
    {
        if (SelectedMode != CaptureModeKind.AudioOnly)
        {
            return;
        }

        _waveformPhase = 0;
        _waveformTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _waveformTimer.Tick -= OnWaveformTimerTick;
        _waveformTimer.Tick += OnWaveformTimerTick;
        _waveformTimer.Start();
    }

    private void StopWaveformAnimation()
    {
        if (_waveformTimer is not null)
        {
            _waveformTimer.Tick -= OnWaveformTimerTick;
            _waveformTimer.Stop();
        }

        ResetWaveformBars();
    }

    private void ResetWaveformBars()
    {
        for (int i = 0; i < AudioWaveformBars.Count && i < DefaultWaveformHeights.Length; i++)
        {
            AudioWaveformBars[i].Height = DefaultWaveformHeights[i];
        }
    }

    private void OnWaveformTimerTick(object? sender, object e)
    {
        if (SelectedMode != CaptureModeKind.AudioOnly || !IsRecording)
        {
            StopWaveformAnimation();
            return;
        }

        double peak = IsPaused ? 0 : _recordingService.AudioMeterLevel;
        _waveformPhase += 0.22;

        for (int i = 0; i < AudioWaveformBars.Count; i++)
        {
            double wobble = 0.4 + (0.6 * Math.Abs(Math.Sin(_waveformPhase + (i * 0.65))));
            double target = IsPaused
                ? DefaultWaveformHeights[i] * 0.35
                : Math.Max(8, (peak * 77 * wobble) + (peak > 0.02 ? 0 : DefaultWaveformHeights[i] * 0.18));

            AudioWaveformBar bar = AudioWaveformBars[i];
            bar.Height += (target - bar.Height) * 0.35;
        }
    }

    [RelayCommand]
    private void SelectMode(CaptureModeKind mode) => SelectedMode = mode;

    [RelayCommand]
    private async Task ToggleRecording()
    {
        if (_recordingService.State == RecordingState.Idle)
        {
            if (SelectedMode == CaptureModeKind.AudioOnly)
            {
                if (!HasMicrophoneDevice && !HasSystemAudioDevice)
                {
                    await BlockRecordingStartAsync(LocalizationService.GetString("AppStatus_NoAudioDevices"));
                    return;
                }

                if (!HasAnyAudioSourceEnabled())
                {
                    await BlockRecordingStartAsync(LocalizationService.GetString("AppStatus_EnableOneAudio"));
                    return;
                }
            }

            string outputFolder = OutputFolderHelper.Resolve(_settingsService);
            if (!StorageSpaceHelper.TryEnsureSpaceForRecording(outputFolder, out string? storageError))
            {
                await BlockRecordingStartAsync(storageError!);
                return;
            }

            if (!await TryValidateRecordingSourceAsync())
            {
                return;
            }

            var audioOptions = BuildAudioOptions();

            if (_settingsService.Current.CountdownEnabled)
            {
                RectInt32 countdownBounds = CountdownOverlayHelper.ResolveTargetBounds(
                    SelectedMode,
                    SelectedDisplay,
                    SelectedWindow,
                    SelectedRegion,
                    _monitorService);
                bool proceed = await CountdownOverlayHelper.RunAsync(
                    countdownBounds,
                    CountdownOverlayHelper.DefaultSeconds);
                if (!proceed)
                {
                    ShowCaptureNotice(
                        LocalizationService.GetString("Status_CountdownCancelled"),
                        CaptureNoticeSeverity.Information);
                    return;
                }
            }

            switch (SelectedMode)
            {
                case CaptureModeKind.FullScreen:
                    Log.Info($"Recording requested: {SelectedDisplay!.Name}, Handle=0x{SelectedDisplay.Handle:X}, Mic={audioOptions.EnableMicrophone}, System={audioOptions.EnableSystemAudio}");
                    await _recordingService.StartFullScreenAsync(SelectedDisplay, audioOptions);
                    break;
                case CaptureModeKind.Window:
                    Log.Info($"Recording requested: {SelectedWindow!.DisplayName} ({SelectedWindow.Resolution}), Mic={audioOptions.EnableMicrophone}, System={audioOptions.EnableSystemAudio}");
                    await _recordingService.StartWindowAsync(SelectedWindow.Item, audioOptions);
                    break;
                case CaptureModeKind.Region:
                    Log.Info($"Recording requested: region {SelectedRegion!.Resolution} @({SelectedRegion.ScreenBounds.X},{SelectedRegion.ScreenBounds.Y})");
                    await _recordingService.StartRegionAsync(SelectedRegion.ScreenBounds, audioOptions);
                    break;
                case CaptureModeKind.AudioOnly:
                    Log.Info($"Recording requested: audio-only, Mic={audioOptions.EnableMicrophone}, System={audioOptions.EnableSystemAudio}");
                    await _recordingService.StartAudioOnlyAsync(audioOptions);
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
            EnableMicrophone = MicrophoneEnabled && HasMicrophoneDevice,
            EnableSystemAudio = SystemAudioEnabled && HasSystemAudioDevice,
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
                _mainViewModel.ClearCaptureNotice();
                break;
            case RecordingState.Idle:
                TimerText = "00:00";
                break;
        }
    }

    private void OnRecordingFailed(object? sender, string message)
    {
        _mainViewModel.ShowCaptureNotice(message, CaptureNoticeSeverity.Error);
        _ = DialogHelper.ShowErrorAsync(
            message,
            LocalizationService.GetString("Recordings_ErrorTitle"));
    }

    private void OnRecordingNotice(object? sender, string message)
    {
        ShowCaptureNotice(message, CaptureNoticeSeverity.Warning);
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

        foreach (var monitor in Monitors)
        {
            _monitorService.RefreshThumbnail(monitor);
        }

        var picked = await DisplayPickerDialog.ShowAsync(Monitors.ToList(), SelectedDisplay);

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
        var region = await RegionPickerHelper.PickRegionAsync();

        if (region is null)
        {
            return;
        }

        await CommitRegionAsync(region.Value);
    }

    private async Task CommitRegionAsync(RectInt32 screenBounds)
    {
        ClearSelectedRegion();

        var normalized = RegionBoundsHelper.NormalizeForEncoding(screenBounds);
        if (normalized.Width != screenBounds.Width || normalized.Height != screenBounds.Height)
        {
            Log.Info(
                $"Region normalized for encoding: {screenBounds.Width}x{screenBounds.Height} -> {normalized.Width}x{normalized.Height}");
        }

        var target = new RegionCaptureTarget(normalized);
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
            var thumbnail = await RegionPreviewHelper.CaptureThumbnailAsync(normalized, cancellationToken: cts.Token);
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
        var item = await WindowCaptureHelper.PickWindowAsync();

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
        ShowCaptureNotice(
            LocalizationService.GetString("AppStatus_WindowClosed"),
            CaptureNoticeSeverity.Warning);
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
        if (e.PropertyName is nameof(MainViewModel.OutputFolderSummary) or nameof(MainViewModel.OutputFolderFull))
        {
            DestinationFolderSummary = BuildDestinationFolderSummary(_mainViewModel.OutputFolderFull);
        }
    }

    private static string BuildDestinationFolderSummary(string folder) =>
        PathDisplayHelper.CompactDockFolderSummary(
            folder,
            OutputFolderHelper.DefaultPath,
            LocalizationService.GetString("OutputFolder_DefaultSummary"));

    private void UpdateOutputSummary()
    {
        var settings = _settingsService.Current;
        int captureIndex = RecordingSettingsHelper.ResolveCapturePresetIndex(settings);
        int audioKbps = RecordingAudioOptions.BitrateFromQualityIndex(settings.AudioQualityIndex);

        if (SelectedMode == CaptureModeKind.AudioOnly)
        {
            OutputFootnoteSpecs = RecordingOutputSummaryHelper.FormatAudioSpecs(audioKbps);
            OutputFootnoteHint = " · " + InlineAudioQualityHint(settings.AudioQualityIndex);
            OutputFootnoteTooltip = OutputFootnoteSpecs + OutputFootnoteHint;
            return;
        }

        string? resolution = GetCaptureResolution();
        if (string.IsNullOrEmpty(resolution))
        {
            OutputFootnoteSpecs = LocalizationService.GetString("Dock_Output_SelectSource");
            OutputFootnoteHint = string.Empty;
            OutputFootnoteTooltip = OutputFootnoteSpecs;
            return;
        }

        string codec = RecordingSettingsHelper.VideoCodecLabel(settings.VideoCodecIndex);
        (int Width, int Height)? dimensions = GetCaptureDimensions();
        int width = dimensions?.Width ?? RecordingSettingsHelper.ReferenceWidth;
        int height = dimensions?.Height ?? RecordingSettingsHelper.ReferenceHeight;
        int effectiveVideoKbps = RecordingSettingsHelper.ResolvePreviewVideoBitrateKbps(settings, width, height);
        bool approximateVideoBitrate = RecordingSettingsHelper.UsesApproximateVideoBitrate(settings);

        OutputFootnoteSpecs = RecordingOutputSummaryHelper.FormatVideoSpecs(
            codec,
            resolution,
            width: null,
            height: null,
            settings.FrameRate,
            effectiveVideoKbps,
            audioKbps,
            includeAudio: true,
            approximateVideoBitrate: approximateVideoBitrate);
        OutputFootnoteHint = " · " + InlinePresetHint(captureIndex);
        OutputFootnoteTooltip = OutputFootnoteSpecs + OutputFootnoteHint;
    }

    private string? GetCaptureResolution() => SelectedMode switch
    {
        CaptureModeKind.FullScreen => SelectedDisplay?.Resolution,
        CaptureModeKind.Window => SelectedWindow?.Resolution,
        CaptureModeKind.Region => SelectedRegion?.Resolution,
        _ => null
    };

    private (int Width, int Height)? GetCaptureDimensions() => SelectedMode switch
    {
        CaptureModeKind.FullScreen when SelectedDisplay is { } display =>
            (display.Width, display.Height),
        CaptureModeKind.Window when SelectedWindow is { } window =>
            (window.Width, window.Height),
        CaptureModeKind.Region when SelectedRegion is { } region =>
            (region.Width, region.Height),
        _ => null
    };

    private static string InlinePresetHint(int captureIndex)
    {
        string text = captureIndex switch
        {
            0 => LocalizationService.GetString("Preset_Friendly_SmallerFiles"),
            2 => LocalizationService.GetString("Preset_Friendly_SmootherMotion"),
            3 => LocalizationService.GetString("Preset_Friendly_Custom"),
            _ => LocalizationService.GetString("Preset_Friendly_Balanced")
        };

        return InlineHintPhrase(text);
    }

    private static string InlineAudioQualityHint(int audioQualityIndex)
    {
        string text = audioQualityIndex switch
        {
            0 => LocalizationService.GetString("AudioQuality_Option_Standard_Short"),
            2 => LocalizationService.GetString("AudioQuality_Option_Best_Short"),
            _ => LocalizationService.GetString("AudioQuality_Option_High_Short")
        };

        return InlineHintPhrase(text);
    }

    private static string InlineHintPhrase(string text) =>
        text.Replace(" · ", LocalizationService.GetString("Dock_Output_HintSeparator"), StringComparison.Ordinal);

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
            PreviewLabel = BuildAudioSourceSummary();
            PreviewCaption = IsRecording
                ? IsPaused
                    ? LocalizationService.GetString("Status_Paused")
                    : LocalizationService.GetString("Capture_Preview_AudioRecording")
                : LocalizationService.GetString("Capture_Preview_AudioLevels");
            ShowPreviewAction = false;
            ShowMonitorGrid = false;
            ShowDisplayPreview = false;
            ShowEmptySource = false;
            ShowRegionPreview = false;
            ShowWindowPreview = false;
            UpdateAudioStatusBadges();
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

    private async Task<bool> TryValidateRecordingSourceAsync()
    {
        switch (SelectedMode)
        {
            case CaptureModeKind.FullScreen when SelectedDisplay is null:
                Log.Info("Recording blocked: no display selected.");
                await BlockRecordingStartAsync(LocalizationService.GetString("AppStatus_SelectDisplay"));
                return false;
            case CaptureModeKind.Window when SelectedWindow is null:
                Log.Info("Recording blocked: no window selected.");
                await BlockRecordingStartAsync(LocalizationService.GetString("AppStatus_SelectWindow"));
                return false;
            case CaptureModeKind.Region when SelectedRegion is null:
                Log.Info("Recording blocked: no region selected.");
                await BlockRecordingStartAsync(LocalizationService.GetString("AppStatus_SelectRegion"));
                return false;
            default:
                return true;
        }
    }

    private async Task BlockRecordingStartAsync(string message)
    {
        _mainViewModel.ShowCaptureNotice(message, CaptureNoticeSeverity.Error);
        await DialogHelper.ShowErrorAsync(
            message,
            LocalizationService.GetString("Capture_Validation_Title"));
    }

    private void ShowCaptureNotice(string message, CaptureNoticeSeverity severity)
    {
        _mainViewModel.ShowCaptureNotice(message, severity);
    }

}
