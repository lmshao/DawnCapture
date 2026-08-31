using CommunityToolkit.Mvvm.ComponentModel;
using DawnCapture.Services;

namespace DawnCapture.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    public string AppName => "DawnCapture";

    public string Version => "0.1.0";

    public string Description => LocalizationService.GetString("About_Description");
}
