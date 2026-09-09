using DawnCapture.Services;
using System;
using System.IO;

namespace DawnCapture.Helpers;

public static class StorageSpaceHelper
{
    /// <summary>Minimum free space required before starting a recording session.</summary>
    public const long MinimumRecordingBytes = 512L * 1024 * 1024;

    public static bool TryEnsureSpaceForRecording(string folder, out string? errorMessage)
    {
        errorMessage = null;

        try
        {
            string resolvedFolder = OutputFolderHelper.Resolve(folder);
            string root = Path.GetPathRoot(Path.GetFullPath(resolvedFolder)) ?? "C:\\";
            var drive = new DriveInfo(root);

            if (!drive.IsReady)
            {
                errorMessage = LocalizationService.GetString("Storage_Check_Unavailable");
                return false;
            }

            if (drive.AvailableFreeSpace < MinimumRecordingBytes)
            {
                long availableMb = drive.AvailableFreeSpace / (1024 * 1024);
                errorMessage = string.Format(
                    LocalizationService.GetString("Storage_Check_Insufficient"),
                    availableMb);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Storage check failed for '{folder}'", ex);
            errorMessage = LocalizationService.GetString("Storage_Check_Unavailable");
            return false;
        }
    }
}
