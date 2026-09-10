using DawnCapture.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DawnCapture.Helpers;

public static class OutputFolderHelper
{
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "DawnCapture");

    public static string Resolve(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return DefaultPath;
        }

        try
        {
            return Path.GetFullPath(configuredPath.Trim());
        }
        catch
        {
            return DefaultPath;
        }
    }

    public static string Resolve(ISettingsService settingsService) =>
        Resolve(settingsService.Current.OutputFolder);

    public static bool TryValidate(string path, out string normalizedPath, out string? errorResourceKey)
    {
        normalizedPath = string.Empty;
        errorResourceKey = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            errorResourceKey = "OutputFolder_InvalidPath";
            return false;
        }

        try
        {
            normalizedPath = Path.GetFullPath(path.Trim());
        }
        catch
        {
            errorResourceKey = "OutputFolder_InvalidPath";
            return false;
        }

        try
        {
            Directory.CreateDirectory(normalizedPath);
            string probeFile = Path.Combine(normalizedPath, $".dawncapture_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probeFile, string.Empty);
            File.Delete(probeFile);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            errorResourceKey = "OutputFolder_NotWritable";
            return false;
        }
        catch (Exception ex)
        {
            Log.Error($"Output folder validation failed for '{normalizedPath}'", ex);
            errorResourceKey = "OutputFolder_InvalidPath";
            return false;
        }
    }

    public static async Task<string?> PickFolderAsync()
    {
        if (App.MainWindow is null)
        {
            return null;
        }

        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        picker.SuggestedStartLocation = PickerLocationId.VideosLibrary;

        var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    public static void OpenInExplorer(string folder)
    {
        string resolved = Resolve(folder);

        try
        {
            Directory.CreateDirectory(resolved);
            Process.Start(new ProcessStartInfo("explorer.exe", resolved)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to open output folder '{resolved}'", ex);
            throw;
        }
    }
}
