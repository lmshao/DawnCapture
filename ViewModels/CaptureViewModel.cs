using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DawnCapture.Helpers;
using DawnCapture.Models;
using DawnCapture.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics;

namespace DawnCapture.ViewModels;

public partial class CaptureViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly MainViewModel _mainViewModel;
    private readonly IMonitorService _monitorService;
    private readonly IRecordingService _recordingService;
    private bool _suppressSettingsSave;
    private CancellationTokenSource? _windowThumbnailCts;
    private CancellationTokenSource? _regionThumbnailCts;
    private DispatcherTimer? _waveformTimer;
    private DispatcherTimer? _previewTimer;
    private WindowLivePreviewSession? _windowPreviewSession;
    private double _waveformPhase;
    private bool _isForegroundActive;
    private bool _isPreviewRefreshRunning;
    private int _previewEpoch;

    private static readonly double[] DefaultWaveformHeights = AudioWaveformHelper.DefaultHeights;

    public ObservableCollection<AudioWaveformBar> AudioWaveformBars { get; } = new();

    public ObservableCollection<MonitorDisplay> Monitors { get; } = new();

    public bool HasMicrophoneDevice { get; } = AudioDeviceHelper.HasActiveCaptureDevice();

    public bool HasSystemAudioDevice { get; } = AudioDeviceHelper.HasActiveRenderDevice();

    public bool MicrophoneToggleEnabled => HasMicrophoneDevice;

    public bool SystemAudioToggleEnabled => HasSystemAudioDevice;

    public string AudioOnlyModeLabel { get; } = LocalizationService.GetString("Capture_Mode_Audio");

    public string OutputFolderFull => _mainViewModel.OutputFolderFull;

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
        PresetFlyout = CreatePresetFlyout();
        AudioQualityFlyout = CreateAudioQualityFlyout();
        _settingsService.SettingsChanged += OnSettingsChanged;
        ApplyFromSettings(_settingsService.Current);
        InitializeMonitors();
        ApplySelectedMode();
        UpdateHeaderCopy();
        UpdatePreviewCopy();
        UpdateOutputSummary();

        InitializeAudioDevices();
        InitializeWaveformBars();
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

    public void SetForegroundActive(bool value)
    {
        if (_isForegroundActive == value)
        {
            return;
        }

        _isForegroundActive = value;
        Log.Info($"Preview foreground state changed: active={value}.");
        UpdatePreviewEngine();

        // Switching back to the app should show the latest frame immediately
        // instead of waiting for the next 1s tick.
        if (value)
        {
            RequestImmediatePreviewRefresh();
        }
    }

    private bool ShouldRunPreview()
    {
        if (!_isForegroundActive || IsRecording || !HasSource)
        {
            return false;
        }

        return SelectedMode switch
        {
            CaptureModeKind.FullScreen => SelectedDisplay is not null,
            CaptureModeKind.Window => SelectedWindow is not null && !IsLoadingWindowThumbnail,
            CaptureModeKind.Region => SelectedRegion is not null && !IsLoadingRegionThumbnail,
            _ => false
        };
    }

    private void UpdatePreviewEngine()
    {
        if (ShouldRunPreview())
        {
            _previewTimer ??= CreatePreviewTimer();
            _previewTimer.Start();

            if (SelectedMode == CaptureModeKind.Window)
            {
                EnsureWindowPreviewSession();
            }

            return;
        }

        _previewTimer?.Stop();
        InvalidatePreviewEpoch();
        _isPreviewRefreshRunning = false;

        if (SelectedMode == CaptureModeKind.Window && SelectedWindow is not null)
        {
            StopWindowPreviewSession(dispose: false);
        }
        else
        {
            StopWindowPreviewSession(dispose: true);
        }
    }

    private DispatcherTimer CreatePreviewTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += OnPreviewTimerTick;
        return timer;
    }

    private async void OnPreviewTimerTick(object? sender, object e)
    {
        await RunPreviewRefreshAsync();
    }

    private void RequestImmediatePreviewRefresh()
    {
        if (_isPreviewRefreshRunning || !ShouldRunPreview())
        {
            return;
        }

        _ = RunPreviewRefreshAsync();
    }

    private async Task RunPreviewRefreshAsync()
    {
        if (_isPreviewRefreshRunning || !ShouldRunPreview())
        {
            return;
        }

        _isPreviewRefreshRunning = true;
        int epoch = _previewEpoch;
        try
        {
            await RefreshSelectedPreviewAsync(epoch);
        }
        catch (Exception ex)
        {
            Log.Debug($"Preview refresh failed: {ex.Message}");
        }
        finally
        {
            _isPreviewRefreshRunning = false;
        }
    }

    private async Task RefreshSelectedPreviewAsync(int epoch)
    {
        switch (SelectedMode)
        {
            case CaptureModeKind.FullScreen when SelectedDisplay is { } display:
            {
                await _monitorService.RefreshThumbnailAsync(display);
                if (IsPreviewStillCurrent(epoch, display))
                {
                    SelectedDisplayThumbnail = display.Thumbnail;
                }

                break;
            }
            case CaptureModeKind.Region when SelectedRegion is { } region:
            {
                var thumbnail = await RegionPreviewHelper.CaptureThumbnailAsync(region.ScreenBounds);
                if (IsPreviewStillCurrent(epoch, region))
                {
                    RegionThumbnail = thumbnail;
                }

                break;
            }
            case CaptureModeKind.Window when SelectedWindow is { } window && _windowPreviewSession is { } session:
            {
                var pixels = await session.TryExtractLatestPixelsAsync(WindowPreviewHelper.DefaultMaxWidth);

                // A just-restarted session needs a moment to deliver its first
                // frame; wait briefly so switching back shows a fresh image.
                for (int attempt = 0; pixels is null && attempt < 4 && IsPreviewStillCurrent(epoch, window); attempt++)
                {
                    await Task.Delay(60);
                    pixels = await session.TryExtractLatestPixelsAsync(WindowPreviewHelper.DefaultMaxWidth);
                }

                if (IsPreviewStillCurrent(epoch, window))
                {
                    WindowThumbnail = pixels is { } data
                        ? RecordingThumbnailHelper.CreateWriteableBitmap(data)
                        : WindowThumbnail;
                }

                break;
            }
        }
    }

    private bool IsPreviewStillCurrent(int epoch, MonitorDisplay display)
    {
        return epoch == _previewEpoch
            && SelectedMode == CaptureModeKind.FullScreen
            && ReferenceEquals(SelectedDisplay, display)
            && _isForegroundActive
            && !IsRecording;
    }

    private bool IsPreviewStillCurrent(int epoch, RegionCaptureTarget region)
    {
        return epoch == _previewEpoch
            && SelectedMode == CaptureModeKind.Region
            && ReferenceEquals(SelectedRegion, region)
            && _isForegroundActive
            && !IsRecording;
    }

    private bool IsPreviewStillCurrent(int epoch, WindowCaptureTarget window)
    {
        return epoch == _previewEpoch
            && SelectedMode == CaptureModeKind.Window
            && ReferenceEquals(SelectedWindow, window)
            && _isForegroundActive
            && !IsRecording;
    }

    private void EnsureWindowPreviewSession()
    {
        if (SelectedWindow is not { } window)
        {
            return;
        }

        _windowPreviewSession ??= new WindowLivePreviewSession();
        try
        {
            _windowPreviewSession.Start(window.Item);
        }
        catch (Exception ex)
        {
            Log.Info($"Window live preview session start failed for '{window.DisplayName}': {ex.Message}");
            StopWindowPreviewSession(dispose: true);
        }
    }

    private void StopWindowPreviewSession(bool dispose)
    {
        if (_windowPreviewSession is null)
        {
            return;
        }

        try
        {
            if (dispose)
            {
                _windowPreviewSession.Dispose();
                _windowPreviewSession = null;
            }
            else
            {
                _windowPreviewSession.Stop();
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Window live preview session stop failed: {ex.Message}");
            _windowPreviewSession = null;
        }
    }

    private void InvalidatePreviewEpoch()
    {
        _previewEpoch++;
    }

    private void StopPreviewEngineBeforeRecording()
    {
        _previewTimer?.Stop();
        InvalidatePreviewEpoch();
        _isPreviewRefreshRunning = false;
        StopWindowPreviewSession(dispose: false);
    }

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

    partial void OnIsRecordingChanged(bool value)
    {
        RecordButtonLabel = value
            ? LocalizationService.GetString("Dock_StopRecording")
            : LocalizationService.GetString("Dock_StartRecording");
        ShowController = value;
        UpdateHeaderCopy();
        UpdatePreviewCopy();
        UpdatePreviewEngine();

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

            // The recording pipeline owns its own WGC session for window capture,
            // so the live preview session must be released before starting.
            StopPreviewEngineBeforeRecording();

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

            // If recording failed to start, IsRecording never changed and the
            // preview engine would otherwise stay stopped. Re-evaluate here.
            UpdatePreviewEngine();
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
    private void OpenOutputFolder()
    {
        _mainViewModel.OpenOutputFolderCommand.Execute(null);
    }

    [RelayCommand]
    private async Task ChangeOutputFolderAsync()
    {
        await _mainViewModel.ChangeOutputFolderCommand.ExecuteAsync(null);
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
