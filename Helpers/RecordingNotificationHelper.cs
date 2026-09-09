using System;
using System.IO;
using DawnCapture.Services;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace DawnCapture.Helpers;

public static class RecordingNotificationHelper
{
    public const string OpenFolderAction = "openFolder";

    public static void TryShowSaved(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        try
        {
            string fileName = Path.GetFileName(filePath);
            string? folder = Path.GetDirectoryName(filePath);
            var builder = new AppNotificationBuilder()
                .AddText(LocalizationService.GetString("Notification_RecordingSaved"))
                .AddText(fileName);

            if (!string.IsNullOrWhiteSpace(folder))
            {
                builder.AddButton(new AppNotificationButton(
                        LocalizationService.GetString("Notification_OpenFolder"))
                    .AddArgument("action", OpenFolderAction)
                    .AddArgument("path", folder));
            }

            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch (Exception ex)
        {
            Log.Info($"Completion notification failed: {ex.Message}");
        }
    }
}
