using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DawnCapture.Helpers;
using DawnCapture.Services;
using System;
using System.IO;

namespace DawnCapture.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const int LowStorageGb = 20;
    private readonly ISettingsService _settingsService;

    public MainViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        RefreshStorage();
    }

    [ObservableProperty]
    private string _appStatusText = LocalizationService.GetString("Status_Ready");

    [ObservableProperty]
    private string _storageLabel = LocalizationService.GetString("Storage_Label");

    [ObservableProperty]
    private string _storageAvailable = string.Empty;

    [ObservableProperty]
    private bool _isStorageLow;

    [ObservableProperty]
    private string _outputFolderFull = string.Empty;

    [ObservableProperty]
    private string _outputFolderSummary = string.Empty;

    public void RefreshStorage()
    {
        string folder = _settingsService.Current.OutputFolder;
        OutputFolderFull = folder;
        OutputFolderSummary = PathDisplayHelper.CompactFolderSummary(
            folder,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "DawnCapture"));

        try
        {
            string root = Path.GetPathRoot(folder) ?? "C:\\";
            var drive = new DriveInfo(root);
            string letter = root.TrimEnd('\\').TrimEnd(':');
            StorageLabel = string.Format(LocalizationService.GetString("Storage_LocalDisk"), letter);

            if (drive.IsReady)
            {
                long availableGb = drive.AvailableFreeSpace / (1024L * 1024L * 1024L);
                StorageAvailable = string.Format(LocalizationService.GetString("Storage_Available"), availableGb);
                IsStorageLow = availableGb <= LowStorageGb;
            }
            else
            {
                StorageAvailable = LocalizationService.GetString("Storage_Unavailable");
                IsStorageLow = false;
            }
        }
        catch
        {
            StorageLabel = LocalizationService.GetString("Storage_Label");
            StorageAvailable = LocalizationService.GetString("Storage_Unavailable");
            IsStorageLow = false;
        }
    }

    [RelayCommand]
    private void OpenFolderFlyout()
    {
        // UI shell: flyout is opened from view code-behind.
    }
}
