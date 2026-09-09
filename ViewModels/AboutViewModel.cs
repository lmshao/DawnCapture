using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DawnCapture.Helpers;
using DawnCapture.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace DawnCapture.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    private const string AuthorEmailAddress = "lmshao@163.com";

    public AboutViewModel()
    {
        VersionLabel = FormatVersionLabel(Assembly.GetExecutingAssembly().GetName().Version);
    }

    public string Tagline => LocalizationService.GetString("About_Title");

    public string Description => LocalizationService.GetString("About_Description");

    public string AuthorPrefix => LocalizationService.GetString("About_AuthorPrefix");

    public string AuthorEmail => AuthorEmailAddress;

    public Uri AuthorMailUri { get; } = new($"mailto:{AuthorEmailAddress}");

    public string TrustLocalText => LocalizationService.GetString("About_Trust_Local");

    public string TrustNoAccountText => LocalizationService.GetString("About_Trust_NoAccount");

    public string TrustWindowsText => LocalizationService.GetString("About_Trust_Windows11");

    [ObservableProperty]
    private string _versionLabel = string.Empty;

    private static string FormatVersionLabel(Version? version)
    {
        if (version is null)
        {
            return LocalizationService.GetString("About_ProductName");
        }

        return string.Format(
            LocalizationService.GetString("About_Version"),
            version.ToString(3));
    }

    [RelayCommand]
    private async Task OpenLicensesAsync()
    {
        string licensePath = Path.Combine(AppContext.BaseDirectory, "LICENSE");
        if (!File.Exists(licensePath))
        {
            await DialogHelper.ShowErrorAsync(LocalizationService.GetString("About_LicensesMissing"));
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(licensePath)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to open license file '{licensePath}'", ex);
            await DialogHelper.ShowErrorAsync(LocalizationService.GetString("About_LicensesOpenFailed"));
        }
    }

    [RelayCommand]
    private async Task OpenLogsAsync()
    {
        string? logsFolder = Path.GetDirectoryName(Log.FilePath);
        if (string.IsNullOrWhiteSpace(logsFolder) || !Directory.Exists(logsFolder))
        {
            await DialogHelper.ShowErrorAsync(LocalizationService.GetString("About_LogsMissing"));
            return;
        }

        try
        {
            OutputFolderHelper.OpenInExplorer(logsFolder);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to open logs folder '{logsFolder}'", ex);
            await DialogHelper.ShowErrorAsync(LocalizationService.GetString("About_LogsOpenFailed"));
        }
    }
}
