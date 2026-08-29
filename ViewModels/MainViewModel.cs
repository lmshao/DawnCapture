using CommunityToolkit.Mvvm.ComponentModel;

namespace DawnCapture.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string _appTitle = "DawnCapture";
}
