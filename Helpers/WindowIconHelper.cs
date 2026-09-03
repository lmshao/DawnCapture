using System;
using System.IO;
using DawnCapture.Services;
using Microsoft.UI.Xaml;

namespace DawnCapture.Helpers;

public static class WindowIconHelper
{
    public static void Apply(Window window)
    {
        try
        {
            string iconPath = Path.Combine(
                AppContext.BaseDirectory,
                PackageAssets.AppIcon.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(iconPath))
            {
                Log.Info($"Window icon not found: {iconPath}");
                return;
            }

            window.AppWindow.SetIcon(iconPath);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to set window icon", ex);
        }
    }
}