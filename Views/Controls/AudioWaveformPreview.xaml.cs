using System.Collections.ObjectModel;
using DawnCapture.Helpers;
using DawnCapture.Models;
using Microsoft.UI.Xaml.Controls;

namespace DawnCapture.Views.Controls;

public sealed partial class AudioWaveformPreview : UserControl
{
    public ObservableCollection<AudioWaveformBar> Bars { get; } = AudioWaveformHelper.CreateDefaultBars();

    public AudioWaveformPreview()
    {
        InitializeComponent();
    }
}
