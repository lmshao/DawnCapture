using CommunityToolkit.Mvvm.ComponentModel;

namespace DawnCapture.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    public string AppName => "DawnCapture";

    public string Version => "0.1.0";

    public string Description => "基于 WinUI 3 的 Windows 原生屏幕录制工具";
}
