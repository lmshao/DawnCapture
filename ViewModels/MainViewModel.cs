using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DawnCapture.Helpers;
using DawnCapture.Models;
using DawnCapture.Services;
using System;
using System.IO;
using System.Threading.Tasks;

namespace DawnCapture.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const int LowStorageGb = 20;
    private readonly ISettingsService _settingsService;
    private readonly IRecordingCatalogService _catalogService;

    public MainViewModel(ISettingsService settingsService, IRecordingCatalogService catalogService)
    {
        _settingsService = settingsService;
        _catalogService = catalogService;
        RefreshStorage();
    }

    public event EventHandler<string>? OutputFolderChanged;

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
        string folder = OutputFolderHelper.Resolve(_settingsService);
        OutputFolderFull = folder;
        OutputFolderSummary = PathDisplayHelper.CompactFolderSummary(
            folder,
            OutputFolderHelper.DefaultPath,
            LocalizationService.GetString("OutputFolder_DefaultSummary"));

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

    public async Task<OutputFolderChangeResult> ChangeOutputFolderAsync(string newPath)
    {
        if (!OutputFolderHelper.TryValidate(newPath, out string normalizedPath, out string? errorResourceKey))
        {
            return OutputFolderChangeResult.Failed(LocalizationService.GetString(errorResourceKey!));
        }

        if (string.Equals(normalizedPath, OutputFolderHelper.Resolve(_settingsService), StringComparison.OrdinalIgnoreCase))
        {
            return OutputFolderChangeResult.Unchanged();
        }

        _settingsService.Current.OutputFolder = normalizedPath;
        _settingsService.Save();
        RefreshStorage();

        try
        {
            await _catalogService.SyncLibraryAsync(normalizedPath);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to sync recordings library after output folder change to '{normalizedPath}'", ex);
            return OutputFolderChangeResult.Failed(LocalizationService.GetString("OutputFolder_SyncFailed"));
        }

        OutputFolderChanged?.Invoke(this, normalizedPath);
        return OutputFolderChangeResult.Success();
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        try
        {
            OutputFolderHelper.OpenInExplorer(OutputFolderFull);
        }
        catch
        {
            _ = DialogHelper.ShowErrorAsync(LocalizationService.GetString("OutputFolder_OpenFailed"));
        }
    }

    [RelayCommand]
    private async Task ChangeOutputFolderAsync()
    {
        string? pickedPath = await OutputFolderHelper.PickFolderAsync(OutputFolderFull);
        if (pickedPath is null)
        {
            return;
        }

        OutputFolderChangeResult result = await ChangeOutputFolderAsync(pickedPath);
        if (result.ErrorMessage is not null)
        {
            await DialogHelper.ShowErrorAsync(result.ErrorMessage);
        }
    }
}
