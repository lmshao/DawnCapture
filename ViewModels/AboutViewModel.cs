using CommunityToolkit.Mvvm.ComponentModel;
using DawnCapture.Services;
using System;
using System.Reflection;

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
}
