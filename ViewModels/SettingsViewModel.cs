using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DawnCapture.Services;
using Microsoft.UI.Xaml;

namespace DawnCapture.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;

    public SettingsViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _outputFolder = _settingsService.Current.OutputFolder;
        _frameRate = _settingsService.Current.FrameRate;
        _bitrateKbps = _settingsService.Current.BitrateKbps;
        _captureCursor = _settingsService.Current.CaptureCursor;
        _selectedLanguage = Languages.FirstOrDefault(
            option => option.Code == _settingsService.Current.Language) ?? Languages[0];
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
    private double _frameRate;

    [ObservableProperty]
    private double _bitrateKbps;

    [ObservableProperty]
    private bool _captureCursor;

    [ObservableProperty]
    private LanguageOption _selectedLanguage = null!;

    [ObservableProperty]
    private string _savedMessage = string.Empty;

    [RelayCommand]
    private void Save()
    {
        _settingsService.Current.OutputFolder = OutputFolder;
        _settingsService.Current.FrameRate = Math.Max(1, (int)Math.Round(FrameRate));
        _settingsService.Current.BitrateKbps = Math.Max(100, (int)Math.Round(BitrateKbps));
        _settingsService.Current.CaptureCursor = CaptureCursor;
        bool languageChanged = _settingsService.Current.Language != SelectedLanguage.Code;
        _settingsService.Current.Language = SelectedLanguage.Code;
        _settingsService.Save();

        if (languageChanged)
        {
            LocalizationService.ApplyLanguage(SelectedLanguage.Code);
            RestartApplication();
            return;
        }

        SavedMessage = LocalizationService.GetString("Status_Saved");
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
