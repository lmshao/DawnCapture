using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DawnCapture.Helpers;
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

    public SettingsViewModel(ISettingsService settingsService, MainViewModel mainViewModel)
    {
        _settingsService = settingsService;
        _mainViewModel = mainViewModel;
        _outputFolder = _settingsService.Current.OutputFolder;
        _outputFolderDisplay = PathDisplayHelper.MiddleEllipsis(_outputFolder, 44);
        _frameRateIndex = RecordingSettingsHelper.FrameRateToSettingsIndex(_settingsService.Current.FrameRate);
        _qualityIndex = _settingsService.Current.QualityIndex;
        _codecIndex = _settingsService.Current.VideoCodecIndex;
        _audioQualityIndex = _settingsService.Current.AudioQualityIndex;
        _captureCursor = _settingsService.Current.CaptureCursor;
        _countdownEnabled = _settingsService.Current.CountdownEnabled;
        _notificationEnabled = _settingsService.Current.NotificationEnabled;
        _selectedLanguage = Languages.FirstOrDefault(
            option => option.Code == _settingsService.Current.Language) ?? Languages[0];

        _mainViewModel.OutputFolderChanged += (_, path) => OutputFolder = path;
        _settingsService.SettingsChanged += OnExternalSettingsChanged;
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
            CodecIndex = settings.VideoCodecIndex;
            AudioQualityIndex = settings.AudioQualityIndex;
            CaptureCursor = settings.CaptureCursor;
            CountdownEnabled = settings.CountdownEnabled;
            NotificationEnabled = settings.NotificationEnabled;
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

    private void PersistSettings()
    {
        if (_suppressPersist)
        {
            return;
        }

        _settingsService.Current.OutputFolder = OutputFolder;
        _settingsService.Current.FrameRate = RecordingSettingsHelper.FrameRateFromSettingsIndex(
            FrameRateIndex,
            _settingsService.Current.FrameRate);
        RecordingSettingsHelper.ApplySettingsQualityIndex(_settingsService.Current, QualityIndex);
        _settingsService.Current.AudioQualityIndex = AudioQualityIndex;
        _settingsService.Current.VideoCodecIndex = CodecIndex;
        _settingsService.Current.CaptureCursor = CaptureCursor;
        _settingsService.Current.CountdownEnabled = CountdownEnabled;
        _settingsService.Current.NotificationEnabled = NotificationEnabled;
        _settingsService.Save();
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
