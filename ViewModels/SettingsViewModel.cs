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
        _frameRateIndex = _settingsService.Current.FrameRate >= 60 ? 1 : 0;
        _captureCursor = _settingsService.Current.CaptureCursor;
        _selectedLanguage = Languages.FirstOrDefault(
            option => option.Code == _settingsService.Current.Language) ?? Languages[0];

        _mainViewModel.OutputFolderChanged += (_, path) => OutputFolder = path;
    }

    public IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new(LocalizationService.SystemLanguage, LocalizationService.GetString("Language_System")),
        new(LocalizationService.SimplifiedChinese, LocalizationService.GetString("Language_Chinese")),
        new(LocalizationService.English, LocalizationService.GetString("Language_English"))
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
        if (string.Equals(value, _settingsService.Current.OutputFolder, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        PersistSettings();
    }

    partial void OnFrameRateIndexChanged(int value) => PersistSettings();

    partial void OnCaptureCursorChanged(bool value) => PersistSettings();

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
        _settingsService.Current.OutputFolder = OutputFolder;
        _settingsService.Current.FrameRate = FrameRateIndex == 1 ? 60 : 30;
        _settingsService.Current.CaptureCursor = CaptureCursor;
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
