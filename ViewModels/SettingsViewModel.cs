using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DawnCapture.Services;

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
    }

    [ObservableProperty]
    private string _outputFolder;

    [ObservableProperty]
    private double _frameRate;

    [ObservableProperty]
    private double _bitrateKbps;

    [ObservableProperty]
    private bool _captureCursor;

    [ObservableProperty]
    private string _savedMessage = string.Empty;

    [RelayCommand]
    private void Save()
    {
        _settingsService.Current.OutputFolder = OutputFolder;
        _settingsService.Current.FrameRate = Math.Max(1, (int)Math.Round(FrameRate));
        _settingsService.Current.BitrateKbps = Math.Max(100, (int)Math.Round(BitrateKbps));
        _settingsService.Current.CaptureCursor = CaptureCursor;
        _settingsService.Save();
        SavedMessage = "已保存";
    }
}
