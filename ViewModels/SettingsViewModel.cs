using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DawnCapture.Helpers;
using DawnCapture.Models;
using DawnCapture.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.UI.Xaml;
using System.Threading.Tasks;

namespace DawnCapture.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly MainViewModel _mainViewModel;
    private readonly IGlobalHotkeyService _globalHotkeyService;

    public SettingsViewModel(
        ISettingsService settingsService,
        MainViewModel mainViewModel,
        IGlobalHotkeyService globalHotkeyService)
    {
        _settingsService = settingsService;
        _mainViewModel = mainViewModel;
        _globalHotkeyService = globalHotkeyService;
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
        _countdownEnabled = _settingsService.Current.CountdownEnabled;
        _notificationEnabled = _settingsService.Current.NotificationEnabled;
        _selectedLanguage = Languages.FirstOrDefault(
            option => option.Code == _settingsService.Current.Language) ?? Languages[0];

        _mainViewModel.OutputFolderChanged += (_, path) => OutputFolder = path;
        _settingsService.SettingsChanged += OnExternalSettingsChanged;
        RefreshHotkeyDisplays();
        RefreshHotkeyRegistrationWarning();
    }

    private bool _suppressPersist;

    private void OnExternalSettingsChanged(object? sender, EventArgs e)
    {
        _suppressPersist = true;
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
            CountdownEnabled = settings.CountdownEnabled;
            NotificationEnabled = settings.NotificationEnabled;
            RefreshHotkeyDisplays();
            RefreshHotkeyRegistrationWarning();
        }
        finally
        {
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
    private int _audioQualityIndex = 1;

    [ObservableProperty]
    private bool _captureCursor;

    [ObservableProperty]
    private bool _countdownEnabled = true;

    [ObservableProperty]
    private bool _notificationEnabled = true;

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

    partial void OnCountdownEnabledChanged(bool value)
    {
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

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        if (value is null)
        {
            return;
        }

        bool languageChanged = _settingsService.Current.Language != value.Code;
        _settingsService.Current.Language = value.Code;
        _settingsService.Save();

        if (languageChanged)
        {
            LocalizationService.ApplyLanguage(value.Code);
            RestartApplication();
        }
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

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        string? pickedPath = await OutputFolderHelper.PickFolderAsync(OutputFolder);
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
        _settingsService.Current.VideoCodecIndex = CodecIndex;
        _settingsService.Current.CaptureCursor = CaptureCursor;
        _settingsService.Current.CountdownEnabled = CountdownEnabled;
        _settingsService.Current.NotificationEnabled = NotificationEnabled;
        if (!_settingsService.Save())
        {
            _ = DialogHelper.ShowErrorAsync(LocalizationService.GetString("Settings_SaveFailed"));
        }

        _mainViewModel.RefreshStorage();
    }

    private static void RestartApplication()
    {
        string? executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            WorkingDirectory = AppContext.BaseDirectory
        });
        Application.Current.Exit();
    }

    public sealed record LanguageOption(string Code, string DisplayName);
}
