using CommunityToolkit.Mvvm.ComponentModel;

namespace DawnCapture.Models;

public partial class AudioWaveformBar : ObservableObject
{
    public AudioWaveformBar(double height)
    {
        _height = height;
    }

    [ObservableProperty]
    private double _height;
}
