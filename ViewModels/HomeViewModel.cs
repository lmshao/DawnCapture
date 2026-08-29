using CommunityToolkit.Mvvm.ComponentModel;
using DawnCapture.Services;

namespace DawnCapture.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly IRecordingService _recordingService;

    public HomeViewModel(IRecordingService recordingService)
    {
        _recordingService = recordingService;
    }

    [ObservableProperty]
    private string _statusText = "准备就绪";
}
