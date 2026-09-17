using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using DawnCapture.Helpers;
using DawnCapture.Models;
using DawnCapture.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System.Threading.Tasks;

namespace DawnCapture.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly MainViewModel _mainViewModel;
    private readonly IGlobalHotkeyService _globalHotkeyService;
    private readonly IRecordingService _recordingService;

    public SettingsViewModel(
        ISettingsService settingsService,
        MainViewModel mainViewModel,
        IGlobalHotkeyService globalHotkeyService,
        IRecordingService recordingService)
    {
        _settingsService = settingsService;
        _mainViewModel = mainViewModel;
        _globalHotkeyService = globalHotkeyService;
        _recordingService = recordingService;
        _outputFolder = _settingsService.Current.OutputFolder;
        _outputFolderDisplay = PathDisplayHelper.MiddleEllipsis(_outputFolder, 44);
        _frameRateIndex = RecordingSettingsHelper.FrameRateToSettingsIndex(_settingsService.Current.FrameRate);
        NormalizeStoredFrameRateIfNeeded();
        _qualityIndex = _settingsService.Current.QualityIndex;
        _bitrateModeIndex = (int)_settingsService.Current.BitrateMode;
        _fixedBitrateMbps = _settingsService.Current.BitrateKbps / 1000.0;
        _codecIndex = _settingsService.Current.VideoCodecIndex;
        _audioQualityIndex = _settingsService.Current.AudioQualityIndex;
        _captureCursor = _settingsService.Current.CaptureCursor;
        _countdownSeconds = _settingsService.Current.CountdownSeconds;
        if (_countdownSeconds > 0)
        {
            // Switching it off and on again should give back what the user had.
            _lastCountdownSeconds = _countdownSeconds;
        }
        _notificationEnabled = _settingsService.Current.NotificationEnabled;
        _segmentEnabled = _settingsService.Current.SegmentEnabled;
        _segmentLimitModeIndex = Math.Clamp((int)_settingsService.Current.SegmentLimitMode, 0, 1);
        _segmentDurationIndex = NearestPresetIndex(SegmentDurationPresetMinutes, _settingsService.Current.SegmentMinutes);
        _segmentSizeIndex = NearestPresetIndex(SegmentSizePresetMb, _settingsService.Current.SegmentSizeMb);
        _maxDurationIndex = NearestPresetIndex(MaxDurationPresetMinutes, _settingsService.Current.MaxRecordingMinutes);
        _closeMainWindowActionIndex = (int)_settingsService.Current.CloseMainWindowAction;
        _selectedLanguage = Languages.FirstOrDefault(
            option => option.Code == _settingsService.Current.Language) ?? Languages[0];

        _mainViewModel.OutputFolderChanged += (_, path) => OutputFolder = path;
        _settingsService.SettingsChanged += OnExternalSettingsChanged;
        _recordingService.StateChanged += OnRecordingStateChanged;
        IsRecording = _recordingService.State is RecordingState.Recording or RecordingState.Paused;
        RefreshHotkeyDisplays();
        RefreshHotkeyRegistrationWarning();
        RefreshAudioDevices();
    }

    private bool _suppressPersist;
    private bool _suppressLanguageChange;

    private void OnExternalSettingsChanged(object? sender, EventArgs e)
    {
        _suppressPersist = true;
        _suppressLanguageChange = true;
        try
        {
            var settings = _settingsService.Current;
            OutputFolder = settings.OutputFolder;
            FrameRateIndex = RecordingSettingsHelper.FrameRateToSettingsIndex(settings.FrameRate);
            QualityIndex = settings.QualityIndex;
            BitrateModeIndex = (int)settings.BitrateMode;
            FixedBitrateMbps = settings.BitrateKbps / 1000.0;
            CodecIndex = settings.VideoCodecIndex;
            AudioQualityIndex = settings.AudioQualityIndex;
            CaptureCursor = settings.CaptureCursor;
            CountdownSeconds = settings.CountdownSeconds;
            NotificationEnabled = settings.NotificationEnabled;
            SegmentEnabled = settings.SegmentEnabled;
            SegmentLimitModeIndex = Math.Clamp((int)settings.SegmentLimitMode, 0, 1);
            SegmentDurationIndex = NearestPresetIndex(SegmentDurationPresetMinutes, settings.SegmentMinutes);
            SegmentSizeIndex = NearestPresetIndex(SegmentSizePresetMb, settings.SegmentSizeMb);
            MaxDurationIndex = NearestPresetIndex(MaxDurationPresetMinutes, settings.MaxRecordingMinutes);
            CloseMainWindowActionIndex = (int)settings.CloseMainWindowAction;
            SelectedLanguage = Languages.FirstOrDefault(option => option.Code == settings.Language) ?? Languages[0];
            RefreshAudioDevices();
            RefreshHotkeyDisplays();
            RefreshHotkeyRegistrationWarning();
        }
        finally
        {
            _suppressLanguageChange = false;
            _suppressPersist = false;
        }
    }

    public IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new(LocalizationService.SystemLanguage, LocalizationService.GetString("Language_System")),
        new(LocalizationService.SimplifiedChinese, LocalizationService.SimplifiedChineseDisplayName),
        new(LocalizationService.English, LocalizationService.EnglishDisplayName)
    ];

    [ObservableProperty]
    private string _outputFolder;

    [ObservableProperty]
    private string _outputFolderDisplay;

    [ObservableProperty]
    private int _frameRateIndex;

    [ObservableProperty]
    private int _qualityIndex = 1;

    [ObservableProperty]
    private int _bitrateModeIndex;

    [ObservableProperty]
    private double _fixedBitrateMbps = 8;

    public bool IsAdaptiveBitrateMode => BitrateModeIndex == (int)Models.VideoBitrateMode.Adaptive;

    [ObservableProperty]
    private int _codecIndex;

    [ObservableProperty]
    private int _audioQualityIndex;

    [ObservableProperty]
    private bool _captureCursor;

    /// <summary>Seconds before recording starts; 0 means no countdown. Bound to a number box.</summary>
    [ObservableProperty]
    private double _countdownSeconds;

    /// <summary>What the switch turns on with when nothing was remembered.</summary>
    private const double DefaultCountdownSeconds = 3;

    /// <summary>Shortest countdown the number box accepts; 0 is reserved for "switched off".</summary>
    private const double MinCountdownSeconds = 1;

    /// <summary>
    /// Set while the switch itself writes the "off" value, which is the only time 0 may be stored:
    /// a 0 typed into the number box means the user went below the minimum, not that the countdown
    /// should switch off.
    /// </summary>
    private bool _switchTurningCountdownOff;

    /// <summary>
    /// Last seconds the user had before switching the countdown off. Deliberately in memory only:
    /// the settings file keeps exactly one value (0 = off), so "on" and a stored 0 can never
    /// disagree, and a restart simply starts switched off again.
    /// </summary>
    private double _lastCountdownSeconds = DefaultCountdownSeconds;

    /// <summary>
    /// The enable switch, which is a view of <see cref="CountdownSeconds"/> rather than a stored
    /// flag: one value answers both "whether" and "how long", so the two can never disagree.
    /// </summary>
    public bool CountdownEnabled
    {
        get => CountdownSeconds > 0;
        set
        {
            if (value == CountdownEnabled)
            {
                return;
            }

            if (value)
            {
                CountdownSeconds = _lastCountdownSeconds > 0
                    ? _lastCountdownSeconds
                    : DefaultCountdownSeconds;
                return;
            }

            _lastCountdownSeconds = CountdownSeconds;
            _switchTurningCountdownOff = true;
            try
            {
                CountdownSeconds = 0;
            }
            finally
            {
                _switchTurningCountdownOff = false;
            }
        }
    }

    public bool ShowCountdownSeconds => CountdownEnabled;

    [ObservableProperty]
    private bool _notificationEnabled = true;

    [ObservableProperty]
    private bool _segmentEnabled = false;

    [ObservableProperty]
    private int _segmentLimitModeIndex;

    [ObservableProperty]
    private int _segmentDurationIndex = 3;

    [ObservableProperty]
    private int _segmentSizeIndex = 2;

    [ObservableProperty]
    private int _maxDurationIndex;

    /// <summary>Minutes per segment; the first entry is the quick-test value.</summary>
    private static readonly int[] SegmentDurationPresetMinutes = [1, 30, 60, 120, 240, 480];

    /// <summary>Megabytes per segment; 64 MB is the quick-test value, 4096 is the FAT32 ceiling.</summary>
    private static readonly int[] SegmentSizePresetMb = [64, 512, 2048, 4096];

    /// <summary>Minutes; 0 means no limit.</summary>
    private static readonly int[] MaxDurationPresetMinutes = [0, 1, 30, 60, 120, 240, 480];

    public bool ShowSegmentOptions => SegmentEnabled;

    public bool ShowSegmentDuration =>
        SegmentEnabled && SegmentLimitModeIndex == (int)Models.SegmentLimitMode.Duration;

    public bool ShowSegmentSize =>
        SegmentEnabled && SegmentLimitModeIndex == (int)Models.SegmentLimitMode.Size;

    public int SegmentDurationMinutes => PresetAt(SegmentDurationPresetMinutes, SegmentDurationIndex);

    public int MaxDurationMinutes => PresetAt(MaxDurationPresetMinutes, MaxDurationIndex);

    public bool ShowSegmentConflict =>
        SegmentEnabled
        && SegmentLimitModeIndex == (int)Models.SegmentLimitMode.Duration
        && MaxDurationMinutes > 0
        && SegmentDurationMinutes > MaxDurationMinutes;

    private static int PresetAt(int[] presets, int index) =>
        presets[Math.Clamp(index, 0, presets.Length - 1)];

    private static int NearestPresetIndex(int[] presets, int value)
    {
        int best = 0;
        int bestDelta = int.MaxValue;
        for (int i = 0; i < presets.Length; i++)
        {
            int delta = Math.Abs(presets[i] - value);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = i;
            }
        }

        return best;
    }

    [ObservableProperty]
    private bool _isRecording;

    public bool CanResetSettings => !IsRecording;

    partial void OnIsRecordingChanged(bool value) => OnPropertyChanged(nameof(CanResetSettings));

    [ObservableProperty]
    private int _closeMainWindowActionIndex;

    [ObservableProperty]
    private LanguageOption _selectedLanguage = null!;

    [ObservableProperty]
    private HotkeyCaptureTarget _hotkeyCaptureTarget;

    [ObservableProperty]
    private string _toggleRecordingHotkeyDisplay = string.Empty;

    [ObservableProperty]
    private string _togglePauseHotkeyDisplay = string.Empty;

    [ObservableProperty]
    private string? _hotkeyErrorMessage;

    [ObservableProperty]
    private string? _hotkeyRegistrationWarning;

    public bool HasHotkeyErrorMessage => !string.IsNullOrEmpty(HotkeyErrorMessage);

    public bool HasHotkeyRegistrationWarning => !string.IsNullOrEmpty(HotkeyRegistrationWarning);

    partial void OnHotkeyErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasHotkeyErrorMessage));

    partial void OnHotkeyRegistrationWarningChanged(string? value) => OnPropertyChanged(nameof(HasHotkeyRegistrationWarning));

    partial void OnOutputFolderChanged(string value)
    {
        OutputFolderDisplay = PathDisplayHelper.MiddleEllipsis(value, 44);
        if (_suppressPersist ||
            string.Equals(value, _settingsService.Current.OutputFolder, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        PersistSettings();
    }

    partial void OnFrameRateIndexChanged(int value)
    {
        if (!_suppressPersist)
        {
            PersistSettings();
        }
    }

    partial void OnQualityIndexChanged(int value)
    {
        if (!_suppressPersist)
        {
            PersistSettings();
        }
    }

    partial void OnBitrateModeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsAdaptiveBitrateMode));
        if (_suppressPersist)
        {
            return;
        }

        if (value == (int)Models.VideoBitrateMode.Fixed)
        {
            FixedBitrateMbps = _settingsService.Current.BitrateKbps / 1000.0;
        }

        PersistSettings();
    }

    partial void OnFixedBitrateMbpsChanged(double value)
    {
        if (_suppressPersist || BitrateModeIndex != (int)Models.VideoBitrateMode.Fixed)
        {
            return;
        }

        PersistSettings();
    }

    partial void OnCodecIndexChanged(int value)
    {
        if (!_suppressPersist)
        {
            PersistSettings();
        }
    }

    partial void OnAudioQualityIndexChanged(int value)
    {
        if (!_suppressPersist)
        {
            PersistSettings();
        }
    }

    partial void OnCaptureCursorChanged(bool value)
    {
        if (!_suppressPersist)
        {
            PersistSettings();
        }
    }

    partial void OnCountdownSecondsChanged(double value)
    {
        if (double.IsNaN(value))
        {
            // The box reports NaN while its text is empty; the smallest allowed value is the
            // closest thing to what the user meant.
            CountdownSeconds = MinCountdownSeconds;
            return;
        }

        // Outside 1-60 the value is pulled back into range - the hint under the number box says
        // so. Only the switch writes a 0, and that is what turns the countdown off.
        double normalized = _switchTurningCountdownOff
            ? 0
            : Math.Clamp(Math.Round(value), MinCountdownSeconds, AppSettings.MaxCountdownSeconds);
        if (normalized != value)
        {
            CountdownSeconds = normalized;
            return;
        }

        // The switch and the seconds row are derived from this value.
        OnPropertyChanged(nameof(CountdownEnabled));
        OnPropertyChanged(nameof(ShowCountdownSeconds));

        if (!_suppressPersist)
        {
            PersistSettings();
        }
    }

    partial void OnNotificationEnabledChanged(bool value)
    {
        if (!_suppressPersist)
        {
            PersistSettings();
        }
    }

    partial void OnSegmentEnabledChanged(bool value)
    {
        RaiseSegmentLayout();
        if (!_suppressPersist)
        {
            PersistSettings();
        }
    }

    partial void OnSegmentLimitModeIndexChanged(int value)
    {
        RaiseSegmentLayout();
        if (!_suppressPersist)
        {
            PersistSettings();
        }
    }

    partial void OnSegmentDurationIndexChanged(int value)
    {
        OnPropertyChanged(nameof(SegmentDurationMinutes));
        OnPropertyChanged(nameof(ShowSegmentConflict));
        if (!_suppressPersist)
        {
            PersistSettings();
        }
    }

    partial void OnSegmentSizeIndexChanged(int value)
    {
        if (!_suppressPersist)
        {
            PersistSettings();
        }
    }

    partial void OnMaxDurationIndexChanged(int value)
    {
        OnPropertyChanged(nameof(MaxDurationMinutes));
        OnPropertyChanged(nameof(ShowSegmentConflict));
        if (!_suppressPersist)
        {
            PersistSettings();
        }
    }

    private void RaiseSegmentLayout()
    {
        OnPropertyChanged(nameof(ShowSegmentOptions));
        OnPropertyChanged(nameof(ShowSegmentDuration));
        OnPropertyChanged(nameof(ShowSegmentSize));
        OnPropertyChanged(nameof(ShowSegmentConflict));
    }

    partial void OnCloseMainWindowActionIndexChanged(int value)
    {
        if (!_suppressPersist)
        {
            PersistSettings();
        }
    }

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        if (value is null || _suppressLanguageChange)
        {
            return;
        }

        string previousCode = _settingsService.Current.Language;
        if (previousCode == value.Code)
        {
            return;
        }

        _ = ConfirmLanguageChangeAsync(previousCode, value);
    }

    private async Task ConfirmLanguageChangeAsync(string previousCode, LanguageOption selected)
    {
        bool confirmed = await DialogHelper.ShowConfirmAsync(
            LocalizationService.GetString("Settings_LanguageRestart_Message"),
            LocalizationService.GetString("Settings_LanguageRestart_Title"),
            confirmText: LocalizationService.GetString("Settings_LanguageRestart_Confirm"),
            cancelText: ContentDialogHelper.CancelText);

        if (!confirmed)
        {
            RevertSelectedLanguage(previousCode);
            return;
        }

        _settingsService.Current.Language = selected.Code;
        if (!_settingsService.Save())
        {
            _ = DialogHelper.ShowErrorAsync(LocalizationService.GetString("Settings_SaveFailed"));
            RevertSelectedLanguage(previousCode);
            return;
        }

        LocalizationService.ApplyLanguage(selected.Code);
        await RestartApplicationAsync();
    }

    private void RevertSelectedLanguage(string languageCode)
    {
        _suppressLanguageChange = true;
        SelectedLanguage = Languages.FirstOrDefault(option => option.Code == languageCode) ?? Languages[0];
        _suppressLanguageChange = false;
    }

    [RelayCommand]
    private void BeginCaptureToggleRecordingHotkey()
    {
        BeginHotkeyCapture(HotkeyCaptureTarget.ToggleRecording);
    }

    [RelayCommand]
    private void BeginCaptureTogglePauseHotkey()
    {
        BeginHotkeyCapture(HotkeyCaptureTarget.TogglePause);
    }

    public void CancelHotkeyCapture()
    {
        if (HotkeyCaptureTarget == HotkeyCaptureTarget.None)
        {
            return;
        }

        HotkeyCaptureTarget = HotkeyCaptureTarget.None;
        HotkeyErrorMessage = null;
        ResumeHotkeyBindings();
        RefreshHotkeyDisplays();
    }

    public bool TryApplyCapturedHotkey(HotkeyBinding binding)
    {
        if (HotkeyCaptureTarget == HotkeyCaptureTarget.None)
        {
            return false;
        }

        HotkeyBinding currentBinding = GetCurrentCaptureTargetBinding();
        if (binding.Equals(currentBinding))
        {
            FinishHotkeyCapture();
            return true;
        }

        HotkeyBinding otherBinding = HotkeyCaptureTarget == HotkeyCaptureTarget.ToggleRecording
            ? _settingsService.Current.HotkeyTogglePause
            : _settingsService.Current.HotkeyToggleRecording;

        string? validationError = HotkeyHelper.ValidateForSettings(
            binding,
            otherBinding,
            candidate => _globalHotkeyService.Probe(candidate, HotkeyCaptureTarget));

        if (validationError is not null)
        {
            HotkeyErrorMessage = validationError;
            return true;
        }

        if (HotkeyCaptureTarget == HotkeyCaptureTarget.ToggleRecording)
        {
            _settingsService.Current.HotkeyToggleRecording = binding;
        }
        else
        {
            _settingsService.Current.HotkeyTogglePause = binding;
        }

        _settingsService.Save();
        FinishHotkeyCapture();
        RefreshHotkeyRegistrationWarning();
        return true;
    }

    private void BeginHotkeyCapture(HotkeyCaptureTarget target)
    {
        HotkeyCaptureTarget = target;
        HotkeyErrorMessage = null;
        _globalHotkeyService.SuspendForCapture();
        RefreshHotkeyDisplays();
    }

    private void FinishHotkeyCapture()
    {
        HotkeyCaptureTarget = HotkeyCaptureTarget.None;
        HotkeyErrorMessage = null;
        ResumeHotkeyBindings();
        RefreshHotkeyDisplays();
    }

    private void ResumeHotkeyBindings()
    {
        _globalHotkeyService.Apply(
            _settingsService.Current.HotkeyToggleRecording,
            _settingsService.Current.HotkeyTogglePause);
    }

    private HotkeyBinding GetCurrentCaptureTargetBinding() =>
        HotkeyCaptureTarget == HotkeyCaptureTarget.ToggleRecording
            ? _settingsService.Current.HotkeyToggleRecording
            : _settingsService.Current.HotkeyTogglePause;

    public void RefreshHotkeyRegistrationWarning()
    {
        var settings = _settingsService.Current;
        var issues = new List<string>();

        if (!settings.HotkeyToggleRecording.IsEmpty && !_globalHotkeyService.IsToggleRecordingRegistered)
        {
            issues.Add(string.Format(
                LocalizationService.GetString("Settings_Hotkey_Warning_Item"),
                LocalizationService.GetString("Settings_Hotkey_ToggleRecording"),
                HotkeyHelper.FormatDisplay(settings.HotkeyToggleRecording)));
        }

        if (!settings.HotkeyTogglePause.IsEmpty && !_globalHotkeyService.IsTogglePauseRegistered)
        {
            issues.Add(string.Format(
                LocalizationService.GetString("Settings_Hotkey_Warning_Item"),
                LocalizationService.GetString("Settings_Hotkey_TogglePause"),
                HotkeyHelper.FormatDisplay(settings.HotkeyTogglePause)));
        }

        HotkeyRegistrationWarning = issues.Count == 0
            ? null
            : string.Format(
                LocalizationService.GetString("Settings_Hotkey_Warning_Summary"),
                string.Join("; ", issues));
    }

    private void RefreshHotkeyDisplays()
    {
        ToggleRecordingHotkeyDisplay = HotkeyCaptureTarget == HotkeyCaptureTarget.ToggleRecording
            ? LocalizationService.GetString("Settings_Hotkey_CapturePrompt")
            : HotkeyHelper.FormatDisplay(_settingsService.Current.HotkeyToggleRecording);

        TogglePauseHotkeyDisplay = HotkeyCaptureTarget == HotkeyCaptureTarget.TogglePause
            ? LocalizationService.GetString("Settings_Hotkey_CapturePrompt")
            : HotkeyHelper.FormatDisplay(_settingsService.Current.HotkeyTogglePause);
    }

    private void OnRecordingStateChanged(object? sender, RecordingState state)
    {
        IsRecording = state is RecordingState.Recording or RecordingState.Paused;
    }

    [RelayCommand]
    private async Task ResetToDefaultsAsync()
    {
        if (_recordingService.State is RecordingState.Recording or RecordingState.Paused)
        {
            await DialogHelper.ShowErrorAsync(
                LocalizationService.GetString("Settings_Reset_RecordingActive"),
                LocalizationService.GetString("Settings_ResetConfirm_Title"));
            return;
        }

        bool confirmed = await DialogHelper.ShowDangerConfirmAsync(
            LocalizationService.GetString("Settings_ResetConfirm_Message"),
            LocalizationService.GetString("Settings_ResetConfirm_Title"),
            confirmText: LocalizationService.GetString("Settings_ResetConfirm_Confirm"),
            cancelText: ContentDialogHelper.CancelText);

        if (!confirmed)
        {
            return;
        }

        string previousLanguage = _settingsService.Current.Language;
        if (!_settingsService.ResetToDefaults())
        {
            await DialogHelper.ShowErrorAsync(LocalizationService.GetString("Settings_SaveFailed"));
            return;
        }

        // The reset may have changed the hotkeys; re-register the defaults.
        _globalHotkeyService.Apply(
            _settingsService.Current.HotkeyToggleRecording,
            _settingsService.Current.HotkeyTogglePause);
        RefreshHotkeyDisplays();
        RefreshHotkeyRegistrationWarning();

        // The output folder may have changed; re-sync the recordings library.
        OutputFolderChangeResult folderResult = await _mainViewModel.RefreshOutputFolderAsync();
        if (folderResult.ErrorMessage is not null)
        {
            await DialogHelper.ShowErrorAsync(folderResult.ErrorMessage);
        }

        if (string.Equals(previousLanguage, _settingsService.Current.Language, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        bool restart = await DialogHelper.ShowConfirmAsync(
            LocalizationService.GetString("Settings_Reset_LanguageRestart_Message"),
            LocalizationService.GetString("Settings_Reset_LanguageRestart_Title"),
            confirmText: LocalizationService.GetString("Settings_LanguageRestart_Confirm"),
            cancelText: ContentDialogHelper.CancelText);

        if (restart)
        {
            LocalizationService.ApplyLanguage(_settingsService.Current.Language);
            await RestartApplicationAsync();
        }
    }

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        string? pickedPath = await OutputFolderHelper.PickFolderAsync();
        if (pickedPath is null)
        {
            return;
        }

        var result = await _mainViewModel.ChangeOutputFolderAsync(pickedPath);
        if (result.ErrorMessage is not null)
        {
            await DialogHelper.ShowErrorAsync(result.ErrorMessage);
            return;
        }

        if (!result.IsUnchanged)
        {
            OutputFolder = _mainViewModel.OutputFolderFull;
        }
    }

    private void NormalizeStoredFrameRateIfNeeded()
    {
        int normalized = RecordingSettingsHelper.NormalizeFrameRate(_settingsService.Current.FrameRate);
        if (_settingsService.Current.FrameRate == normalized)
        {
            return;
        }

        _settingsService.Current.FrameRate = normalized;
        _settingsService.Save();
    }

    private void PersistSettings()
    {
        if (_suppressPersist)
        {
            return;
        }

        _settingsService.Current.OutputFolder = OutputFolder;
        _settingsService.Current.FrameRate = RecordingSettingsHelper.FrameRateFromSettingsIndex(FrameRateIndex);
        _settingsService.Current.BitrateMode = (Models.VideoBitrateMode)BitrateModeIndex;
        if (BitrateModeIndex == (int)Models.VideoBitrateMode.Fixed)
        {
            int fixedKbps = (int)Math.Round(Math.Clamp(FixedBitrateMbps, 1, 100) * 1000);
            _settingsService.Current.BitrateKbps = fixedKbps;
        }
        else
        {
            RecordingSettingsHelper.ApplySettingsQualityIndex(_settingsService.Current, QualityIndex);
        }

        _settingsService.Current.AudioQualityIndex = AudioQualityIndex;

        // Only a loaded list may write these: a null entry means the list was never filled (a
        // failed enumeration), and writing it would drop the saved device on an unrelated change.
        // Choosing the "system default" entry is not that case - the entry itself is not null,
        // its Id is.
        if (SelectedMicrophoneDevice is not null)
        {
            _settingsService.Current.MicrophoneDeviceId = SelectedMicrophoneDevice.Id;
        }

        if (SelectedSystemAudioDevice is not null)
        {
            _settingsService.Current.SystemAudioDeviceId = SelectedSystemAudioDevice.Id;
        }

        _settingsService.Current.VideoCodecIndex = CodecIndex;
        _settingsService.Current.CaptureCursor = CaptureCursor;
        _settingsService.Current.CountdownSeconds = (int)Math.Round(CountdownSeconds);
        _settingsService.Current.NotificationEnabled = NotificationEnabled;
        _settingsService.Current.SegmentEnabled = SegmentEnabled;
        _settingsService.Current.SegmentLimitMode = (Models.SegmentLimitMode)Math.Clamp(SegmentLimitModeIndex, 0, 1);
        _settingsService.Current.SegmentMinutes = PresetAt(SegmentDurationPresetMinutes, SegmentDurationIndex);
        _settingsService.Current.SegmentSizeMb = PresetAt(SegmentSizePresetMb, SegmentSizeIndex);
        _settingsService.Current.MaxRecordingMinutes = PresetAt(MaxDurationPresetMinutes, MaxDurationIndex);
        _settingsService.Current.CloseMainWindowAction = (CloseMainWindowAction)CloseMainWindowActionIndex;
        if (!_settingsService.Save())
        {
            _ = DialogHelper.ShowErrorAsync(LocalizationService.GetString("Settings_SaveFailed"));
        }

        _mainViewModel.RefreshStorage();
    }

    private static async Task RestartApplicationAsync()
    {
        // Finalize any active recording before restarting; the process exit
        // below would otherwise abort the MP4 and leave it unplayable.
        var recordingService = Ioc.Default.GetRequiredService<IRecordingService>();
        if (recordingService.State is RecordingState.Recording or RecordingState.Paused)
        {
            await recordingService.StopAsync();
        }

        string? executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable))
        {
            return;
        }

        // Release the single-instance key so the new process can register it
        // before this one exits; otherwise the new process sees an existing
        // instance and quits immediately, leaving no window at all.
        AppInstance.GetCurrent().UnregisterKey();

        Process.Start(new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            WorkingDirectory = AppContext.BaseDirectory
        });
        Application.Current.Exit();
    }

    /// <summary>
    /// One pickable endpoint. <see cref="Id"/> is null for the "system default" entry, which is
    /// what a fresh install stores - no choice to make, and it follows the default device.
    /// A class rather than a record on purpose: entries are identified by reference, because a
    /// value-equal replacement would not raise a change notification and the combo box would be
    /// left showing nothing after the list is rebuilt.
    /// </summary>
    public sealed class AudioDeviceOption(string? id, string displayName)
    {
        public string? Id { get; } = id;

        public string DisplayName { get; } = displayName;

        public override string ToString() => DisplayName;
    }

    public ObservableCollection<AudioDeviceOption> MicrophoneDevices { get; } = new();

    public ObservableCollection<AudioDeviceOption> SystemAudioDevices { get; } = new();

    /// <summary>
    /// What the list shows. Only this view model writes it: the combo boxes in the page are
    /// bound one way and report the user's picks through <see cref="ApplyMicrophoneDevice"/>,
    /// so rebuilding a list can never be mistaken for a choice.
    /// </summary>
    [ObservableProperty]
    private AudioDeviceOption? _selectedMicrophoneDevice;

    [ObservableProperty]
    private AudioDeviceOption? _selectedSystemAudioDevice;

    /// <summary>
    /// False when the machine has no such endpoint at all. The list then holds a single entry
    /// saying so, and the combo box is disabled because there is nothing to choose.
    /// </summary>
    public bool HasMicrophoneDevices { get; private set; }

    public bool HasSystemAudioDevices { get; private set; }

    public void ApplyMicrophoneDevice(AudioDeviceOption? option) => ApplyDevice(option, systemAudio: false);

    public void ApplySystemAudioDevice(AudioDeviceOption? option) => ApplyDevice(option, systemAudio: true);

    private void ApplyDevice(AudioDeviceOption? option, bool systemAudio)
    {
        if (option is null)
        {
            // The entry is never "nothing": the list always holds the default entry, so a null
            // selection can only be a rebuilt list, not a choice.
            return;
        }

        if (systemAudio)
        {
            SelectedSystemAudioDevice = option;
        }
        else
        {
            SelectedMicrophoneDevice = option;
        }

        string? storedId = systemAudio
            ? _settingsService.Current.SystemAudioDeviceId
            : _settingsService.Current.MicrophoneDeviceId;
        if (!string.Equals(storedId, option.Id, StringComparison.OrdinalIgnoreCase))
        {
            PersistSettings();
        }
    }

    /// <summary>
    /// Brings both device lists in line with the endpoints that exist now. Called when the page
    /// is constructed and every time it is shown, because devices can be plugged in or removed
    /// while the app runs. A saved device that is no longer connected is dropped and the setting
    /// itself goes back to "system default", so what the list shows and what is stored never
    /// disagree.
    /// </summary>
    public void RefreshAudioDevices()
    {
        var settings = _settingsService.Current;
        var captureDevices = AudioDeviceHelper.GetActiveCaptureDevices();
        var renderDevices = AudioDeviceHelper.GetActiveRenderDevices();

        // A null list means the query failed, not that the machine has no such endpoint: the row
        // is then left exactly as it is, so a transient failure cannot drop the saved device.
        if (captureDevices is not null)
        {
            var (microphone, missing) = SyncDeviceOptions(
                MicrophoneDevices,
                captureDevices,
                settings.MicrophoneDeviceId);
            SetSelectedDevice(systemAudio: false, microphone);
            SetDeviceAvailability(systemAudio: false, captureDevices.Count > 0);

            if (missing)
            {
                // The value is cleared before saving, so it is right even if this save is
                // suppressed (an external change is in flight) and lands with the next one.
                settings.MicrophoneDeviceId = null;
                PersistSettings();
            }
        }

        if (renderDevices is not null)
        {
            var (systemAudio, missing) = SyncDeviceOptions(
                SystemAudioDevices,
                renderDevices,
                settings.SystemAudioDeviceId);
            SetSelectedDevice(systemAudio: true, systemAudio);
            SetDeviceAvailability(systemAudio: true, renderDevices.Count > 0);

            if (missing)
            {
                settings.SystemAudioDeviceId = null;
                PersistSettings();
            }
        }
    }

    /// <summary>
    /// Applies a selection through null on purpose: a combo box drops its selection while its list
    /// is rebuilt, and re-assigning an entry it treats as the same value raises no notification at
    /// all - either way the selection would stay blank. Both assignments are synchronous, so
    /// nothing is painted in between.
    /// </summary>
    private void SetSelectedDevice(bool systemAudio, AudioDeviceOption option)
    {
        if (systemAudio)
        {
            SelectedSystemAudioDevice = null;
            SelectedSystemAudioDevice = option;
        }
        else
        {
            SelectedMicrophoneDevice = null;
            SelectedMicrophoneDevice = option;
        }
    }

    private void SetDeviceAvailability(bool systemAudio, bool available)
    {
        if (systemAudio)
        {
            if (HasSystemAudioDevices == available)
            {
                return;
            }

            HasSystemAudioDevices = available;
            OnPropertyChanged(nameof(HasSystemAudioDevices));
        }
        else
        {
            if (HasMicrophoneDevices == available)
            {
                return;
            }

            HasMicrophoneDevices = available;
            OnPropertyChanged(nameof(HasMicrophoneDevices));
        }
    }

    /// <summary>
    /// Updates a list in place when it is already correct, and rebuilds it otherwise. Rebuilding
    /// clears the combo box selection, so an unchanged list is left completely alone - and when
    /// it is rebuilt, the returned entry is one of the new instances, which is what the combo box
    /// can select.
    /// </summary>
    private static (AudioDeviceOption Option, bool WasMissing) SyncDeviceOptions(
        ObservableCollection<AudioDeviceOption> target,
        IReadOnlyList<AudioDeviceHelper.AudioDeviceInfo> devices,
        string? storedId)
    {
        // With no endpoint at all, "system default" would be a lie: the row then says there is
        // nothing to choose, and the page disables it.
        string firstLabel = LocalizationService.GetString(
            devices.Count > 0 ? "Settings_AudioDevice_Default" : "Settings_AudioDevice_None");

        if (target.Count == 0 ||
            !string.Equals(target[0].DisplayName, firstLabel, StringComparison.Ordinal) ||
            !MatchesCurrentDevices(target, devices))
        {
            target.Clear();
            target.Add(new AudioDeviceOption(null, firstLabel));

            foreach (var device in devices)
            {
                target.Add(new AudioDeviceOption(device.Id, device.Name));
            }
        }

        foreach (var option in target)
        {
            if (string.Equals(option.Id, storedId, StringComparison.OrdinalIgnoreCase))
            {
                return (option, false);
            }
        }

        if (string.IsNullOrEmpty(storedId))
        {
            // Defensive: the first entry is the default one and always matches a null id.
            return (target[0], false);
        }

        Log.Info($"Saved audio device '{storedId}' is not connected; the setting goes back to the system default.");
        return (target[0], true);
    }

    /// <summary>True when the list already holds exactly the first entry plus these devices.</summary>
    private static bool MatchesCurrentDevices(
        ObservableCollection<AudioDeviceOption> target,
        IReadOnlyList<AudioDeviceHelper.AudioDeviceInfo> devices)
    {
        if (target.Count != devices.Count + 1)
        {
            return false;
        }

        for (int i = 0; i < devices.Count; i++)
        {
            var option = target[i + 1];
            if (!string.Equals(option.Id, devices[i].Id, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(option.DisplayName, devices[i].Name, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    public sealed record LanguageOption(string Code, string DisplayName);
}
