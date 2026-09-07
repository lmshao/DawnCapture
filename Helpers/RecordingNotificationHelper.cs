using System;
using System.IO;
using DawnCapture.Services;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace DawnCapture.Helpers;

public static class RecordingNotificationHelper
{
    public static void TryShowSaved(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        try
        {
            string fileName = Path.GetFileName(filePath);
            var notification = new AppNotificationBuilder()
                .AddText(LocalizationService.GetString("Notification_RecordingSaved"))
                .AddText(fileName)
                .BuildNotification();

            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception ex)
        {
            Log.Info($"Completion notification failed: {ex.Message}");
        }
    }
}
