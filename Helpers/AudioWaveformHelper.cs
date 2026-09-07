using System.Collections.ObjectModel;
using DawnCapture.Models;

namespace DawnCapture.Helpers;

public static class AudioWaveformHelper
{
    public static readonly double[] DefaultHeights =
    [
        20, 34, 56, 30, 67, 44, 77, 38, 59, 31, 72, 48, 26, 52, 36, 21
    ];

    public static ObservableCollection<AudioWaveformBar> CreateDefaultBars()
    {
        ObservableCollection<AudioWaveformBar> bars = new();
        foreach (double height in DefaultHeights)
        {
            bars.Add(new AudioWaveformBar(height));
        }

        return bars;
    }
}
