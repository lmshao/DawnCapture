using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DawnCapture.Services;
using System.Reflection;

namespace DawnCapture.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    public AboutViewModel()
    {
        VersionLabel = string.Format(
            LocalizationService.GetString("About_Version"),
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");
        AuthorLine = string.Format(
            LocalizationService.GetString("About_Author"),
            "lmshao@163.com");
    }

    public string Title => LocalizationService.GetString("About_Title");

    public string Description => LocalizationService.GetString("About_Description");

    [ObservableProperty]
    private string _versionLabel = string.Empty;

    [ObservableProperty]
    private string _authorLine = string.Empty;

    [RelayCommand]
    private void OpenLicenses()
    {
        // UI shell only.
    }
}
